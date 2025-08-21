using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PowerDaemon.Monitoring.Configuration;

public class MonitoringConfiguration
{
    [JsonPropertyName("alerting")]
    public AlertingConfiguration Alerting { get; set; } = new();

    [JsonPropertyName("thresholds")]
    public ThresholdConfiguration Thresholds { get; set; } = new();

    [JsonPropertyName("notifications")]
    public NotificationConfiguration Notifications { get; set; } = new();

    [JsonPropertyName("healthChecks")]
    public HealthCheckConfiguration HealthChecks { get; set; } = new();

    [JsonPropertyName("metrics")]
    public MetricsConfiguration Metrics { get; set; } = new();

    [JsonPropertyName("dashboards")]
    public DashboardConfiguration Dashboards { get; set; } = new();
    
    [JsonPropertyName("productionScale")]
    public MonitoringProductionSettings ProductionScale { get; set; } = new();
}

public class AlertingConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("evaluationIntervalSeconds")]
    [Range(5, 3600)]
    public int EvaluationIntervalSeconds { get; set; } = 30;

    [JsonPropertyName("alertRetentionDays")]
    [Range(1, 365)]
    public int AlertRetentionDays { get; set; } = 30;

    [JsonPropertyName("maxAlertsPerMinute")]
    [Range(1, 1000)]
    public int MaxAlertsPerMinute { get; set; } = 10;

    [JsonPropertyName("suppressDuplicateAlerts")]
    public bool SuppressDuplicateAlerts { get; set; } = true;

    [JsonPropertyName("suppressionTimeoutMinutes")]
    [Range(1, 1440)]
    public int SuppressionTimeoutMinutes { get; set; } = 5;

    [JsonPropertyName("enableEscalation")]
    public bool EnableEscalation { get; set; } = true;

    [JsonPropertyName("escalationTimeoutMinutes")]
    [Range(5, 1440)]
    public int EscalationTimeoutMinutes { get; set; } = 30;
}

public class ThresholdConfiguration
{
    [JsonPropertyName("cpu")]
    public ResourceThreshold Cpu { get; set; } = new()
    {
        Warning = 70.0,
        Critical = 90.0,
        EvaluationWindowMinutes = 5
    };

    [JsonPropertyName("memory")]
    public ResourceThreshold Memory { get; set; } = new()
    {
        Warning = 80.0,
        Critical = 95.0,
        EvaluationWindowMinutes = 5
    };

    [JsonPropertyName("disk")]
    public ResourceThreshold Disk { get; set; } = new()
    {
        Warning = 85.0,
        Critical = 95.0,
        EvaluationWindowMinutes = 10
    };

    [JsonPropertyName("network")]
    public NetworkThreshold Network { get; set; } = new()
    {
        WarningMbps = 800.0,
        CriticalMbps = 950.0,
        PacketLossWarning = 1.0,
        PacketLossCritical = 5.0,
        EvaluationWindowMinutes = 5
    };

    [JsonPropertyName("service")]
    public ServiceThreshold Service { get; set; } = new()
    {
        ResponseTimeWarningMs = 2000,
        ResponseTimeCriticalMs = 5000,
        ErrorRateWarning = 1.0,
        ErrorRateCritical = 5.0,
        EvaluationWindowMinutes = 10
    };

    [JsonPropertyName("deployment")]
    public DeploymentThreshold Deployment { get; set; } = new()
    {
        FailureRateWarning = 10.0,
        FailureRateCritical = 25.0,
        DurationWarningMinutes = 30,
        DurationCriticalMinutes = 60,
        RollbackTimeWarningMinutes = 10,
        RollbackTimeCriticalMinutes = 20
    };
}

public class ResourceThreshold
{
    [JsonPropertyName("warning")]
    [Range(0.0, 100.0)]
    public double Warning { get; set; }

    [JsonPropertyName("critical")]
    [Range(0.0, 100.0)]
    public double Critical { get; set; }

    [JsonPropertyName("evaluationWindowMinutes")]
    [Range(1, 1440)]
    public int EvaluationWindowMinutes { get; set; }

    [JsonPropertyName("minimumDataPoints")]
    [Range(1, 100)]
    public int MinimumDataPoints { get; set; } = 3;
}

public class NetworkThreshold
{
    [JsonPropertyName("warningMbps")]
    [Range(0.0, 10000.0)]
    public double WarningMbps { get; set; }

    [JsonPropertyName("criticalMbps")]
    [Range(0.0, 10000.0)]
    public double CriticalMbps { get; set; }

    [JsonPropertyName("packetLossWarning")]
    [Range(0.0, 100.0)]
    public double PacketLossWarning { get; set; }

