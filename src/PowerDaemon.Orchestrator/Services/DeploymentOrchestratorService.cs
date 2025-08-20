using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Orchestrator.Models;
using PowerDaemon.Orchestrator.Configuration;
using PowerDaemon.Messaging.Services;
using PowerDaemon.Messaging.Messages;
using PowerDaemon.Cache.Services;
using System.Collections.Concurrent;

namespace PowerDaemon.Orchestrator.Services;

public class DeploymentOrchestratorService : IDeploymentOrchestrator, IDisposable
{
    private readonly ILogger<DeploymentOrchestratorService> _logger;
    private readonly OrchestratorConfiguration _config;
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowExecutor _workflowExecutor;
    private readonly IMessagePublisher _messagePublisher;
    private readonly ICacheService _cacheService;
    private readonly Dictionary<DeploymentStrategy, IDeploymentStrategy> _strategies;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningWorkflows;
    private readonly Timer _healthCheckTimer;
    private readonly DateTime _startupTime;
    private bool _disposed;

    public DeploymentOrchestratorService(
        ILogger<DeploymentOrchestratorService> logger,
        IOptions<OrchestratorConfiguration> config,
        IWorkflowRepository workflowRepository,
        IWorkflowExecutor workflowExecutor,
        IMessagePublisher messagePublisher,
        ICacheService cacheService,
        IEnumerable<IDeploymentStrategy> strategies)
    {
        _logger = logger;
        _config = config.Value;
        _workflowRepository = workflowRepository;
        _workflowExecutor = workflowExecutor;
        _messagePublisher = messagePublisher;
        _cacheService = cacheService;
        _runningWorkflows = new ConcurrentDictionary<string, CancellationTokenSource>();
        _startupTime = DateTime.UtcNow;

        _strategies = strategies.ToDictionary(s => s.StrategyType, s => s);
        
        _healthCheckTimer = new Timer(PerformHealthCheck, null, 
            TimeSpan.FromSeconds(_config.HealthCheckIntervalSeconds), 
            TimeSpan.FromSeconds(_config.HealthCheckIntervalSeconds));
            
        _logger.LogInformation("Deployment orchestrator service initialized with {StrategyCount} strategies", 
            _strategies.Count);
    }

    public async Task<DeploymentWorkflow> CreateWorkflowAsync(DeploymentWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_strategies.TryGetValue(request.Strategy, out var strategy))
            {
                throw new ArgumentException($"Unsupported deployment strategy: {request.Strategy}");
            }

            var isValidConfig = await strategy.ValidateConfigurationAsync(request.Configuration, cancellationToken);
            if (!isValidConfig)
            {
                throw new ArgumentException("Invalid deployment configuration for selected strategy");
            }

            var workflow = new DeploymentWorkflow
            {
                Name = request.Name,
                Description = request.Description,
                Strategy = request.Strategy,
                TargetServers = request.TargetServers,
                ServiceName = request.ServiceName,
                Version = request.Version,
                PackageUrl = request.PackageUrl,
                Configuration = request.Configuration,
                RollbackConfiguration = request.RollbackConfiguration,
                CreatedBy = request.CreatedBy,
                Metadata = request.Metadata,
                Timeout = request.Timeout ?? TimeSpan.FromHours(2)
            };

            var phases = await strategy.CreatePhasesAsync(request, cancellationToken);
            workflow.Phases = phases;

            var workflowId = await _workflowRepository.CreateWorkflowAsync(workflow, cancellationToken);
            workflow.Id = workflowId;

            await AddWorkflowEventAsync(workflow.Id, WorkflowEventType.Created, 
                $"Workflow created with strategy {request.Strategy}", request.CreatedBy);

            await _cacheService.SetAsync($"workflow:{workflowId}", workflow, TimeSpan.FromHours(24));

            _logger.LogInformation("Created workflow {WorkflowId} for service {ServiceName} with strategy {Strategy}",
                workflowId, request.ServiceName, request.Strategy);

