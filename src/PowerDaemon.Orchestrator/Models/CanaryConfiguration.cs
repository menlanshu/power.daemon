using System.Text.Json.Serialization;

namespace PowerDaemon.Orchestrator.Models;

public class CanaryConfiguration
{
    [JsonPropertyName("canaryPercentage")]
    public double CanaryPercentage { get; set; } = 10.0; // 10% default

    [JsonPropertyName("canaryConfiguration")]
    public CanaryDeploymentConfiguration DeploymentConfiguration { get; set; } = new();

    [JsonPropertyName("trafficSplitting")]
    public TrafficSplittingConfiguration TrafficSplitting { get; set; } = new();

    [JsonPropertyName("monitoringConfiguration")]
    public CanaryMonitoringConfiguration MonitoringConfiguration { get; set; } = new();

    [JsonPropertyName("validationConfiguration")]
    public CanaryValidationConfiguration ValidationConfiguration { get; set; } = new();

    [JsonPropertyName("rollbackTriggers")]
    public CanaryRollbackTriggers RollbackTriggers { get; set; } = new();

    [JsonPropertyName("productionDeployment")]
    public ProductionDeploymentConfiguration ProductionDeployment { get; set; } = new();

    [JsonPropertyName("healthCheckTimeout")]
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("monitoringDuration")]
    public TimeSpan MonitoringDuration { get; set; } = TimeSpan.FromMinutes(30);

    [JsonPropertyName("autoRollbackEnabled")]
    public bool AutoRollbackEnabled { get; set; } = true;

    [JsonPropertyName("autoPromotionEnabled")]
    public bool AutoPromotionEnabled { get; set; } = false;
}

public class CanaryDeploymentConfiguration
{
    [JsonPropertyName("explicitCanaryServers")]
    public List<string>? ExplicitCanaryServers { get; set; }

    [JsonPropertyName("canarySelectionStrategy")]
    public CanarySelectionStrategy CanarySelectionStrategy { get; set; } = CanarySelectionStrategy.FirstN;

    [JsonPropertyName("canaryEnvironmentVariables")]
    public Dictionary<string, string> CanaryEnvironmentVariables { get; set; } = new();

    [JsonPropertyName("canaryConfiguration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    [JsonPropertyName("isolationLevel")]
    public CanaryIsolationLevel IsolationLevel { get; set; } = CanaryIsolationLevel.NetworkLevel;

    [JsonPropertyName("canaryLabels")]
    public Dictionary<string, string> CanaryLabels { get; set; } = new();
}

public class TrafficSplittingConfiguration
{
    [JsonPropertyName("strategy")]
    public TrafficSplittingStrategy Strategy { get; set; } = TrafficSplittingStrategy.Percentage;

    [JsonPropertyName("routingRules")]
    public List<RoutingRule> RoutingRules { get; set; } = new();

    [JsonPropertyName("stickySession")]
    public bool StickySession { get; set; } = false;

    [JsonPropertyName("sessionAffinityTimeout")]
    public TimeSpan SessionAffinityTimeout { get; set; } = TimeSpan.FromMinutes(30);

    [JsonPropertyName("gradualRolloutConfiguration")]
    public GradualRolloutConfiguration? GradualRolloutConfiguration { get; set; }

    [JsonPropertyName("headerBasedRouting")]
    public HeaderBasedRoutingConfiguration? HeaderBasedRouting { get; set; }
}

public class RoutingRule
{
    [JsonPropertyName("type")]
    public RoutingRuleType Type { get; set; }

    [JsonPropertyName("criteria")]
    public Dictionary<string, object> Criteria { get; set; } = new();

    [JsonPropertyName("targetPercentage")]
    public double TargetPercentage { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public class GradualRolloutConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("stages")]
    public List<RolloutStage> Stages { get; set; } = new();

    [JsonPropertyName("stageInterval")]
    public TimeSpan StageInterval { get; set; } = TimeSpan.FromMinutes(15);

    [JsonPropertyName("autoAdvance")]
    public bool AutoAdvance { get; set; } = false;

    [JsonPropertyName("advancementCriteria")]
    public AdvancementCriteria AdvancementCriteria { get; set; } = new();
}

public class RolloutStage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("trafficPercentage")]
    public double TrafficPercentage { get; set; }