    [JsonPropertyName("packetLossCritical")]
    [Range(0.0, 100.0)]
    public double PacketLossCritical { get; set; }

    [JsonPropertyName("evaluationWindowMinutes")]
    [Range(1, 1440)]
    public int EvaluationWindowMinutes { get; set; }
}

public class ServiceThreshold
{
    [JsonPropertyName("responseTimeWarningMs")]
    [Range(1, 60000)]
    public int ResponseTimeWarningMs { get; set; }

    [JsonPropertyName("responseTimeCriticalMs")]
    [Range(1, 60000)]
    public int ResponseTimeCriticalMs { get; set; }

    [JsonPropertyName("errorRateWarning")]
    [Range(0.0, 100.0)]
    public double ErrorRateWarning { get; set; }

    [JsonPropertyName("errorRateCritical")]
    [Range(0.0, 100.0)]
    public double ErrorRateCritical { get; set; }

    [JsonPropertyName("evaluationWindowMinutes")]
    [Range(1, 1440)]
    public int EvaluationWindowMinutes { get; set; }
}

public class DeploymentThreshold
{
    [JsonPropertyName("failureRateWarning")]
    [Range(0.0, 100.0)]
    public double FailureRateWarning { get; set; }

    [JsonPropertyName("failureRateCritical")]
    [Range(0.0, 100.0)]
    public double FailureRateCritical { get; set; }

    [JsonPropertyName("durationWarningMinutes")]
    [Range(1, 1440)]
    public int DurationWarningMinutes { get; set; }

    [JsonPropertyName("durationCriticalMinutes")]
    [Range(1, 1440)]
    public int DurationCriticalMinutes { get; set; }

    [JsonPropertyName("rollbackTimeWarningMinutes")]
    [Range(1, 1440)]
    public int RollbackTimeWarningMinutes { get; set; }

    [JsonPropertyName("rollbackTimeCriticalMinutes")]
    [Range(1, 1440)]
    public int RollbackTimeCriticalMinutes { get; set; }
}

public class NotificationConfiguration
{
    [JsonPropertyName("channels")]
    public List<NotificationChannel> Channels { get; set; } = new();

    [JsonPropertyName("routing")]
    public NotificationRouting Routing { get; set; } = new();

    [JsonPropertyName("templates")]
    public NotificationTemplates Templates { get; set; } = new();
}

