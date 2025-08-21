using PowerDaemon.Monitoring.Models;
using PowerDaemon.Monitoring.Configuration;

namespace PowerDaemon.Monitoring.Services;

public interface IAlertService
{
    Task<Alert> CreateAlertAsync(CreateAlertRequest request, CancellationToken cancellationToken = default);
    Task<Alert?> GetAlertAsync(string alertId, CancellationToken cancellationToken = default);
    Task<List<Alert>> GetActiveAlertsAsync(CancellationToken cancellationToken = default);
    Task<List<Alert>> GetAlertsByServerAsync(string serverId, CancellationToken cancellationToken = default);
    Task<List<Alert>> GetAlertsByServiceAsync(string serviceId, CancellationToken cancellationToken = default);
    Task<List<Alert>> GetAlertsByCategoryAsync(AlertCategory category, CancellationToken cancellationToken = default);
    Task<List<Alert>> GetAlertsBySeverityAsync(AlertSeverity severity, CancellationToken cancellationToken = default);
    
    Task<Alert> AcknowledgeAlertAsync(string alertId, string acknowledgedBy, string? comment = null, CancellationToken cancellationToken = default);
    Task<Alert> ResolveAlertAsync(string alertId, string resolvedBy, string? comment = null, CancellationToken cancellationToken = default);
    Task<Alert> EscalateAlertAsync(string alertId, string escalatedBy, string? comment = null, CancellationToken cancellationToken = default);
    Task<Alert> AddCommentAsync(string alertId, string author, string comment, CancellationToken cancellationToken = default);
    
    Task<AlertStatistics> GetStatisticsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<List<Alert>> SearchAlertsAsync(AlertSearchRequest request, CancellationToken cancellationToken = default);
    
    Task<bool> SuppressAlertAsync(string alertId, TimeSpan duration, string reason, CancellationToken cancellationToken = default);
    Task<bool> UnsuppressAlertAsync(string alertId, CancellationToken cancellationToken = default);
    
    Task CleanupExpiredAlertsAsync(CancellationToken cancellationToken = default);
    
    event EventHandler<AlertCreatedEventArgs>? AlertCreated;
    event EventHandler<AlertUpdatedEventArgs>? AlertUpdated;
    event EventHandler<AlertResolvedEventArgs>? AlertResolved;
}

public interface IAlertRuleService
{
    Task<AlertRule> CreateRuleAsync(CreateAlertRuleRequest request, CancellationToken cancellationToken = default);
    Task<AlertRule> UpdateRuleAsync(string ruleId, UpdateAlertRuleRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteRuleAsync(string ruleId, CancellationToken cancellationToken = default);
    
    Task<AlertRule?> GetRuleAsync(string ruleId, CancellationToken cancellationToken = default);
    Task<List<AlertRule>> GetAllRulesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);
    Task<List<AlertRule>> GetRulesByCategoryAsync(AlertCategory category, CancellationToken cancellationToken = default);
    Task<List<AlertRule>> GetRulesByTagAsync(string tag, CancellationToken cancellationToken = default);
    
    Task<bool> EnableRuleAsync(string ruleId, CancellationToken cancellationToken = default);
    Task<bool> DisableRuleAsync(string ruleId, CancellationToken cancellationToken = default);
    
    Task<bool> TestRuleAsync(string ruleId, CancellationToken cancellationToken = default);
    Task<AlertRule> DuplicateRuleAsync(string ruleId, string newName, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task<bool> SendNotificationAsync(Alert alert, NotificationChannel channel, CancellationToken cancellationToken = default);
    Task<bool> SendNotificationAsync(Alert alert, string channelName, CancellationToken cancellationToken = default);
    Task<List<NotificationResult>> SendBatchNotificationAsync(List<Alert> alerts, string channelName, CancellationToken cancellationToken = default);
    
    Task<bool> TestChannelAsync(string channelName, CancellationToken cancellationToken = default);
    Task<List<NotificationChannel>> GetChannelsAsync(CancellationToken cancellationToken = default);
    Task<NotificationChannel?> GetChannelAsync(string channelName, CancellationToken cancellationToken = default);
    
    Task<NotificationStatistics> GetStatisticsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
}

public interface IMetricsAggregationService
{
    Task<double?> EvaluateMetricAsync(string metric, ComparisonOperator operation, double threshold, 
        TimeSpan evaluationWindow, AggregationType aggregation, Dictionary<string, string>? filters = null, 
        CancellationToken cancellationToken = default);
    
    Task<List<double>> GetMetricValuesAsync(string metric, DateTime from, DateTime to, 
        Dictionary<string, string>? filters = null, CancellationToken cancellationToken = default);
    