    [JsonPropertyName("duration")]
    public TimeSpan Duration { get; set; }

    [JsonPropertyName("successCriteria")]
    public Dictionary<string, object> SuccessCriteria { get; set; } = new();
}

public class AdvancementCriteria
{
    [JsonPropertyName("errorRateThreshold")]
    public double ErrorRateThreshold { get; set; } = 1.0;

    [JsonPropertyName("responseTimeThreshold")]
    public TimeSpan ResponseTimeThreshold { get; set; } = TimeSpan.FromSeconds(2);

    [JsonPropertyName("minimumRequestCount")]
    public int MinimumRequestCount { get; set; } = 100;

    [JsonPropertyName("successRateThreshold")]
    public double SuccessRateThreshold { get; set; } = 99.0;
}

public class HeaderBasedRoutingConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("canaryHeaders")]
    public List<CanaryHeader> CanaryHeaders { get; set; } = new();

    [JsonPropertyName("defaultToProduction")]
    public bool DefaultToProduction { get; set; } = true;
}

public class CanaryHeader
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("matchType")]
    public HeaderMatchType MatchType { get; set; } = HeaderMatchType.Exact;
}

public class CanaryMonitoringConfiguration
{
    [JsonPropertyName("requiredMetrics")]
    public List<string> RequiredMetrics { get; set; } = new()
    {
        "error_rate", "response_time", "throughput", "cpu_usage", "memory_usage"
    };

    [JsonPropertyName("businessMetrics")]
    public List<string> BusinessMetrics { get; set; } = new();

    [JsonPropertyName("customMetrics")]
    public Dictionary<string, MetricConfiguration> CustomMetrics { get; set; } = new();

    [JsonPropertyName("alertingConfiguration")]
    public AlertingConfiguration AlertingConfiguration { get; set; } = new();

    [JsonPropertyName("dashboardConfiguration")]
    public DashboardConfiguration DashboardConfiguration { get; set; } = new();

    [JsonPropertyName("logAnalysisConfiguration")]
    public LogAnalysisConfiguration LogAnalysisConfiguration { get; set; } = new();
}

public class MetricConfiguration
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("threshold")]
    public double Threshold { get; set; }

    [JsonPropertyName("comparison")]
    public MetricComparison Comparison { get; set; } = MetricComparison.LessThan;

    [JsonPropertyName("aggregation")]
    public MetricAggregation Aggregation { get; set; } = MetricAggregation.Average;

    [JsonPropertyName("timeWindow")]
    public TimeSpan TimeWindow { get; set; } = TimeSpan.FromMinutes(5);
}

public class AlertingConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("channels")]
    public List<AlertChannel> Channels { get; set; } = new();

    [JsonPropertyName("severity")]
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;

    [JsonPropertyName("suppressionDuration")]
    public TimeSpan SuppressionDuration { get; set; } = TimeSpan.FromMinutes(5);
}

public class AlertChannel
{
    [JsonPropertyName("type")]
    public AlertChannelType Type { get; set; }

    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public class DashboardConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("dashboardUrl")]
    public string? DashboardUrl { get; set; }

    [JsonPropertyName("autoRefreshInterval")]
    public TimeSpan AutoRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    [JsonPropertyName("widgets")]
    public List<DashboardWidget> Widgets { get; set; } = new();
}

public class DashboardWidget
{
    [JsonPropertyName("type")]
    public WidgetType Type { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("metrics")]
    public List<string> Metrics { get; set; } = new();

    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();
}

public class LogAnalysisConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("logSources")]
    public List<string> LogSources { get; set; } = new();

    [JsonPropertyName("errorPatterns")]
    public List<string> ErrorPatterns { get; set; } = new();

    [JsonPropertyName("warningPatterns")]
    public List<string> WarningPatterns { get; set; } = new();

    [JsonPropertyName("analysisWindow")]
    public TimeSpan AnalysisWindow { get; set; } = TimeSpan.FromMinutes(10);
}

public class CanaryValidationConfiguration
{
    [JsonPropertyName("smokeTestSuite")]
    public string SmokeTestSuite { get; set; } = string.Empty;

