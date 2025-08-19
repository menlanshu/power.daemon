using System.Text.Json.Serialization;

namespace PowerDaemon.Messaging.Messages;

public class ServiceCommand
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("targetServerId")]
    public string TargetServerId { get; set; } = string.Empty;

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public ServiceCommandType Command { get; set; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, object> Parameters { get; set; } = new();

    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("issuedAt")]
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("issuedBy")]
    public string IssuedBy { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public CommandPriority Priority { get; set; } = CommandPriority.Normal;

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;
}

public enum ServiceCommandType
{
    Start,
    Stop,
    Restart,
    Status,
    HealthCheck,
    Configure,
    Update,
    Rollback
}

public enum CommandPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}