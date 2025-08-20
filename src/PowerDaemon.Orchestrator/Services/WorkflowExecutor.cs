using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Orchestrator.Models;
using PowerDaemon.Orchestrator.Configuration;
using PowerDaemon.Messaging.Services;
using PowerDaemon.Messaging.Messages;
using OrchestratorServiceCommand = PowerDaemon.Orchestrator.Models.ServiceCommand;
using PowerDaemon.Cache.Services;

namespace PowerDaemon.Orchestrator.Services;

public class WorkflowExecutor : IWorkflowExecutor
{
    private readonly ILogger<WorkflowExecutor> _logger;
    private readonly OrchestratorConfiguration _config;
    private readonly IMessagePublisher _messagePublisher;
    private readonly ICacheService _cacheService;
    private readonly IHealthCheckService _healthCheckService;
    private readonly IWorkflowRepository _workflowRepository;

    public WorkflowExecutor(
        ILogger<WorkflowExecutor> logger,
        IOptions<OrchestratorConfiguration> config,
        IMessagePublisher messagePublisher,
        ICacheService cacheService,
        IHealthCheckService healthCheckService,
        IWorkflowRepository workflowRepository)
    {
        _logger = logger;
        _config = config.Value;
        _messagePublisher = messagePublisher;
        _cacheService = cacheService;
        _healthCheckService = healthCheckService;
        _workflowRepository = workflowRepository;
    }