    [JsonPropertyName("integrationTestSuite")]
    public string IntegrationTestSuite { get; set; } = string.Empty;

    [JsonPropertyName("performanceTestSuite")]
    public string PerformanceTestSuite { get; set; } = string.Empty;

    [JsonPropertyName("successRateThreshold")]
    public double SuccessRateThreshold { get; set; } = 99.0;

    [JsonPropertyName("performanceThreshold")]
    public double PerformanceThreshold { get; set; } = 10.0; // 10% degradation threshold

    [JsonPropertyName("businessMetricsThreshold")]
    public double BusinessMetricsThreshold { get; set; } = 5.0; // 5% variance threshold

    [JsonPropertyName("validationTimeout")]
    public TimeSpan ValidationTimeout { get; set; } = TimeSpan.FromMinutes(15);

    [JsonPropertyName("customValidations")]
    public List<CustomValidation> CustomValidations { get; set; } = new();
}

public class CustomValidation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("validationType")]
    public string ValidationType { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public Dictionary<string, object> Parameters { get; set; } = new();

    [JsonPropertyName("critical")]
    public bool Critical { get; set; } = false;

    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}

public class CanaryRollbackTriggers
{
    [JsonPropertyName("errorRateThreshold")]
    public double ErrorRateThreshold { get; set; } = 5.0; // 5% error rate

    [JsonPropertyName("responseTimeThreshold")]
    public TimeSpan ResponseTimeThreshold { get; set; } = TimeSpan.FromSeconds(3);

    [JsonPropertyName("memoryUsageThreshold")]
    public double MemoryUsageThreshold { get; set; } = 90.0; // 90% memory usage

    [JsonPropertyName("cpuUsageThreshold")]
    public double CpuUsageThreshold { get; set; } = 90.0; // 90% CPU usage

    [JsonPropertyName("healthCheckFailureThreshold")]
    public int HealthCheckFailureThreshold { get; set; } = 3;

    [JsonPropertyName("businessMetricThresholds")]
    public Dictionary<string, double> BusinessMetricThresholds { get; set; } = new();

    [JsonPropertyName("customTriggers")]
    public List<CustomRollbackTrigger> CustomTriggers { get; set; } = new();

    [JsonPropertyName("monitoringWindow")]
    public TimeSpan MonitoringWindow { get; set; } = TimeSpan.FromMinutes(15);

    [JsonPropertyName("minimumDataPoints")]
    public int MinimumDataPoints { get; set; } = 10;
}

public class CustomRollbackTrigger
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("metricName")]
    public string MetricName { get; set; } = string.Empty;

    [JsonPropertyName("threshold")]
    public double Threshold { get; set; }

    [JsonPropertyName("comparison")]
    public MetricComparison Comparison { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("severity")]
    public TriggerSeverity Severity { get; set; } = TriggerSeverity.High;
}

public class ProductionDeploymentConfiguration
{
    [JsonPropertyName("batchSize")]
    public int BatchSize { get; set; } = 0; // 0 = deploy all at once

    [JsonPropertyName("batchDelay")]
    public TimeSpan BatchDelay { get; set; } = TimeSpan.Zero;

    [JsonPropertyName("parallelDeployment")]
    public bool ParallelDeployment { get; set; } = true;

    [JsonPropertyName("healthCheckBetweenBatches")]
    public bool HealthCheckBetweenBatches { get; set; } = true;

    [JsonPropertyName("rollbackOnBatchFailure")]
    public bool RollbackOnBatchFailure { get; set; } = true;

    [JsonPropertyName("maxBatchFailures")]
    public int MaxBatchFailures { get; set; } = 1;
}

// Enums
public enum CanarySelectionStrategy
{
    FirstN,
    LastN,
    Random,
    Explicit,
    GeographicallyDistributed,
    LoadBalanced
}

public enum CanaryIsolationLevel
{
    NetworkLevel,
    ProcessLevel,
    ContainerLevel,
    VirtualMachineLevel
}

public enum TrafficSplittingStrategy
{
    Percentage,
    HeaderBased,
    UserBased,
    Weighted,
    Gradual,
    Geographic
}