public class NotificationChannel
{
    [JsonPropertyName("name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [Required]
    public NotificationChannelType Type { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    [JsonPropertyName("severityLevels")]
    public List<AlertSeverity> SeverityLevels { get; set; } = new();

    [JsonPropertyName("timeRestrictions")]
    public List<TimeRestriction> TimeRestrictions { get; set; } = new();
}

public class NotificationRouting
{
    [JsonPropertyName("defaultChannels")]
    public List<string> DefaultChannels { get; set; } = new();

    [JsonPropertyName("severityRouting")]
    public Dictionary<AlertSeverity, List<string>> SeverityRouting { get; set; } = new();

    [JsonPropertyName("categoryRouting")]
    public Dictionary<AlertCategory, List<string>> CategoryRouting { get; set; } = new();

    [JsonPropertyName("serverRouting")]
    public Dictionary<string, List<string>> ServerRouting { get; set; } = new();
}

public class NotificationTemplates
{
    [JsonPropertyName("alertTitle")]
    public string AlertTitle { get; set; } = "[{Severity}] {Category}: {Title}";

    [JsonPropertyName("alertBody")]
    public string AlertBody { get; set; } = "Server: {ServerName}\nService: {ServiceName}\nMessage: {Message}\nTime: {Timestamp}\nDetails: {Details}";

    [JsonPropertyName("recoveryTitle")]
    public string RecoveryTitle { get; set; } = "[RECOVERED] {Category}: {Title}";

    [JsonPropertyName("recoveryBody")]
    public string RecoveryBody { get; set; } = "Alert has recovered.\nServer: {ServerName}\nService: {ServiceName}\nRecovery Time: {Timestamp}";
}

public class TimeRestriction
{
    [JsonPropertyName("dayOfWeek")]
    public DayOfWeek? DayOfWeek { get; set; }

    [JsonPropertyName("startTime")]
    public TimeSpan StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public TimeSpan EndTime { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public class HealthCheckConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("intervalSeconds")]
    [Range(5, 3600)]
    public int IntervalSeconds { get; set; } = 30;

    [JsonPropertyName("timeoutSeconds")]
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 10;

    [JsonPropertyName("retryAttempts")]
    [Range(1, 10)]
    public int RetryAttempts { get; set; } = 3;

    [JsonPropertyName("retryDelaySeconds")]
    [Range(1, 60)]
    public int RetryDelaySeconds { get; set; } = 5;

    [JsonPropertyName("customChecks")]
    public List<CustomHealthCheck> CustomChecks { get; set; } = new();
}

public class CustomHealthCheck
{
    [JsonPropertyName("name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [Required]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("timeoutSeconds")]
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 10;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public class MetricsConfiguration
{
    [JsonPropertyName("collectionEnabled")]
    public bool CollectionEnabled { get; set; } = true;

    [JsonPropertyName("aggregationEnabled")]
    public bool AggregationEnabled { get; set; } = true;

    [JsonPropertyName("retentionDays")]
    [Range(1, 365)]
    public int RetentionDays { get; set; } = 30;

    [JsonPropertyName("aggregationIntervalMinutes")]
    [Range(1, 1440)]
    public int AggregationIntervalMinutes { get; set; } = 5;

    [JsonPropertyName("customMetrics")]
    public List<CustomMetric> CustomMetrics { get; set; } = new();

    [JsonPropertyName("exporters")]
    public List<MetricExporter> Exporters { get; set; } = new();
}

public class CustomMetric
{
    [JsonPropertyName("name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public MetricType Type { get; set; } = MetricType.Gauge;

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; set; } = new();
}

public class MetricExporter
{
    [JsonPropertyName("name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public MetricExporterType Type { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    [JsonPropertyName("exportIntervalSeconds")]
    [Range(10, 3600)]
    public int ExportIntervalSeconds { get; set; } = 60;
}

public class DashboardConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("autoRefreshSeconds")]
    [Range(5, 300)]
    public int AutoRefreshSeconds { get; set; } = 30;

    [JsonPropertyName("maxDataPoints")]
    [Range(50, 10000)]
    public int MaxDataPoints { get; set; } = 1000;

    [JsonPropertyName("customDashboards")]
    public List<CustomDashboard> CustomDashboards { get; set; } = new();

    [JsonPropertyName("widgets")]
    public List<DashboardWidget> Widgets { get; set; } = new();
}

public class CustomDashboard
{
    [JsonPropertyName("name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("layout")]
    public DashboardLayout Layout { get; set; } = new();

    [JsonPropertyName("widgets")]
    public List<string> Widgets { get; set; } = new();

    [JsonPropertyName("refreshIntervalSeconds")]
    [Range(5, 300)]
    public int RefreshIntervalSeconds { get; set; } = 30;
}

public class DashboardLayout
{
    [JsonPropertyName("columns")]
    [Range(1, 12)]
    public int Columns { get; set; } = 12;

    [JsonPropertyName("rows")]
    [Range(1, 24)]
    public int Rows { get; set; } = 8;

    [JsonPropertyName("autoResize")]
    public bool AutoResize { get; set; } = true;
}

public class DashboardWidget
{
    [JsonPropertyName("id")]
    [Required]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public WidgetType Type { get; set; }

    [JsonPropertyName("dataSource")]
    public string DataSource { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public WidgetPosition Position { get; set; } = new();

    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();
}

public class WidgetPosition
{
    [JsonPropertyName("x")]
    [Range(0, 11)]
    public int X { get; set; }

    [JsonPropertyName("y")]
    [Range(0, 23)]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    [Range(1, 12)]
    public int Width { get; set; } = 4;

    [JsonPropertyName("height")]
    [Range(1, 8)]
    public int Height { get; set; } = 2;
}

// Enums
public enum NotificationChannelType
{
    Email,
    Slack,
    Teams,
    Webhook,
    SMS,
    PagerDuty,
    Discord,
    Custom
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum AlertCategory
{
    System,
    Service,
    Deployment,
    Network,
    Security,
    Performance,
    Custom
}

public enum MetricType
{
    Gauge,
    Counter,
    Histogram,
    Summary
}

public enum MetricExporterType
{
    Prometheus,
    InfluxDB,
    Graphite,
    ElasticSearch,
    Custom
}

public enum WidgetType
{
    LineChart,
    BarChart,
    Gauge,
    Table,
    Counter,
    Status,
    Map,
    Custom
}

// Production scale optimization settings for monitoring 200+ servers
public class MonitoringProductionSettings
{
    [JsonPropertyName("dataProcessing")]
    public DataProcessingSettings DataProcessing { get; set; } = new();
    
    [JsonPropertyName("alertingScale")]
    public AlertingScaleSettings AlertingScale { get; set; } = new();
    
    [JsonPropertyName("metricsScale")]
    public MetricsScaleSettings MetricsScale { get; set; } = new();
    
    [JsonPropertyName("dashboardScale")]
    public DashboardScaleSettings DashboardScale { get; set; } = new();
    
    [JsonPropertyName("performance")]
    public PerformanceSettings Performance { get; set; } = new();
}

public class DataProcessingSettings
{
    [JsonPropertyName("batchSize")]
    [Range(100, 10000)]
    public int BatchSize { get; set; } = 1000;
    
    [JsonPropertyName("processingThreads")]
    [Range(1, 50)]
    public int ProcessingThreads { get; set; } = 10;
    
    [JsonPropertyName("queueCapacity")]
    [Range(1000, 100000)]
    public int QueueCapacity { get; set; } = 50000;
    
    [JsonPropertyName("enableParallelProcessing")]
    public bool EnableParallelProcessing { get; set; } = true;
    
    [JsonPropertyName("maxConcurrentOperations")]
    [Range(50, 1000)]
    public int MaxConcurrentOperations { get; set; } = 500;
}

public class AlertingScaleSettings
{
    [JsonPropertyName("maxAlertsPerSecond")]
    [Range(10, 1000)]
    public int MaxAlertsPerSecond { get; set; } = 100;
    
    [JsonPropertyName("alertBatchSize")]
    [Range(10, 500)]
    public int AlertBatchSize { get; set; } = 50;
    
    [JsonPropertyName("evaluationParallelism")]
    [Range(1, 20)]
    public int EvaluationParallelism { get; set; } = 8;
    
    [JsonPropertyName("notificationBatchSize")]
    [Range(5, 100)]
    public int NotificationBatchSize { get; set; } = 20;
    
    [JsonPropertyName("enableAlertGrouping")]
    public bool EnableAlertGrouping { get; set; } = true;
    
    [JsonPropertyName("groupingWindowSeconds")]
    [Range(10, 300)]
    public int GroupingWindowSeconds { get; set; } = 60;
}

public class MetricsScaleSettings
{
    [JsonPropertyName("collectionParallelism")]
    [Range(2, 50)]
    public int CollectionParallelism { get; set; } = 16;
    
    [JsonPropertyName("aggregationBatchSize")]
    [Range(100, 5000)]
    public int AggregationBatchSize { get; set; } = 1000;
    
    [JsonPropertyName("maxMetricsPerServer")]
    [Range(100, 10000)]
    public int MaxMetricsPerServer { get; set; } = 1000;
    
    [JsonPropertyName("compressionRatio")]
    [Range(1, 100)]
    public int CompressionRatio { get; set; } = 10; // Keep 1 in 10 data points for long-term storage
    
    [JsonPropertyName("enableDownsampling")]
    public bool EnableDownsampling { get; set; } = true;
    
    [JsonPropertyName("downsamplingIntervals")]
    public List<int> DownsamplingIntervals { get; set; } = new() { 60, 300, 3600 }; // 1min, 5min, 1hour
}

public class DashboardScaleSettings
{
    [JsonPropertyName("maxDataPointsPerWidget")]
    [Range(100, 10000)]
    public int MaxDataPointsPerWidget { get; set; } = 2000;
    
    [JsonPropertyName("cacheDurationSeconds")]
    [Range(10, 300)]
    public int CacheDurationSeconds { get; set; } = 60;
    
    [JsonPropertyName("enableWidgetCaching")]
    public bool EnableWidgetCaching { get; set; } = true;
    
    [JsonPropertyName("maxConcurrentDashboards")]
    [Range(10, 1000)]
    public int MaxConcurrentDashboards { get; set; } = 200;
    
    [JsonPropertyName("enableLazyLoading")]
    public bool EnableLazyLoading { get; set; } = true;
}

public class PerformanceSettings
{
    [JsonPropertyName("enablePerformanceMonitoring")]
    public bool EnablePerformanceMonitoring { get; set; } = true;
    
    [JsonPropertyName("memoryThresholdMB")]
    [Range(100, 10000)]
    public int MemoryThresholdMB { get; set; } = 2048;
    
    [JsonPropertyName("cpuThresholdPercent")]
    [Range(10, 90)]
    public int CpuThresholdPercent { get; set; } = 80;
    
    [JsonPropertyName("enableAutoScaling")]
    public bool EnableAutoScaling { get; set; } = true;
    
    [JsonPropertyName("scaleUpThreshold")]
    [Range(50, 95)]
    public int ScaleUpThreshold { get; set; } = 80;
    
    [JsonPropertyName("scaleDownThreshold")]
    [Range(10, 50)]
    public int ScaleDownThreshold { get; set; } = 30;
    
    [JsonPropertyName("enableLoadBalancing")]
    public bool EnableLoadBalancing { get; set; } = true;
    
    [JsonPropertyName("loadBalancingStrategy")]
    public string LoadBalancingStrategy { get; set; } = "round-robin";
    
    [JsonPropertyName("healthCheckIntervalSeconds")]
    [Range(5, 60)]
    public int HealthCheckIntervalSeconds { get; set; } = 15;
}