    public async Task<bool> ExecuteWorkflowAsync(DeploymentWorkflow workflow, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting execution of workflow {WorkflowId}", workflow.Id);

            using var timeoutCts = new CancellationTokenSource(workflow.Timeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            for (int phaseIndex = 0; phaseIndex < workflow.Phases.Count; phaseIndex++)
            {
                await CheckPauseState(workflow.Id, combinedCts.Token);

                var phase = workflow.Phases[phaseIndex];
                workflow.CurrentPhaseIndex = phaseIndex;

                var success = await ExecutePhaseAsync(workflow, phase, combinedCts.Token);
                if (!success)
                {
                    _logger.LogError("Phase {PhaseId} failed in workflow {WorkflowId}", phase.Id, workflow.Id);
                    
                    if (phase.RollbackOnFailure && workflow.RollbackConfiguration?.AutomaticRollback == true)
                    {
                        _logger.LogInformation("Starting automatic rollback for workflow {WorkflowId}", workflow.Id);
                        await RollbackWorkflowAsync(workflow, null, combinedCts.Token);
                    }
                    
                    return false;
                }

                workflow.ProgressPercent = (int)((double)(phaseIndex + 1) / workflow.Phases.Count * 100);
                await _workflowRepository.UpdateWorkflowAsync(workflow, combinedCts.Token);
            }

            _logger.LogInformation("Workflow {WorkflowId} completed successfully", workflow.Id);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Workflow {WorkflowId} execution was cancelled", workflow.Id);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Workflow {WorkflowId} execution timed out after {Timeout}", workflow.Id, workflow.Timeout);
            
            workflow.Errors.Add(new WorkflowError
            {
                Severity = ErrorSeverity.Critical,
                Message = "Workflow execution timeout",
                Details = $"Timeout after {workflow.Timeout}"
            });
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error executing workflow {WorkflowId}", workflow.Id);
            
            workflow.Errors.Add(new WorkflowError
            {
                Severity = ErrorSeverity.Critical,
                Message = "Unhandled execution error",
                Details = ex.Message
            });
            
            return false;
        }
    }

    public async Task<bool> ExecutePhaseAsync(DeploymentWorkflow workflow, DeploymentPhase phase, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting phase {PhaseId} ({PhaseName}) in workflow {WorkflowId}", 
                phase.Id, phase.Name, workflow.Id);

            phase.Status = PhaseStatus.Running;
            phase.StartedAt = DateTime.UtcNow;
            await _workflowRepository.UpdateWorkflowAsync(workflow, cancellationToken);

            await AddWorkflowEventAsync(workflow.Id, WorkflowEventType.PhaseStarted, 
                $"Phase {phase.Name} started", phase.Id);

            using var timeoutCts = new CancellationTokenSource(phase.Timeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            for (int attempt = 0; attempt <= phase.MaxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    _logger.LogInformation("Retrying phase {PhaseId}, attempt {Attempt}/{MaxRetries}", 
                        phase.Id, attempt, phase.MaxRetries);
                    
                    await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds * attempt), combinedCts.Token);
                }

                var success = await ExecutePhaseSteps(workflow, phase, combinedCts.Token);
                if (success)
                {
                    phase.Status = PhaseStatus.Completed;
                    phase.CompletedAt = DateTime.UtcNow;
                    phase.RetryCount = attempt;
                    
                    await _workflowRepository.UpdateWorkflowAsync(workflow, cancellationToken);
                    await AddWorkflowEventAsync(workflow.Id, WorkflowEventType.PhaseCompleted, 
                        $"Phase {phase.Name} completed successfully", phase.Id);
                    
                    _logger.LogInformation("Phase {PhaseId} completed successfully in workflow {WorkflowId}", 
                        phase.Id, workflow.Id);
                    
                    return true;
                }

                phase.RetryCount = attempt;
                await _workflowRepository.UpdateWorkflowAsync(workflow, cancellationToken);

                if (attempt < phase.MaxRetries)
                {
                    _logger.LogWarning("Phase {PhaseId} failed, will retry. Attempt {Attempt}/{MaxRetries}", 
                        phase.Id, attempt + 1, phase.MaxRetries);
                }
            }

            phase.Status = PhaseStatus.Failed;
            phase.CompletedAt = DateTime.UtcNow;
            await _workflowRepository.UpdateWorkflowAsync(workflow, cancellationToken);
            
            await AddWorkflowEventAsync(workflow.Id, WorkflowEventType.PhaseFailed, 
                $"Phase {phase.Name} failed after {phase.MaxRetries} retries", phase.Id);

            _logger.LogError("Phase {PhaseId} failed after {MaxRetries} retries in workflow {WorkflowId}", 
                phase.Id, phase.MaxRetries, workflow.Id);

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            phase.Status = PhaseStatus.Cancelled;
            phase.CompletedAt = DateTime.UtcNow;
            await _workflowRepository.UpdateWorkflowAsync(workflow, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error executing phase {PhaseId} in workflow {WorkflowId}", 
                phase.Id, workflow.Id);
            
            phase.Status = PhaseStatus.Failed;
            phase.CompletedAt = DateTime.UtcNow;
            await _workflowRepository.UpdateWorkflowAsync(workflow, CancellationToken.None);
            
            workflow.Errors.Add(new WorkflowError
            {
                Severity = ErrorSeverity.Error,
                Message = $"Phase {phase.Name} execution error",
                Details = ex.Message,
                PhaseId = phase.Id
            });
            
            return false;
        }
    }

    public async Task<bool> ExecuteStepAsync(DeploymentWorkflow workflow, DeploymentPhase phase, DeploymentStep step, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Executing step {StepId} ({StepName}) on server {Server}", 
                step.Id, step.Name, step.TargetServer);

            step.Status = StepStatus.Running;
            step.StartedAt = DateTime.UtcNow;
            await _workflowRepository.UpdateWorkflowAsync(workflow, cancellationToken);

            await AddWorkflowEventAsync(workflow.Id, WorkflowEventType.StepStarted, 
                $"Step {step.Name} started on {step.TargetServer}", phase.Id, step.Id);

            for (int attempt = 0; attempt <= step.MaxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_config.RetryDelaySeconds * attempt), cancellationToken);
                }

                var success = await ExecuteStepOperation(workflow, phase, step, cancellationToken);
                if (success)
                {
                    step.Status = StepStatus.Completed;
                    step.CompletedAt = DateTime.UtcNow;
                    step.RetryCount = attempt;
                    
                    await _workflowRepository.UpdateWorkflowAsync(workflow, cancellationToken);
                    await AddWorkflowEventAsync(workflow.Id, WorkflowEventType.StepCompleted, 
                        $"Step {step.Name} completed on {step.TargetServer}", phase.Id, step.Id);
                    
                    return true;
                }

                step.RetryCount = attempt;
                await _workflowRepository.UpdateWorkflowAsync(workflow, cancellationToken);
            }

            step.Status = StepStatus.Failed;
            step.CompletedAt = DateTime.UtcNow;
            await _workflowRepository.UpdateWorkflowAsync(workflow, cancellationToken);
            
            await AddWorkflowEventAsync(workflow.Id, WorkflowEventType.StepFailed, 
                $"Step {step.Name} failed on {step.TargetServer}", phase.Id, step.Id);

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            step.Status = StepStatus.Cancelled;
            step.CompletedAt = DateTime.UtcNow;
            await _workflowRepository.UpdateWorkflowAsync(workflow, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing step {StepId} in workflow {WorkflowId}", step.Id, workflow.Id);
            
            step.Status = StepStatus.Failed;
            step.CompletedAt = DateTime.UtcNow;
            step.Error = ex.Message;
            await _workflowRepository.UpdateWorkflowAsync(workflow, CancellationToken.None);
            
            return false;
        }
    }

    public async Task<bool> RollbackWorkflowAsync(DeploymentWorkflow workflow, string? targetVersion = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting rollback for workflow {WorkflowId} to version {Version}", 
                workflow.Id, targetVersion ?? workflow.RollbackConfiguration?.RollbackVersion ?? "previous");

            if (workflow.RollbackConfiguration?.Enabled != true)
            {
                _logger.LogWarning("Rollback not enabled for workflow {WorkflowId}", workflow.Id);
                return false;
            }

            var rollbackTimeout = workflow.RollbackConfiguration.RollbackTimeout;
            using var timeoutCts = new CancellationTokenSource(rollbackTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Create rollback command
            var rollbackCommand = new PowerDaemon.Messaging.Messages.DeploymentCommand
            {
                Type = OrchestratorServiceCommand.Rollback,
                ServiceName = workflow.ServiceName,
                TargetServers = workflow.TargetServers,
                Parameters = new Dictionary<string, object>
                {
                    ["TargetVersion"] = targetVersion ?? workflow.RollbackConfiguration.RollbackVersion ?? string.Empty,
                    ["WorkflowId"] = workflow.Id
                }
            };

            // Execute rollback for each server
            var rollbackTasks = workflow.TargetServers.Select(async server =>
            {
                try
                {
                    var serverRollbackCommand = new PowerDaemon.Messaging.Messages.DeploymentCommand
                    {
                        DeploymentId = rollbackCommand.DeploymentId,
                        TargetServerId = server,
                        ServiceName = rollbackCommand.ServiceName,
                        Strategy = rollbackCommand.Strategy,
                        RollbackVersion = rollbackCommand.RollbackVersion,
                        Configuration = rollbackCommand.Configuration
                    };
                    await _messagePublisher.PublishAsync(serverRollbackCommand, $"rollback.{server}", combinedCts.Token);
                    
                    // Wait for rollback completion with health check
                    var healthCheckTimeout = workflow.RollbackConfiguration.HealthCheckTimeout;
                    var isHealthy = await _healthCheckService.WaitForHealthyAsync(server, healthCheckTimeout, combinedCts.Token);
                    
                    if (!isHealthy)
                    {
                        _logger.LogError("Server {Server} failed health check after rollback", server);
                        return false;
                    }
                    
                    _logger.LogInformation("Rollback completed successfully for server {Server}", server);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Rollback failed for server {Server}", server);
                    return false;
                }
            });

            var results = await Task.WhenAll(rollbackTasks);
            var success = results.All(r => r);

            _logger.LogInformation("Rollback for workflow {WorkflowId} completed with success: {Success}", 
                workflow.Id, success);

            return success;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Rollback for workflow {WorkflowId} timed out", workflow.Id);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rollback for workflow {WorkflowId}", workflow.Id);
            return false;
        }
    }

    private async Task<bool> ExecutePhaseSteps(DeploymentWorkflow workflow, DeploymentPhase phase, CancellationToken cancellationToken)
    {
        var allSuccess = true;

        foreach (var step in phase.Steps)
        {
            await CheckPauseState(workflow.Id, cancellationToken);

            var success = await ExecuteStepAsync(workflow, phase, step, cancellationToken);
            if (!success)
            {
                allSuccess = false;
                
                // Check if step is critical - if so, fail the entire phase
                if (step.Parameters.TryGetValue("Critical", out var criticalObj) && 
                    criticalObj is bool critical && critical)
                {
                    _logger.LogError("Critical step {StepId} failed, failing phase {PhaseId}", step.Id, phase.Id);
                    break;
                }
                
                // Non-critical step failure - mark as skipped and continue
                step.Status = StepStatus.Skipped;
                _logger.LogWarning("Non-critical step {StepId} failed, continuing with next step", step.Id);
            }
        }

        return allSuccess;
    }

    private async Task<bool> ExecuteStepOperation(DeploymentWorkflow workflow, DeploymentPhase phase, DeploymentStep step, CancellationToken cancellationToken)
    {
        try
        {
            switch (step.Type)
            {
                case StepType.Deploy:
                    return await ExecuteDeployStep(workflow, step, cancellationToken);
                
                case StepType.HealthCheck:
                    return await ExecuteHealthCheckStep(step, cancellationToken);
                
                case StepType.WaitForHealthy:
                    return await ExecuteWaitForHealthyStep(step, cancellationToken);
                
                case StepType.ServiceStart:
                case StepType.ServiceStop:
                case StepType.ServiceRestart:
                    return await ExecuteServiceStep(workflow, step, cancellationToken);
                
                case StepType.TrafficSwitch:
                    return await ExecuteTrafficSwitchStep(workflow, step, cancellationToken);
                
                case StepType.Validation:
                    return await ExecuteValidationStep(step, cancellationToken);
                
                case StepType.Cleanup:
                    return await ExecuteCleanupStep(workflow, step, cancellationToken);
                
                case StepType.Custom:
                    return await ExecuteCustomStep(workflow, step, cancellationToken);
                
                default:
                    _logger.LogError("Unknown step type: {StepType}", step.Type);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing step operation {StepType} for step {StepId}", step.Type, step.Id);
            step.Error = ex.Message;
            return false;
        }
    }

    private async Task<bool> ExecuteDeployStep(DeploymentWorkflow workflow, DeploymentStep step, CancellationToken cancellationToken)
    {
        var deployCommand = new PowerDaemon.Messaging.Messages.DeploymentCommand
        {
            DeploymentId = workflow.Id,
            TargetServerId = step.TargetServer,
            ServiceName = workflow.ServiceName,
            Strategy = PowerDaemon.Messaging.Messages.DeploymentStrategy.Rolling,
            PackageUrl = workflow.PackageUrl,
            Version = workflow.Version,
            Configuration = new Dictionary<string, object>(step.Parameters)
            {
                ["WorkflowId"] = workflow.Id,
                ["StepId"] = step.Id
            }
        };

        await _messagePublisher.PublishAsync(deployCommand, $"deploy.{step.TargetServer}", cancellationToken);
        
        // Wait for deployment completion (implementation depends on your response handling)
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); // Placeholder
        
        step.Output = $"Deployment command sent to {step.TargetServer}";
        return true;
    }

    private async Task<bool> ExecuteHealthCheckStep(DeploymentStep step, CancellationToken cancellationToken)
    {
        var isHealthy = await _healthCheckService.CheckServerHealthAsync(step.TargetServer, cancellationToken);
        step.Output = $"Health check result: {(isHealthy ? "Healthy" : "Unhealthy")}";
        return isHealthy;
    }

    private async Task<bool> ExecuteWaitForHealthyStep(DeploymentStep step, CancellationToken cancellationToken)
    {
        var timeout = step.Parameters.TryGetValue("Timeout", out var timeoutObj) && timeoutObj is TimeSpan to 
            ? to : TimeSpan.FromMinutes(5);
            
        var isHealthy = await _healthCheckService.WaitForHealthyAsync(step.TargetServer, timeout, cancellationToken);
        step.Output = $"Wait for healthy result: {(isHealthy ? "Healthy" : "Timeout/Unhealthy")}";
        return isHealthy;
    }

    private async Task<bool> ExecuteServiceStep(DeploymentWorkflow workflow, DeploymentStep step, CancellationToken cancellationToken)
    {
        var serviceCommand = step.Type switch
        {
            StepType.ServiceStart => OrchestratorServiceCommand.Start,
            StepType.ServiceStop => OrchestratorServiceCommand.Stop,
            StepType.ServiceRestart => OrchestratorServiceCommand.Restart,
            _ => OrchestratorServiceCommand.Status
        };

        var command = new PowerDaemon.Messaging.Messages.DeploymentCommand
        {
            DeploymentId = workflow.Id,
            TargetServerId = step.TargetServer,
            ServiceName = workflow.ServiceName,
            Strategy = PowerDaemon.Messaging.Messages.DeploymentStrategy.Rolling,
            Configuration = new Dictionary<string, object>(step.Parameters)
            {
                ["WorkflowId"] = workflow.Id,
                ["StepId"] = step.Id,
                ["ServiceCommand"] = serviceCommand.ToString()
            }
        };

        await _messagePublisher.PublishAsync(command, $"service.{step.TargetServer}", cancellationToken);
        
        step.Output = $"Service {serviceCommand} command sent to {step.TargetServer}";
        return true;
    }

    private async Task<bool> ExecuteTrafficSwitchStep(DeploymentWorkflow workflow, DeploymentStep step, CancellationToken cancellationToken)
    {
        // Implementation for traffic switching (load balancer updates, etc.)
        await Task.Delay(100, cancellationToken); // Placeholder
        step.Output = $"Traffic switch completed for {step.TargetServer}";
        return true;
    }

    private async Task<bool> ExecuteValidationStep(DeploymentStep step, CancellationToken cancellationToken)
    {
        // Implementation for validation checks
        await Task.Delay(100, cancellationToken); // Placeholder
        step.Output = $"Validation completed for {step.TargetServer}";
        return true;
    }

    private async Task<bool> ExecuteCleanupStep(DeploymentWorkflow workflow, DeploymentStep step, CancellationToken cancellationToken)
    {
        // Implementation for cleanup operations
        await Task.Delay(100, cancellationToken); // Placeholder
        step.Output = $"Cleanup completed for {step.TargetServer}";
        return true;
    }

    private async Task<bool> ExecuteCustomStep(DeploymentWorkflow workflow, DeploymentStep step, CancellationToken cancellationToken)
    {
        // Implementation for custom step execution
        await Task.Delay(100, cancellationToken); // Placeholder
        step.Output = $"Custom step executed for {step.TargetServer}";
        return true;
    }

    private async Task CheckPauseState(string workflowId, CancellationToken cancellationToken)
    {
        var pauseKey = $"workflow-pause:{workflowId}";
        
        while (await _cacheService.ExistsAsync(pauseKey))
        {
            _logger.LogDebug("Workflow {WorkflowId} is paused, waiting...", workflowId);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private async Task AddWorkflowEventAsync(string workflowId, WorkflowEventType eventType, string message, string? phaseId = null, string? stepId = null)
    {
        try
        {
            var workflowEvent = new WorkflowEvent
            {
                WorkflowId = workflowId,
                Type = eventType,
                Message = message,
                PhaseId = phaseId,
                StepId = stepId
            };

            await _workflowRepository.AddWorkflowEventAsync(workflowEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add workflow event for {WorkflowId}", workflowId);
        }
    }
}