            return workflow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workflow for service {ServiceName}", request.ServiceName);
            throw;
        }
    }

    public async Task<bool> StartWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        try
        {
            var workflow = await GetWorkflowAsync(workflowId, cancellationToken);
            if (workflow == null)
            {
                _logger.LogWarning("Workflow {WorkflowId} not found", workflowId);
                return false;
            }

            if (workflow.Status != WorkflowStatus.Created && workflow.Status != WorkflowStatus.Queued)
            {
                _logger.LogWarning("Cannot start workflow {WorkflowId} in status {Status}", workflowId, workflow.Status);
                return false;
            }

            var lockKey = $"workflow-lock:{workflowId}";
            using var workflowLock = await _cacheService.AcquireLockAsync(lockKey, TimeSpan.FromMinutes(5));
            if (workflowLock == null)
            {
                _logger.LogWarning("Could not acquire lock for workflow {WorkflowId}", workflowId);
                return false;
            }

            workflow.Status = WorkflowStatus.Running;
            workflow.StartedAt = DateTime.UtcNow;
            await _workflowRepository.UpdateWorkflowAsync(workflow, cancellationToken);

            await AddWorkflowEventAsync(workflowId, WorkflowEventType.Started, "Workflow execution started");

            var cts = new CancellationTokenSource();
            _runningWorkflows[workflowId] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    var success = await _workflowExecutor.ExecuteWorkflowAsync(workflow, cts.Token);
                    
                    workflow.Status = success ? WorkflowStatus.Completed : WorkflowStatus.Failed;
                    workflow.CompletedAt = DateTime.UtcNow;
                    workflow.ProgressPercent = success ? 100 : workflow.ProgressPercent;
                    
                    await _workflowRepository.UpdateWorkflowAsync(workflow, CancellationToken.None);
                    await AddWorkflowEventAsync(workflowId, success ? WorkflowEventType.Completed : WorkflowEventType.Failed,
                        success ? "Workflow completed successfully" : "Workflow execution failed");

                    await _cacheService.SetAsync($"workflow:{workflowId}", workflow, TimeSpan.FromHours(24));
                }
                catch (OperationCanceledException)
                {
                    workflow.Status = WorkflowStatus.Cancelled;
                    workflow.CompletedAt = DateTime.UtcNow;
                    await _workflowRepository.UpdateWorkflowAsync(workflow, CancellationToken.None);
                    await AddWorkflowEventAsync(workflowId, WorkflowEventType.Cancelled, "Workflow execution cancelled");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in workflow {WorkflowId} execution", workflowId);
                    workflow.Status = WorkflowStatus.Failed;
                    workflow.CompletedAt = DateTime.UtcNow;
                    workflow.Errors.Add(new WorkflowError
                    {
                        Severity = ErrorSeverity.Critical,
                        Message = "Unhandled execution error",
                        Details = ex.Message
                    });
                    await _workflowRepository.UpdateWorkflowAsync(workflow, CancellationToken.None);
                    await AddWorkflowEventAsync(workflowId, WorkflowEventType.Failed, $"Workflow failed: {ex.Message}");
                }
                finally
                {
                    _runningWorkflows.TryRemove(workflowId, out _);
                    cts.Dispose();
                }
            }, cts.Token);

            _logger.LogInformation("Started workflow {WorkflowId} execution", workflowId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start workflow {WorkflowId}", workflowId);
            return false;
        }
    }

    public async Task<bool> CancelWorkflowAsync(string workflowId, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_runningWorkflows.TryGetValue(workflowId, out var cts))
            {
                cts.Cancel();
                await AddWorkflowEventAsync(workflowId, WorkflowEventType.Cancelled, $"Workflow cancelled: {reason}");
                _logger.LogInformation("Cancelled workflow {WorkflowId}: {Reason}", workflowId, reason);
                return true;
            }

            _logger.LogWarning("Cannot cancel workflow {WorkflowId} - not currently running", workflowId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel workflow {WorkflowId}", workflowId);
            return false;
        }
    }

    public async Task<bool> PauseWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        try
        {
            var lockKey = $"workflow-pause:{workflowId}";
            await _cacheService.SetAsync(lockKey, "paused", TimeSpan.FromHours(24));
            await AddWorkflowEventAsync(workflowId, WorkflowEventType.Paused, "Workflow paused");
            
            _logger.LogInformation("Paused workflow {WorkflowId}", workflowId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause workflow {WorkflowId}", workflowId);
            return false;
        }
    }

    public async Task<bool> ResumeWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        try
        {
            var lockKey = $"workflow-pause:{workflowId}";
            await _cacheService.DeleteAsync(lockKey);
            await AddWorkflowEventAsync(workflowId, WorkflowEventType.Resumed, "Workflow resumed");
            
            _logger.LogInformation("Resumed workflow {WorkflowId}", workflowId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume workflow {WorkflowId}", workflowId);
            return false;
        }
    }

    public async Task<bool> RollbackWorkflowAsync(string workflowId, string? targetVersion = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var workflow = await GetWorkflowAsync(workflowId, cancellationToken);
            if (workflow == null)
            {
                _logger.LogWarning("Workflow {WorkflowId} not found for rollback", workflowId);
                return false;
            }

            if (workflow.RollbackConfiguration?.Enabled != true)
            {
                _logger.LogWarning("Rollback not enabled for workflow {WorkflowId}", workflowId);
                return false;
            }

            workflow.Status = WorkflowStatus.RollingBack;
            await _workflowRepository.UpdateWorkflowAsync(workflow, cancellationToken);
            await AddWorkflowEventAsync(workflowId, WorkflowEventType.RollbackStarted, 
                $"Rollback started to version {targetVersion ?? workflow.RollbackConfiguration.RollbackVersion ?? "previous"}");

            var success = await _workflowExecutor.RollbackWorkflowAsync(workflow, targetVersion, cancellationToken);
            
            workflow.Status = success ? WorkflowStatus.RolledBack : WorkflowStatus.Failed;
            workflow.CompletedAt = DateTime.UtcNow;
            await _workflowRepository.UpdateWorkflowAsync(workflow, cancellationToken);

            await AddWorkflowEventAsync(workflowId, success ? WorkflowEventType.RollbackCompleted : WorkflowEventType.RollbackFailed,
                success ? "Rollback completed successfully" : "Rollback failed");

            _logger.LogInformation("Rollback workflow {WorkflowId} completed with success: {Success}", workflowId, success);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback workflow {WorkflowId}", workflowId);
            return false;
        }
    }

    public async Task<bool> AutoRollbackAsync(string workflowId, RollbackTriggerType triggerType, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var workflow = await GetWorkflowAsync(workflowId, cancellationToken);
            if (workflow?.RollbackConfiguration?.AutomaticRollback == true)
            {
                await AddWorkflowEventAsync(workflowId, WorkflowEventType.RollbackStarted, 
                    $"Auto-rollback triggered by {triggerType}: {reason}");
                
                return await RollbackWorkflowAsync(workflowId, null, cancellationToken);
            }

            _logger.LogInformation("Auto-rollback not enabled for workflow {WorkflowId}", workflowId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-rollback workflow {WorkflowId}", workflowId);
            return false;
        }
    }

    public async Task<DeploymentWorkflow?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        try
        {
            var cachedWorkflow = await _cacheService.GetAsync<DeploymentWorkflow>($"workflow:{workflowId}");
            if (cachedWorkflow != null)
            {
                return cachedWorkflow;
            }

            var workflow = await _workflowRepository.GetWorkflowAsync(workflowId, cancellationToken);
            if (workflow != null)
            {
                await _cacheService.SetAsync($"workflow:{workflowId}", workflow, TimeSpan.FromHours(1));
            }

            return workflow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflow {WorkflowId}", workflowId);
            return null;
        }
    }

    public async Task<List<DeploymentWorkflow>> GetActiveWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _workflowRepository.GetActiveWorkflowsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active workflows");
            return new List<DeploymentWorkflow>();
        }
    }

    public async Task<List<DeploymentWorkflow>> GetWorkflowsAsync(WorkflowFilter filter, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _workflowRepository.GetWorkflowsAsync(filter, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflows with filter");
            return new List<DeploymentWorkflow>();
        }
    }

    public async Task<WorkflowStatistics> GetStatisticsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = new WorkflowFilter
            {
                CreatedAfter = from,
                CreatedBefore = to,
                Take = int.MaxValue
            };

            var workflows = await _workflowRepository.GetWorkflowsAsync(filter, cancellationToken);
            
            return new WorkflowStatistics
            {
                TotalWorkflows = workflows.Count,
                ActiveWorkflows = workflows.Count(w => w.Status == WorkflowStatus.Running || w.Status == WorkflowStatus.Queued),
                CompletedWorkflows = workflows.Count(w => w.Status == WorkflowStatus.Completed),
                FailedWorkflows = workflows.Count(w => w.Status == WorkflowStatus.Failed),
                SuccessRate = workflows.Count > 0 ? (double)workflows.Count(w => w.Status == WorkflowStatus.Completed) / workflows.Count * 100 : 0,
                AverageExecutionTime = CalculateAverageExecutionTime(workflows),
                StrategyDistribution = workflows.GroupBy(w => w.Strategy).ToDictionary(g => g.Key, g => g.Count()),
                ServiceDistribution = workflows.GroupBy(w => w.ServiceName).ToDictionary(g => g.Key, g => g.Count())
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflow statistics");
            return new WorkflowStatistics();
        }
    }

    public async Task<OrchestratorHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var activeWorkflows = await GetActiveWorkflowsAsync(cancellationToken);
            var queuedWorkflows = activeWorkflows.Where(w => w.Status == WorkflowStatus.Queued).ToList();
            var runningWorkflows = activeWorkflows.Where(w => w.Status == WorkflowStatus.Running).ToList();

            var issues = new List<string>();
            var isHealthy = true;

            if (runningWorkflows.Count > _config.MaxConcurrentWorkflows)
            {
                issues.Add($"Too many concurrent workflows: {runningWorkflows.Count}/{_config.MaxConcurrentWorkflows}");
                isHealthy = false;
            }

            if (queuedWorkflows.Count > _config.MaxQueuedWorkflows)
            {
                issues.Add($"Too many queued workflows: {queuedWorkflows.Count}/{_config.MaxQueuedWorkflows}");
                isHealthy = false;
            }

            return new OrchestratorHealth
            {
                IsHealthy = isHealthy,
                Status = isHealthy ? "Healthy" : "Degraded",
                ActiveWorkflows = runningWorkflows.Count,
                QueuedWorkflows = queuedWorkflows.Count,
                Uptime = DateTime.UtcNow - _startupTime,
                LastHealthCheck = DateTime.UtcNow,
                Issues = issues,
                Details = new Dictionary<string, object>
                {
                    ["MaxConcurrentWorkflows"] = _config.MaxConcurrentWorkflows,
                    ["MaxQueuedWorkflows"] = _config.MaxQueuedWorkflows,
                    ["AvailableStrategies"] = _strategies.Keys.ToList(),
                    ["RunningWorkflowIds"] = _runningWorkflows.Keys.ToList()
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get orchestrator health");
            return new OrchestratorHealth
            {
                IsHealthy = false,
                Status = "Error",
                Issues = new List<string> { $"Health check failed: {ex.Message}" }
            };
        }
    }

    public async Task<List<WorkflowEvent>> GetWorkflowEventsAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _workflowRepository.GetWorkflowEventsAsync(workflowId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflow events for {WorkflowId}", workflowId);
            return new List<WorkflowEvent>();
        }
    }

    public IDeploymentStrategy GetStrategy(DeploymentStrategy strategyType)
    {
        if (_strategies.TryGetValue(strategyType, out var strategy))
        {
            return strategy;
        }

        throw new ArgumentException($"Strategy {strategyType} not found");
    }

    private async Task AddWorkflowEventAsync(string workflowId, WorkflowEventType eventType, string message, string? userId = null)
    {
        try
        {
            var workflowEvent = new WorkflowEvent
            {
                WorkflowId = workflowId,
                Type = eventType,
                Message = message,
                UserId = userId
            };

            await _workflowRepository.AddWorkflowEventAsync(workflowEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add workflow event for {WorkflowId}", workflowId);
        }
    }

    private static TimeSpan CalculateAverageExecutionTime(List<DeploymentWorkflow> workflows)
    {
        var completedWorkflows = workflows.Where(w => w.StartedAt.HasValue && w.CompletedAt.HasValue).ToList();
        if (!completedWorkflows.Any())
            return TimeSpan.Zero;

        var totalTicks = completedWorkflows.Sum(w => (w.CompletedAt!.Value - w.StartedAt!.Value).Ticks);
        return new TimeSpan(totalTicks / completedWorkflows.Count);
    }

    private async void PerformHealthCheck(object? state)
    {
        try
        {
            var health = await GetHealthAsync();
            await _cacheService.SetAsync("orchestrator:health", health, TimeSpan.FromMinutes(5));
            
            if (!health.IsHealthy)
            {
                _logger.LogWarning("Orchestrator health check failed: {Issues}", string.Join(", ", health.Issues));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _healthCheckTimer?.Dispose();
            
            foreach (var cts in _runningWorkflows.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _runningWorkflows.Clear();

            _logger.LogInformation("Deployment orchestrator service disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing deployment orchestrator service");
        }
        finally
        {
            _disposed = true;
        }
    }
}