public enum RoutingRuleType
{
    Header,
    UserAgent,
    SourceIP,
    Cookie,
    QueryParameter,
    Custom
}

public enum HeaderMatchType
{
    Exact,
    Prefix,
    Suffix,
    Contains,
    Regex
}

public enum MetricComparison
{
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Equal,
    NotEqual
}

public enum MetricAggregation
{
    Average,
    Sum,
    Min,
    Max,
    Count,
    Percentile95,
    Percentile99
}

public enum AlertSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public enum AlertChannelType
{
    Email,
    Slack,
    Teams,
    PagerDuty,
    Webhook,
    SMS
}

public enum WidgetType
{
    LineChart,
    BarChart,
    Gauge,
    SingleValue,
    Table,
    Heatmap
}

public enum TriggerSeverity
{
    Low,
    Medium,
    High,
    Critical
}

// Extension methods for configuration conversion
public static class CanaryConfigurationExtensions
{
    public static Dictionary<string, object> ToDictionary(this CanaryConfiguration config)
    {
        return new Dictionary<string, object>
        {
            ["CanaryConfiguration"] = new Dictionary<string, object>
            {
                ["ExplicitCanaryServers"] = config.DeploymentConfiguration.ExplicitCanaryServers ?? new List<string>(),
                ["CanarySelectionStrategy"] = config.DeploymentConfiguration.CanarySelectionStrategy.ToString(),
                ["CanaryEnvironmentVariables"] = config.DeploymentConfiguration.CanaryEnvironmentVariables,
                ["Configuration"] = config.DeploymentConfiguration.Configuration,
                ["IsolationLevel"] = config.DeploymentConfiguration.IsolationLevel.ToString(),
                ["CanaryLabels"] = config.DeploymentConfiguration.CanaryLabels
            },
            ["TrafficSplitting"] = new Dictionary<string, object>
            {
                ["Strategy"] = config.TrafficSplitting.Strategy.ToString(),
                ["RoutingRules"] = config.TrafficSplitting.RoutingRules.Select(r => new Dictionary<string, object>
                {
                    ["Type"] = r.Type.ToString(),
                    ["Criteria"] = r.Criteria,
                    ["TargetPercentage"] = r.TargetPercentage,
                    ["Priority"] = r.Priority,
                    ["Enabled"] = r.Enabled
                }).ToList(),
                ["StickySession"] = config.TrafficSplitting.StickySession,
                ["SessionAffinityTimeout"] = config.TrafficSplitting.SessionAffinityTimeout
            },
            ["MonitoringConfiguration"] = new Dictionary<string, object>
            {
                ["RequiredMetrics"] = config.MonitoringConfiguration.RequiredMetrics,
                ["BusinessMetrics"] = config.MonitoringConfiguration.BusinessMetrics,
                ["CustomMetrics"] = config.MonitoringConfiguration.CustomMetrics,
                ["AlertingConfiguration"] = config.MonitoringConfiguration.AlertingConfiguration
            },
            ["CanaryPercentage"] = config.CanaryPercentage,
            ["HealthCheckTimeout"] = config.HealthCheckTimeout,
            ["MonitoringDuration"] = config.MonitoringDuration,
            ["AutoRollbackEnabled"] = config.AutoRollbackEnabled,
            ["AutoPromotionEnabled"] = config.AutoPromotionEnabled
        };
    }

