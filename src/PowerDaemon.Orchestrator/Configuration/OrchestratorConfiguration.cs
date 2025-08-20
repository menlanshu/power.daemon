namespace PowerDaemon.Orchestrator.Configuration;

public class OrchestratorConfiguration
{
    public int MaxConcurrentWorkflows { get; set; } = 10;
    public int MaxQueuedWorkflows { get; set; } = 50;
    public int HealthCheckIntervalSeconds { get; set; } = 30;
    public int WorkflowTimeoutMinutes { get; set; } = 120;
    public int PhaseTimeoutMinutes { get; set; } = 30;
    public int StepTimeoutMinutes { get; set; } = 10;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 30;
    public bool EnableAutoRollback { get; set; } = true;
    public int RollbackTimeoutMinutes { get; set; } = 15;
    public string WorkflowStorePath { get; set; } = "./workflows";
    public bool PersistWorkflowEvents { get; set; } = true;
    public int WorkflowCleanupDays { get; set; } = 30;
}