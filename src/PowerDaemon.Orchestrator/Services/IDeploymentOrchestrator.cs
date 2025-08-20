using PowerDaemon.Orchestrator.Models;
using PowerDaemon.Messaging.Messages;

namespace PowerDaemon.Orchestrator.Services;

public interface IDeploymentOrchestrator
{
    // Workflow Management
    Task<DeploymentWorkflow> CreateWorkflowAsync(DeploymentWorkflowRequest request, CancellationToken cancellationToken = default);
    Task<bool> StartWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<bool> CancelWorkflowAsync(string workflowId, string reason, CancellationToken cancellationToken = default);
    Task<bool> PauseWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<bool> ResumeWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
    
    // Rollback Operations
    Task<bool> RollbackWorkflowAsync(string workflowId, string? targetVersion = null, CancellationToken cancellationToken = default);
    Task<bool> AutoRollbackAsync(string workflowId, RollbackTriggerType triggerType, string reason, CancellationToken cancellationToken = default);
    
    // Workflow Queries
    Task<DeploymentWorkflow?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<List<DeploymentWorkflow>> GetActiveWorkflowsAsync(CancellationToken cancellationToken = default);
    Task<List<DeploymentWorkflow>> GetWorkflowsAsync(WorkflowFilter filter, CancellationToken cancellationToken = default);
    Task<WorkflowStatistics> GetStatisticsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    
    // Health and Status
    Task<OrchestratorHealth> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<List<WorkflowEvent>> GetWorkflowEventsAsync(string workflowId, CancellationToken cancellationToken = default);
    
    // Strategy Factory
    IDeploymentStrategy GetStrategy(DeploymentStrategy strategyType);
}

public class DeploymentWorkflowRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DeploymentStrategy Strategy { get; set; }
    public List<string> TargetServers { get; set; } = new();
    public string ServiceName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string PackageUrl { get; set; } = string.Empty;
    public Dictionary<string, object> Configuration { get; set; } = new();
    public RollbackConfiguration? RollbackConfiguration { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new();
    public TimeSpan? Timeout { get; set; }
}

public class WorkflowFilter
{
    public WorkflowStatus? Status { get; set; }
    public DeploymentStrategy? Strategy { get; set; }
    public string? ServiceName { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }
    public List<string>? TargetServers { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 100;
}

public class WorkflowStatistics
{
    public int TotalWorkflows { get; set; }
    public int ActiveWorkflows { get; set; }
    public int CompletedWorkflows { get; set; }
    public int FailedWorkflows { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public Dictionary<DeploymentStrategy, int> StrategyDistribution { get; set; } = new();
    public Dictionary<string, int> ServiceDistribution { get; set; } = new();
    public List<WorkflowTrend> Trends { get; set; } = new();
}

public class WorkflowTrend
{
    public DateTime Date { get; set; }
    public int TotalDeployments { get; set; }
    public int SuccessfulDeployments { get; set; }
    public int FailedDeployments { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
}

public class OrchestratorHealth
{
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ActiveWorkflows { get; set; }
    public int QueuedWorkflows { get; set; }
    public TimeSpan Uptime { get; set; }
    public DateTime LastHealthCheck { get; set; }
    public Dictionary<string, object> Details { get; set; } = new();
    public List<string> Issues { get; set; } = new();
}

public class WorkflowEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string WorkflowId { get; set; } = string.Empty;
    public WorkflowEventType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PhaseId { get; set; }
    public string? StepId { get; set; }
    public string? UserId { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
}

public enum WorkflowEventType
{
    Created,
    Started,
    PhaseStarted,
    StepStarted,
    StepCompleted,
    StepFailed,
    PhaseCompleted,
    PhaseFailed,
    Completed,
    Failed,
    Cancelled,
    Paused,
    Resumed,
    RollbackStarted,
    RollbackCompleted,
    RollbackFailed
}