    public static Dictionary<string, object> ToDictionary(this CanaryMonitoringConfiguration config)
    {
        return new Dictionary<string, object>
        {
            ["RequiredMetrics"] = config.RequiredMetrics,
            ["BusinessMetrics"] = config.BusinessMetrics,
            ["CustomMetrics"] = config.CustomMetrics.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<string, object>
                {
                    ["Query"] = kvp.Value.Query,
                    ["Threshold"] = kvp.Value.Threshold,
                    ["Comparison"] = kvp.Value.Comparison.ToString(),
                    ["Aggregation"] = kvp.Value.Aggregation.ToString(),
                    ["TimeWindow"] = kvp.Value.TimeWindow
                }
            ),
            ["AlertingConfiguration"] = new Dictionary<string, object>
            {
                ["Enabled"] = config.AlertingConfiguration.Enabled,
                ["Channels"] = config.AlertingConfiguration.Channels,
                ["Severity"] = config.AlertingConfiguration.Severity.ToString(),
                ["SuppressionDuration"] = config.AlertingConfiguration.SuppressionDuration
            }
        };
    }

    public static CanaryConfiguration FromDictionary(Dictionary<string, object> dict)
    {
        var config = new CanaryConfiguration();

        if (dict.TryGetValue("CanaryPercentage", out var percentageObj) && percentageObj is double percentage)
        {
            config.CanaryPercentage = percentage;
        }

        if (dict.TryGetValue("CanaryConfiguration", out var canaryConfigObj) && 
            canaryConfigObj is Dictionary<string, object> canaryConfig)
        {
            config.DeploymentConfiguration = ParseCanaryDeploymentConfiguration(canaryConfig);
        }

        if (dict.TryGetValue("TrafficSplitting", out var trafficObj) && 
            trafficObj is Dictionary<string, object> trafficConfig)
        {
            config.TrafficSplitting = ParseTrafficSplittingConfiguration(trafficConfig);
        }

        if (dict.TryGetValue("MonitoringConfiguration", out var monitoringObj) && 
            monitoringObj is Dictionary<string, object> monitoringConfig)
        {
            config.MonitoringConfiguration = ParseMonitoringConfiguration(monitoringConfig);
        }

        if (dict.TryGetValue("HealthCheckTimeout", out var healthTimeoutObj) && 
            healthTimeoutObj is TimeSpan healthTimeout)
        {
            config.HealthCheckTimeout = healthTimeout;
        }

        if (dict.TryGetValue("MonitoringDuration", out var monitoringDurationObj) && 
            monitoringDurationObj is TimeSpan monitoringDuration)
        {
            config.MonitoringDuration = monitoringDuration;
        }

        if (dict.TryGetValue("AutoRollbackEnabled", out var autoRollbackObj) && 
            autoRollbackObj is bool autoRollback)
        {
            config.AutoRollbackEnabled = autoRollback;
        }

        return config;
    }

    private static CanaryDeploymentConfiguration ParseCanaryDeploymentConfiguration(Dictionary<string, object> dict)
    {
        var config = new CanaryDeploymentConfiguration();

        if (dict.TryGetValue("ExplicitCanaryServers", out var serversObj) && 
            serversObj is List<string> servers)
        {
            config.ExplicitCanaryServers = servers;
        }

        if (dict.TryGetValue("CanarySelectionStrategy", out var strategyObj) && 
            Enum.TryParse<CanarySelectionStrategy>(strategyObj.ToString(), out var strategy))
        {
            config.CanarySelectionStrategy = strategy;
        }

        if (dict.TryGetValue("CanaryEnvironmentVariables", out var envVarsObj) && 
            envVarsObj is Dictionary<string, string> envVars)
        {
            config.CanaryEnvironmentVariables = envVars;
        }

        return config;
    }

    private static TrafficSplittingConfiguration ParseTrafficSplittingConfiguration(Dictionary<string, object> dict)
    {
        var config = new TrafficSplittingConfiguration();

        if (dict.TryGetValue("Strategy", out var strategyObj) && 
            Enum.TryParse<TrafficSplittingStrategy>(strategyObj.ToString(), out var strategy))
        {
            config.Strategy = strategy;
        }

        if (dict.TryGetValue("StickySession", out var stickyObj) && stickyObj is bool sticky)
        {
            config.StickySession = sticky;
        }

        return config;
    }

    private static CanaryMonitoringConfiguration ParseMonitoringConfiguration(Dictionary<string, object> dict)
    {
        var config = new CanaryMonitoringConfiguration();

        if (dict.TryGetValue("RequiredMetrics", out var metricsObj) && 
            metricsObj is List<string> metrics)
        {
            config.RequiredMetrics = metrics;
        }

        if (dict.TryGetValue("BusinessMetrics", out var businessMetricsObj) && 
            businessMetricsObj is List<string> businessMetrics)
        {
            config.BusinessMetrics = businessMetrics;
        }

        return config;
    }
}