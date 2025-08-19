using System.Text.Json.Serialization;

namespace PowerDaemon.Messaging.Messages;

public class StatusUpdate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("sourceServerId")]
    public string SourceServerId { get; set; } = string.Empty;

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("statusType")]
    public StatusUpdateType StatusType { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public Dictionary<string, object> Details { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("severity")]
    public StatusSeverity Severity { get; set; } = StatusSeverity.Info;

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("deployment")]
    public DeploymentStatusInfo? Deployment { get; set; }
}

public class DeploymentStatusInfo
{
    [JsonPropertyName("deploymentId")]
    public string DeploymentId { get; set; } = string.Empty;

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = string.Empty;

    [JsonPropertyName("progressPercent")]
    public int ProgressPercent { get; set; } = 0;

    [JsonPropertyName("estimatedTimeRemaining")]
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}

public enum StatusUpdateType
{
    ServiceStatus,
    DeploymentProgress,
    SystemHealth,
    Alert,
    Metrics,
    Audit
}

public enum StatusSeverity
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}