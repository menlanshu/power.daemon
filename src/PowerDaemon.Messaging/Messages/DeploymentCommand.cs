using System.Text.Json.Serialization;

namespace PowerDaemon.Messaging.Messages;

public class DeploymentCommand
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("deploymentId")]
    public string DeploymentId { get; set; } = string.Empty;

    [JsonPropertyName("targetServerId")]
    public string TargetServerId { get; set; } = string.Empty;

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("strategy")]
    public DeploymentStrategy Strategy { get; set; } = DeploymentStrategy.Rolling;

    [JsonPropertyName("packageUrl")]
    public string PackageUrl { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    [JsonPropertyName("rollbackVersion")]
    public string? RollbackVersion { get; set; }

    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    [JsonPropertyName("issuedAt")]
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("issuedBy")]
    public string IssuedBy { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public DeploymentPriority Priority { get; set; } = DeploymentPriority.Normal;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("targetServers")]
    public List<string> TargetServers { get; set; } = new();

    [JsonPropertyName("parameters")]
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public enum DeploymentStrategy
{
    Rolling,
    BlueGreen,
    Canary,
    Immediate,
    Scheduled
}

public enum DeploymentPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public static class OrchestratorServiceCommand
{
    public const string Deploy = "Deploy";
    public const string Rollback = "Rollback";
    public const string Stop = "Stop";
    public const string Start = "Start";
    public const string Restart = "Restart";
    public const string HealthCheck = "HealthCheck";
}