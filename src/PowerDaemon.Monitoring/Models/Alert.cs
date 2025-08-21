using System.Text.Json.Serialization;
using PowerDaemon.Monitoring.Configuration;

namespace PowerDaemon.Monitoring.Models;

public class Alert
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public AlertSeverity Severity { get; set; }

    [JsonPropertyName("category")]
    public AlertCategory Category { get; set; }

    [JsonPropertyName("status")]
    public AlertStatus Status { get; set; } = AlertStatus.Active;

    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    [JsonPropertyName("serverName")]
    public string? ServerName { get; set; }

    [JsonPropertyName("serviceId")]
    public string? ServiceId { get; set; }

    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("acknowledgedAt")]
    public DateTime? AcknowledgedAt { get; set; }

    [JsonPropertyName("acknowledgedBy")]
    public string? AcknowledgedBy { get; set; }

    [JsonPropertyName("resolvedAt")]
    public DateTime? ResolvedAt { get; set; }

    [JsonPropertyName("resolvedBy")]
    public string? ResolvedBy { get; set; }

    [JsonPropertyName("escalatedAt")]
    public DateTime? EscalatedAt { get; set; }

    [JsonPropertyName("escalationLevel")]
    public int EscalationLevel { get; set; } = 0;

    [JsonPropertyName("notificationsSent")]
    public int NotificationsSent { get; set; } = 0;

    [JsonPropertyName("lastNotificationAt")]
    public DateTime? LastNotificationAt { get; set; }

    [JsonPropertyName("suppressionRules")]
    public List<string> SuppressionRules { get; set; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("sourceSystem")]
    public string SourceSystem { get; set; } = "PowerDaemon";

    [JsonPropertyName("sourceRule")]
    public string? SourceRule { get; set; }

    [JsonPropertyName("thresholdValue")]
    public double? ThresholdValue { get; set; }

    [JsonPropertyName("actualValue")]
    public double? ActualValue { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("dataPoints")]
    public List<AlertDataPoint> DataPoints { get; set; } = new();

    [JsonPropertyName("relatedAlerts")]
    public List<string> RelatedAlerts { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<AlertAction> Actions { get; set; } = new();

    [JsonPropertyName("notifications")]
    public List<AlertNotification> Notifications { get; set; } = new();

    public string GenerateFingerprint()
    {
        var components = new[]
        {
            Title,
            Category.ToString(),
            ServerId ?? "",
            ServiceId ?? "",
            SourceRule ?? ""
        };

        var content = string.Join("|", components);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash)[..16];
    }

    public bool ShouldEscalate(TimeSpan escalationTimeout)
    {
        if (Status != AlertStatus.Active) return false;
        if (EscalationLevel >= 3) return false; // Max escalation level
        
        var timeSinceCreated = DateTime.UtcNow - CreatedAt;
        var timeSinceLastEscalation = EscalatedAt.HasValue ? DateTime.UtcNow - EscalatedAt.Value : timeSinceCreated;
        
        return timeSinceLastEscalation >= escalationTimeout;
    }

    public bool ShouldSuppressNotification(TimeSpan suppressionTimeout)
    {
        if (!LastNotificationAt.HasValue) return false;
        return DateTime.UtcNow - LastNotificationAt.Value < suppressionTimeout;
    }
}

