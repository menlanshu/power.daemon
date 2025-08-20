using PowerDaemon.Orchestrator.Models;
using PowerDaemon.Messaging.Messages;

namespace PowerDaemon.Orchestrator.Services;

public interface IDeploymentStrategy
{
    DeploymentStrategy StrategyType { get; }
    
    Task<List<DeploymentPhase>> CreatePhasesAsync(
        DeploymentWorkflowRequest request, 
        CancellationToken cancellationToken = default);
    
    Task<bool> ValidateConfigurationAsync(
        Dictionary<string, object> configuration, 
        CancellationToken cancellationToken = default);
    
    Task<TimeSpan> EstimateExecutionTimeAsync(
        List<string> targetServers, 
        Dictionary<string, object> configuration,
        CancellationToken cancellationToken = default);
}

public interface IWorkflowExecutor
{
    Task<bool> ExecuteWorkflowAsync(DeploymentWorkflow workflow, CancellationToken cancellationToken = default);
    Task<bool> ExecutePhaseAsync(DeploymentWorkflow workflow, DeploymentPhase phase, CancellationToken cancellationToken = default);
    Task<bool> ExecuteStepAsync(DeploymentWorkflow workflow, DeploymentPhase phase, DeploymentStep step, CancellationToken cancellationToken = default);
    Task<bool> RollbackWorkflowAsync(DeploymentWorkflow workflow, string? targetVersion = null, CancellationToken cancellationToken = default);
}

public interface IWorkflowRepository
{
    Task<string> CreateWorkflowAsync(DeploymentWorkflow workflow, CancellationToken cancellationToken = default);
    Task<DeploymentWorkflow?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<bool> UpdateWorkflowAsync(DeploymentWorkflow workflow, CancellationToken cancellationToken = default);
    Task<List<DeploymentWorkflow>> GetActiveWorkflowsAsync(CancellationToken cancellationToken = default);
    Task<List<DeploymentWorkflow>> GetWorkflowsAsync(WorkflowFilter filter, CancellationToken cancellationToken = default);
    Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<List<WorkflowEvent>> GetWorkflowEventsAsync(string workflowId, CancellationToken cancellationToken = default);
    Task<bool> AddWorkflowEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default);
}

public interface IHealthCheckService
{
    Task<bool> CheckServerHealthAsync(string serverAddress, CancellationToken cancellationToken = default);
    Task<Dictionary<string, bool>> CheckMultipleServersHealthAsync(List<string> serverAddresses, CancellationToken cancellationToken = default);
    Task<bool> WaitForHealthyAsync(string serverAddress, TimeSpan timeout, CancellationToken cancellationToken = default);
}