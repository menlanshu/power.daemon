using PowerDaemon.Messaging.Messages;
using System.Text.Json.Serialization;

namespace PowerDaemon.Orchestrator.Models;

public class DeploymentWorkflow
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("strategy")]
    public DeploymentStrategy Strategy { get; set; }

    [JsonPropertyName("status")]
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Created;

    [JsonPropertyName("targetServers")]
    public List<string> TargetServers { get; set; } = new();

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("packageUrl")]
    public string PackageUrl { get; set; } = string.Empty;

    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    [JsonPropertyName("rollbackConfiguration")]
    public RollbackConfiguration? RollbackConfiguration { get; set; }

    [JsonPropertyName("phases")]
    public List<DeploymentPhase> Phases { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(2);

    [JsonPropertyName("currentPhaseIndex")]
    public int CurrentPhaseIndex { get; set; } = 0;

    [JsonPropertyName("progressPercent")]
    public int ProgressPercent { get; set; } = 0;

    [JsonPropertyName("errors")]
    public List<WorkflowError> Errors { get; set; } = new();
}

public class DeploymentPhase
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("status")]
    public PhaseStatus Status { get; set; } = PhaseStatus.Pending;

    [JsonPropertyName("targetServers")]
    public List<string> TargetServers { get; set; } = new();

    [JsonPropertyName("steps")]
    public List<DeploymentStep> Steps { get; set; } = new();

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;

    [JsonPropertyName("rollbackOnFailure")]
    public bool RollbackOnFailure { get; set; } = true;
}

public class DeploymentStep
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public StepType Type { get; set; }

    [JsonPropertyName("status")]
    public StepStatus Status { get; set; } = StepStatus.Pending;

    [JsonPropertyName("targetServer")]
    public string TargetServer { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public ServiceCommand? Command { get; set; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, object> Parameters { get; set; } = new();

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;
}

public class RollbackConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("automaticRollback")]
    public bool AutomaticRollback { get; set; } = true;

    [JsonPropertyName("rollbackVersion")]
    public string? RollbackVersion { get; set; }

    [JsonPropertyName("healthCheckTimeout")]
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("rollbackTimeout")]
    public TimeSpan RollbackTimeout { get; set; } = TimeSpan.FromMinutes(15);

    [JsonPropertyName("conditions")]
    public List<RollbackCondition> Conditions { get; set; } = new();
}

public class RollbackCondition
{
    [JsonPropertyName("type")]
    public RollbackTriggerType Type { get; set; }

    [JsonPropertyName("threshold")]
    public double Threshold { get; set; }

    [JsonPropertyName("duration")]
    public TimeSpan Duration { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public class WorkflowError
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("severity")]
    public ErrorSeverity Severity { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("phaseId")]
    public string? PhaseId { get; set; }

    [JsonPropertyName("stepId")]
    public string? StepId { get; set; }

    [JsonPropertyName("server")]
    public string? Server { get; set; }
}

public enum WorkflowStatus
{
    Created,
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
    RollingBack,
    RolledBack
}

public enum PhaseStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    RollingBack
}

public enum StepStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    Skipped
}

public enum StepType
{
    ServiceStop,
    ServiceStart,
    ServiceRestart,
    Deploy,
    HealthCheck,
    WaitForHealthy,
    TrafficSwitch,
    Validation,
    Cleanup,
    Custom
}

public enum RollbackTriggerType
{
    ErrorRate,
    ResponseTime,
    HealthCheckFailure,
    ManualTrigger,
    Timeout
}

public enum ErrorSeverity
{
    Info,
    Warning,
    Error,
    Critical
}