public class AlertDataPoint
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class AlertAction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("type")]
    public AlertActionType Type { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("performedBy")]
    public string PerformedBy { get; set; } = string.Empty;

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class AlertNotification
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("channelName")]
    public string ChannelName { get; set; } = string.Empty;

    [JsonPropertyName("channelType")]
    public NotificationChannelType ChannelType { get; set; }

    [JsonPropertyName("sentAt")]
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("status")]
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;

    [JsonPropertyName("deliveryConfirmation")]
    public bool DeliveryConfirmation { get; set; } = false;

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class AlertRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("category")]
    public AlertCategory Category { get; set; }

    [JsonPropertyName("severity")]
    public AlertSeverity Severity { get; set; }

    [JsonPropertyName("condition")]
    public AlertCondition Condition { get; set; } = new();

    [JsonPropertyName("evaluationInterval")]
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromMinutes(1);

    [JsonPropertyName("evaluationWindow")]
    public TimeSpan EvaluationWindow { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("minimumDataPoints")]
    public int MinimumDataPoints { get; set; } = 3;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("notifications")]
    public List<string> NotificationChannels { get; set; } = new();

    [JsonPropertyName("suppressionRules")]
    public List<SuppressionRule> SuppressionRules { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedBy")]
    public string UpdatedBy { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;
}

public class AlertCondition
{
    [JsonPropertyName("metric")]
    public string Metric { get; set; } = string.Empty;

    [JsonPropertyName("operator")]
    public ComparisonOperator Operator { get; set; }

    [JsonPropertyName("threshold")]
    public double Threshold { get; set; }

    [JsonPropertyName("aggregation")]
    public AggregationType Aggregation { get; set; } = AggregationType.Average;

    [JsonPropertyName("filters")]
    public Dictionary<string, string> Filters { get; set; } = new();

    [JsonPropertyName("groupBy")]
    public List<string> GroupBy { get; set; } = new();
}

public class SuppressionRule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("condition")]
    public string Condition { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public class AlertStatistics
{
    [JsonPropertyName("totalAlerts")]
    public int TotalAlerts { get; set; }

    [JsonPropertyName("activeAlerts")]
    public int ActiveAlerts { get; set; }

    [JsonPropertyName("acknowledgedAlerts")]
    public int AcknowledgedAlerts { get; set; }

    [JsonPropertyName("resolvedAlerts")]
    public int ResolvedAlerts { get; set; }

    [JsonPropertyName("criticalAlerts")]
    public int CriticalAlerts { get; set; }

    [JsonPropertyName("warningAlerts")]
    public int WarningAlerts { get; set; }

    [JsonPropertyName("infoAlerts")]
    public int InfoAlerts { get; set; }

    [JsonPropertyName("alertsByCategory")]
    public Dictionary<AlertCategory, int> AlertsByCategory { get; set; } = new();

    [JsonPropertyName("alertsByServer")]
    public Dictionary<string, int> AlertsByServer { get; set; } = new();

    [JsonPropertyName("alertsByService")]
    public Dictionary<string, int> AlertsByService { get; set; } = new();

    [JsonPropertyName("averageResolutionTime")]
    public TimeSpan AverageResolutionTime { get; set; }

    [JsonPropertyName("alertTrends")]
    public List<AlertTrend> AlertTrends { get; set; } = new();

    [JsonPropertyName("topAlertRules")]
    public List<TopAlertRule> TopAlertRules { get; set; } = new();

    [JsonPropertyName("calculatedAt")]
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}

public class AlertTrend
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("severity")]
    public AlertSeverity? Severity { get; set; }

    [JsonPropertyName("category")]
    public AlertCategory? Category { get; set; }
}

public class TopAlertRule
{
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("ruleName")]
    public string RuleName { get; set; } = string.Empty;

    [JsonPropertyName("alertCount")]
    public int AlertCount { get; set; }

    [JsonPropertyName("lastTriggered")]
    public DateTime? LastTriggered { get; set; }
}

// Enums
public enum AlertStatus
{
    Active,
    Acknowledged,
    Resolved,
    Suppressed
}

public enum AlertActionType
{
    Created,
    Acknowledged,
    Escalated,
    Resolved,
    Suppressed,
    NotificationSent,
    CommentAdded,
    Assigned,
    Updated
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Delivered,
    Failed,
    Cancelled
}

public enum ComparisonOperator
{
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Equal,
    NotEqual,
    Contains,
    NotContains
}

public enum AggregationType
{
    Average,
    Sum,
    Count,
    Min,
    Max,
    P95,
    P99
}