    Task<Dictionary<string, double>> GetMultipleMetricsAsync(List<string> metrics, DateTime from, DateTime to,
        AggregationType aggregation = AggregationType.Average, Dictionary<string, string>? filters = null, 
        CancellationToken cancellationToken = default);
    
    Task<bool> IsMetricAvailableAsync(string metric, CancellationToken cancellationToken = default);
    Task<List<string>> GetAvailableMetricsAsync(CancellationToken cancellationToken = default);
}

public interface IMonitoringDashboardService
{
    Task<Dashboard> CreateDashboardAsync(CreateDashboardRequest request, CancellationToken cancellationToken = default);
    Task<Dashboard> UpdateDashboardAsync(string dashboardId, UpdateDashboardRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteDashboardAsync(string dashboardId, CancellationToken cancellationToken = default);
    
    Task<Dashboard?> GetDashboardAsync(string dashboardId, CancellationToken cancellationToken = default);
    Task<List<Dashboard>> GetAllDashboardsAsync(CancellationToken cancellationToken = default);
    Task<List<Dashboard>> GetUserDashboardsAsync(string userId, CancellationToken cancellationToken = default);
    
    Task<DashboardData> GetDashboardDataAsync(string dashboardId, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<WidgetData> GetWidgetDataAsync(string widgetId, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
}

// Request/Response models
public class CreateAlertRequest
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public AlertCategory Category { get; set; }
    public string? ServerId { get; set; }
    public string? ServiceId { get; set; }
    public string? SourceRule { get; set; }
    public double? ThresholdValue { get; set; }
    public double? ActualValue { get; set; }
    public string? Unit { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<AlertDataPoint> DataPoints { get; set; } = new();
}

public class AlertSearchRequest
{
    public string? Query { get; set; }
    public AlertSeverity? Severity { get; set; }
    public AlertCategory? Category { get; set; }
    public AlertStatus? Status { get; set; }
    public string? ServerId { get; set; }
    public string? ServiceId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<string>? Tags { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 100;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
}

public class CreateAlertRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AlertCategory Category { get; set; }
    public AlertSeverity Severity { get; set; }
    public AlertCondition Condition { get; set; } = new();
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan EvaluationWindow { get; set; } = TimeSpan.FromMinutes(5);
    public int MinimumDataPoints { get; set; } = 3;
    public List<string> Tags { get; set; } = new();
    public List<string> NotificationChannels { get; set; } = new();
    public List<SuppressionRule> SuppressionRules { get; set; } = new();
    public string CreatedBy { get; set; } = string.Empty;
}

public class UpdateAlertRuleRequest : CreateAlertRuleRequest
{
    public string UpdatedBy { get; set; } = string.Empty;
}

public class NotificationResult
{
    public string AlertId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public class NotificationStatistics
{
    public int TotalNotifications { get; set; }
    public int SuccessfulNotifications { get; set; }
    public int FailedNotifications { get; set; }
    public Dictionary<NotificationChannelType, int> NotificationsByChannel { get; set; } = new();
    public Dictionary<string, int> NotificationsByAlert { get; set; } = new();
    public TimeSpan AverageDeliveryTime { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}

// Event args
public class AlertCreatedEventArgs : EventArgs
{
    public Alert Alert { get; }
    public AlertCreatedEventArgs(Alert alert) => Alert = alert;
}

public class AlertUpdatedEventArgs : EventArgs
{
    public Alert Alert { get; }
    public AlertActionType ActionType { get; }
    public AlertUpdatedEventArgs(Alert alert, AlertActionType actionType)
    {
        Alert = alert;
        ActionType = actionType;
    }
}

public class AlertResolvedEventArgs : EventArgs
{
    public Alert Alert { get; }
    public string ResolvedBy { get; }
    public AlertResolvedEventArgs(Alert alert, string resolvedBy)
    {
        Alert = alert;
        ResolvedBy = resolvedBy;
    }
}

// Dashboard models
public class Dashboard
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public List<DashboardWidget> Widgets { get; set; } = new();
    public DashboardLayout Layout { get; set; } = new();
    public bool IsPublic { get; set; } = false;
    public List<string> SharedWith { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CreateDashboardRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public List<DashboardWidget> Widgets { get; set; } = new();
    public DashboardLayout Layout { get; set; } = new();
    public bool IsPublic { get; set; } = false;
    public List<string> SharedWith { get; set; } = new();
}

public class UpdateDashboardRequest : CreateDashboardRequest
{
}

public class DashboardData
{
    public string DashboardId { get; set; } = string.Empty;
    public Dictionary<string, WidgetData> WidgetData { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class WidgetData
{
    public string WidgetId { get; set; } = string.Empty;
    public List<DataSeries> Series { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class DataSeries
{
    public string Name { get; set; } = string.Empty;
    public List<DataPoint> Points { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class DataPoint
{
    public DateTime Timestamp { get; set; }
    public double Value { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}