using System.Text.Json.Serialization;

namespace PowerDaemon.Orchestrator.Models;

public class RollingConfiguration
{
    [JsonPropertyName("rollingConfiguration")]
    public RollingDeploymentConfiguration DeploymentConfiguration { get; set; } = new();

    [JsonPropertyName("waveConfiguration")]
    public WaveConfiguration WaveConfiguration { get; set; } = new();

    [JsonPropertyName("healthCheckConfiguration")]
    public RollingHealthCheckConfiguration HealthCheckConfiguration { get; set; } = new();

    [JsonPropertyName("monitoringConfiguration")]
    public RollingMonitoringConfiguration MonitoringConfiguration { get; set; } = new();

    [JsonPropertyName("validationConfiguration")]
    public RollingValidationConfiguration ValidationConfiguration { get; set; } = new();

    [JsonPropertyName("rollbackTriggers")]
    public RollingRollbackTriggers RollbackTriggers { get; set; } = new();

    [JsonPropertyName("loadBalancerConfiguration")]
    public RollingLoadBalancerConfiguration LoadBalancerConfiguration { get; set; } = new();
}

public class RollingDeploymentConfiguration
{
    [JsonPropertyName("minimumAvailableInstances")]
    public int MinimumAvailableInstances { get; set; } = 1;

    [JsonPropertyName("minimumAvailablePercentage")]
    public double MinimumAvailablePercentage { get; set; } = 50.0;

    [JsonPropertyName("serviceDependencies")]
    public List<string> ServiceDependencies { get; set; } = new();

    [JsonPropertyName("rollbackOnAnyWaveFailure")]
    public bool RollbackOnAnyWaveFailure { get; set; } = true;

    [JsonPropertyName("continueOnNonCriticalFailure")]
    public bool ContinueOnNonCriticalFailure { get; set; } = true;

    [JsonPropertyName("maxConcurrentWaves")]
    public int MaxConcurrentWaves { get; set; } = 1;

    [JsonPropertyName("enablePreemptiveScaling")]
    public bool EnablePreemptiveScaling { get; set; } = false;

    [JsonPropertyName("gracefulShutdownTimeout")]
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromMinutes(5);
}

public class WaveConfiguration
{
    [JsonPropertyName("strategy")]
    public WaveStrategy Strategy { get; set; } = WaveStrategy.FixedSize;

    [JsonPropertyName("waveSize")]
    public int WaveSize { get; set; } = 0; // 0 = calculated automatically

    [JsonPropertyName("wavePercentage")]
    public double WavePercentage { get; set; } = 25.0; // 25% per wave

    [JsonPropertyName("waveInterval")]
    public TimeSpan WaveInterval { get; set; } = TimeSpan.FromMinutes(10);

    [JsonPropertyName("parallelDeploymentWithinWave")]
    public bool ParallelDeploymentWithinWave { get; set; } = true;

    [JsonPropertyName("maxParallelism")]
    public int MaxParallelism { get; set; } = 5;

    [JsonPropertyName("delayBetweenServers")]
    public TimeSpan DelayBetweenServers { get; set; } = TimeSpan.FromSeconds(30);

    [JsonPropertyName("adaptiveWaveSizing")]
    public bool AdaptiveWaveSizing { get; set; } = false;

    [JsonPropertyName("geographicConfiguration")]
    public List<GeographicWaveConfiguration>? GeographicConfiguration { get; set; }

    [JsonPropertyName("customWaves")]
    public List<CustomWaveConfiguration>? CustomWaves { get; set; }

    [JsonPropertyName("rollbackStrategy")]
    public WaveRollbackStrategy RollbackStrategy { get; set; } = WaveRollbackStrategy.ReverseOrder;
}

public class GeographicWaveConfiguration
{
    [JsonPropertyName("region")]
    public string Region { get; set; } = string.Empty;

    [JsonPropertyName("datacenter")]
    public string? Datacenter { get; set; }

    [JsonPropertyName("availabilityZone")]
    public string? AvailabilityZone { get; set; }

    [JsonPropertyName("serverPatterns")]
    public List<string> ServerPatterns { get; set; } = new();

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("delayAfterCompletion")]
    public TimeSpan DelayAfterCompletion { get; set; } = TimeSpan.Zero;
}

public class CustomWaveConfiguration
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("serverList")]
    public List<string> ServerList { get; set; } = new();

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("parallelDeployment")]
    public bool ParallelDeployment { get; set; } = true;

    [JsonPropertyName("healthCheckDelay")]
    public TimeSpan HealthCheckDelay { get; set; } = TimeSpan.FromMinutes(2);

    [JsonPropertyName("customConfiguration")]
    public Dictionary<string, object> CustomConfiguration { get; set; } = new();
}

public class RollingHealthCheckConfiguration
{
    [JsonPropertyName("healthCheckTimeout")]
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("healthCheckInterval")]
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    [JsonPropertyName("maxHealthCheckRetries")]
    public int MaxHealthCheckRetries { get; set; } = 3;

    [JsonPropertyName("healthCheckEndpoints")]
    public List<HealthCheckEndpoint> HealthCheckEndpoints { get; set; } = new();

    [JsonPropertyName("customHealthChecks")]
    public List<CustomHealthCheck> CustomHealthChecks { get; set; } = new();

    [JsonPropertyName("healthCheckGracePeriod")]
    public TimeSpan HealthCheckGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    [JsonPropertyName("requireAllEndpointsHealthy")]
    public bool RequireAllEndpointsHealthy { get; set; } = true;
}

public class HealthCheckEndpoint
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "/health";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 80;

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "HTTP";

    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    [JsonPropertyName("expectedStatusCodes")]
    public List<int> ExpectedStatusCodes { get; set; } = new() { 200 };

    [JsonPropertyName("expectedResponseContent")]
    public string? ExpectedResponseContent { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();
}

public class CustomHealthCheck
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);

    [JsonPropertyName("critical")]
    public bool Critical { get; set; } = true;
}

public class RollingMonitoringConfiguration
{
    [JsonPropertyName("enableContinuousMonitoring")]
    public bool EnableContinuousMonitoring { get; set; } = true;

    [JsonPropertyName("monitoringInterval")]
    public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromSeconds(30);

    [JsonPropertyName("metricsToMonitor")]
    public List<string> MetricsToMonitor { get; set; } = new()
    {
        "error_rate", "response_time", "throughput", "cpu_usage", "memory_usage", "disk_usage"
    };

    [JsonPropertyName("customMetrics")]
    public Dictionary<string, RollingMetricConfiguration> CustomMetrics { get; set; } = new();

    [JsonPropertyName("alertConfiguration")]
    public RollingAlertConfiguration AlertConfiguration { get; set; } = new();

    [JsonPropertyName("dashboardConfiguration")]
    public RollingDashboardConfiguration DashboardConfiguration { get; set; } = new();

    [JsonPropertyName("logMonitoring")]
    public LogMonitoringConfiguration LogMonitoring { get; set; } = new();
}

public class RollingMetricConfiguration
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("threshold")]
    public double Threshold { get; set; }

    [JsonPropertyName("comparison")]
    public MetricComparison Comparison { get; set; } = MetricComparison.LessThan;

    [JsonPropertyName("aggregationWindow")]
    public TimeSpan AggregationWindow { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("alertOnThresholdBreach")]
    public bool AlertOnThresholdBreach { get; set; } = true;

    [JsonPropertyName("rollbackOnThresholdBreach")]
    public bool RollbackOnThresholdBreach { get; set; } = false;
}

public class RollingAlertConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("alertChannels")]
    public List<AlertChannel> AlertChannels { get; set; } = new();

    [JsonPropertyName("alertSeverityMapping")]
    public Dictionary<string, AlertSeverity> AlertSeverityMapping { get; set; } = new();

    [JsonPropertyName("suppressDuplicateAlerts")]
    public bool SuppressDuplicateAlerts { get; set; } = true;

    [JsonPropertyName("alertSuppressionWindow")]
    public TimeSpan AlertSuppressionWindow { get; set; } = TimeSpan.FromMinutes(5);
}

public class RollingDashboardConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("dashboardUrl")]
    public string? DashboardUrl { get; set; }

    [JsonPropertyName("autoRefresh")]
    public bool AutoRefresh { get; set; } = true;

    [JsonPropertyName("refreshInterval")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    [JsonPropertyName("showWaveProgress")]
    public bool ShowWaveProgress { get; set; } = true;

    [JsonPropertyName("customWidgets")]
    public List<DashboardWidget> CustomWidgets { get; set; } = new();
}

public class LogMonitoringConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("logSources")]
    public List<LogSource> LogSources { get; set; } = new();

    [JsonPropertyName("errorPatterns")]
    public List<LogPattern> ErrorPatterns { get; set; } = new();

    [JsonPropertyName("warningPatterns")]
    public List<LogPattern> WarningPatterns { get; set; } = new();

    [JsonPropertyName("logAnalysisWindow")]
    public TimeSpan LogAnalysisWindow { get; set; } = TimeSpan.FromMinutes(10);
}

public class LogSource
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public LogSourceType Type { get; set; } = LogSourceType.File;

    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();
}

public class LogPattern
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    [JsonPropertyName("patternType")]
    public LogPatternType PatternType { get; set; } = LogPatternType.Regex;

    [JsonPropertyName("severity")]
    public LogPatternSeverity Severity { get; set; } = LogPatternSeverity.Warning;

    [JsonPropertyName("ignoreCase")]
    public bool IgnoreCase { get; set; } = true;
}

public class RollingValidationConfiguration
{
    [JsonPropertyName("smokeTestSuite")]
    public string SmokeTestSuite { get; set; } = string.Empty;

    [JsonPropertyName("integrationTestSuite")]
    public string IntegrationTestSuite { get; set; } = string.Empty;

    [JsonPropertyName("performanceTestSuite")]
    public string PerformanceTestSuite { get; set; } = string.Empty;

    [JsonPropertyName("loadTestConfiguration")]
    public LoadTestConfiguration LoadTestConfiguration { get; set; } = new();

    [JsonPropertyName("performanceThreshold")]
    public double PerformanceThreshold { get; set; } = 10.0; // 10% degradation threshold

    [JsonPropertyName("validationTimeout")]
    public TimeSpan ValidationTimeout { get; set; } = TimeSpan.FromMinutes(15);

    [JsonPropertyName("runValidationBetweenWaves")]
    public bool RunValidationBetweenWaves { get; set; } = true;

    [JsonPropertyName("customValidations")]
    public List<CustomValidation> CustomValidations { get; set; } = new();
}

public class LoadTestConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("testDuration")]
    public TimeSpan TestDuration { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("rampUpDuration")]
    public TimeSpan RampUpDuration { get; set; } = TimeSpan.FromMinutes(1);

    [JsonPropertyName("targetThroughput")]
    public int TargetThroughput { get; set; } = 100; // requests per second

    [JsonPropertyName("maxResponseTime")]
    public TimeSpan MaxResponseTime { get; set; } = TimeSpan.FromSeconds(5);

    [JsonPropertyName("acceptableErrorRate")]
    public double AcceptableErrorRate { get; set; } = 1.0; // 1%

    [JsonPropertyName("testScenarios")]
    public List<LoadTestScenario> TestScenarios { get; set; } = new();
}

public class LoadTestScenario
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";

    [JsonPropertyName("weight")]
    public double Weight { get; set; } = 1.0;

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

public class RollingRollbackTriggers
{
    [JsonPropertyName("errorRateThreshold")]
    public double ErrorRateThreshold { get; set; } = 5.0; // 5% error rate

    [JsonPropertyName("responseTimeThreshold")]
    public TimeSpan ResponseTimeThreshold { get; set; } = TimeSpan.FromSeconds(5);

    [JsonPropertyName("memoryUsageThreshold")]
    public double MemoryUsageThreshold { get; set; } = 90.0; // 90% memory usage

    [JsonPropertyName("cpuUsageThreshold")]
    public double CpuUsageThreshold { get; set; } = 90.0; // 90% CPU usage

    [JsonPropertyName("diskUsageThreshold")]
    public double DiskUsageThreshold { get; set; } = 95.0; // 95% disk usage

    [JsonPropertyName("healthCheckFailureThreshold")]
    public int HealthCheckFailureThreshold { get; set; } = 3;

    [JsonPropertyName("consecutiveFailureThreshold")]
    public int ConsecutiveFailureThreshold { get; set; } = 2;

    [JsonPropertyName("customTriggers")]
    public List<CustomRollbackTrigger> CustomTriggers { get; set; } = new();

    [JsonPropertyName("evaluationWindow")]
    public TimeSpan EvaluationWindow { get; set; } = TimeSpan.FromMinutes(10);

    [JsonPropertyName("minimumDataPoints")]
    public int MinimumDataPoints { get; set; } = 5;
}

public class RollingLoadBalancerConfiguration
{
    [JsonPropertyName("automaticServerManagement")]
    public bool AutomaticServerManagement { get; set; } = true;

    [JsonPropertyName("gracefulRemovalTimeout")]
    public TimeSpan GracefulRemovalTimeout { get; set; } = TimeSpan.FromMinutes(2);

    [JsonPropertyName("healthCheckBeforeAddition")]
    public bool HealthCheckBeforeAddition { get; set; } = true;

    [JsonPropertyName("drainConnectionsBeforeRemoval")]
    public bool DrainConnectionsBeforeRemoval { get; set; } = true;

    [JsonPropertyName("connectionDrainTimeout")]
    public TimeSpan ConnectionDrainTimeout { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("loadBalancerEndpoints")]
    public List<LoadBalancerEndpoint> LoadBalancerEndpoints { get; set; } = new();

    [JsonPropertyName("retryConfiguration")]
    public LoadBalancerRetryConfiguration RetryConfiguration { get; set; } = new();
}

public class LoadBalancerEndpoint
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public LoadBalancerType Type { get; set; } = LoadBalancerType.NginxPlus;

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

public class LoadBalancerRetryConfiguration
{
    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;

    [JsonPropertyName("retryDelay")]
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    [JsonPropertyName("exponentialBackoff")]
    public bool ExponentialBackoff { get; set; } = true;

    [JsonPropertyName("maxRetryDelay")]
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);
}

// Enums
public enum WaveStrategy
{
    FixedSize,
    Percentage,
    Geographic,
    Custom,
    Adaptive
}

public enum WaveRollbackStrategy
{
    ReverseOrder,
    ParallelRollback,
    CustomOrder
}

public enum LogSourceType
{
    File,
    Syslog,
    Database,
    EventLog,
    Custom
}

public enum LogPatternType
{
    Regex,
    Contains,
    StartsWith,
    EndsWith,
    Exact
}

public enum LogPatternSeverity
{
    Info,
    Warning,
    Error,
    Critical
}


// Extension methods for configuration conversion
public static class RollingConfigurationExtensions
{
    public static Dictionary<string, object> ToDictionary(this RollingConfiguration config)
    {
        return new Dictionary<string, object>
        {
            ["RollingConfiguration"] = new Dictionary<string, object>
            {
                ["MinimumAvailableInstances"] = config.DeploymentConfiguration.MinimumAvailableInstances,
                ["MinimumAvailablePercentage"] = config.DeploymentConfiguration.MinimumAvailablePercentage,
                ["ServiceDependencies"] = config.DeploymentConfiguration.ServiceDependencies,
                ["RollbackOnAnyWaveFailure"] = config.DeploymentConfiguration.RollbackOnAnyWaveFailure,
                ["ContinueOnNonCriticalFailure"] = config.DeploymentConfiguration.ContinueOnNonCriticalFailure,
                ["MaxConcurrentWaves"] = config.DeploymentConfiguration.MaxConcurrentWaves,
                ["GracefulShutdownTimeout"] = config.DeploymentConfiguration.GracefulShutdownTimeout
            },
            ["WaveConfiguration"] = new Dictionary<string, object>
            {
                ["Strategy"] = config.WaveConfiguration.Strategy.ToString(),
                ["WaveSize"] = config.WaveConfiguration.WaveSize,
                ["WavePercentage"] = config.WaveConfiguration.WavePercentage,
                ["WaveInterval"] = config.WaveConfiguration.WaveInterval,
                ["ParallelDeploymentWithinWave"] = config.WaveConfiguration.ParallelDeploymentWithinWave,
                ["MaxParallelism"] = config.WaveConfiguration.MaxParallelism,
                ["DelayBetweenServers"] = config.WaveConfiguration.DelayBetweenServers,
                ["RollbackStrategy"] = config.WaveConfiguration.RollbackStrategy.ToString()
            },
            ["HealthCheckConfiguration"] = new Dictionary<string, object>
            {
                ["HealthCheckTimeout"] = config.HealthCheckConfiguration.HealthCheckTimeout,
                ["HealthCheckInterval"] = config.HealthCheckConfiguration.HealthCheckInterval,
                ["MaxHealthCheckRetries"] = config.HealthCheckConfiguration.MaxHealthCheckRetries,
                ["HealthCheckGracePeriod"] = config.HealthCheckConfiguration.HealthCheckGracePeriod,
                ["RequireAllEndpointsHealthy"] = config.HealthCheckConfiguration.RequireAllEndpointsHealthy
            },
            ["MonitoringConfiguration"] = config.MonitoringConfiguration.ToDictionary(),
            ["ValidationConfiguration"] = new Dictionary<string, object>
            {
                ["SmokeTestSuite"] = config.ValidationConfiguration.SmokeTestSuite,
                ["IntegrationTestSuite"] = config.ValidationConfiguration.IntegrationTestSuite,
                ["PerformanceTestSuite"] = config.ValidationConfiguration.PerformanceTestSuite,
                ["PerformanceThreshold"] = config.ValidationConfiguration.PerformanceThreshold,
                ["ValidationTimeout"] = config.ValidationConfiguration.ValidationTimeout,
                ["RunValidationBetweenWaves"] = config.ValidationConfiguration.RunValidationBetweenWaves
            }
        };
    }

    public static Dictionary<string, object> ToDictionary(this RollingMonitoringConfiguration config)
    {
        return new Dictionary<string, object>
        {
            ["EnableContinuousMonitoring"] = config.EnableContinuousMonitoring,
            ["MonitoringInterval"] = config.MonitoringInterval,
            ["MetricsToMonitor"] = config.MetricsToMonitor,
            ["CustomMetrics"] = config.CustomMetrics.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<string, object>
                {
                    ["Query"] = kvp.Value.Query,
                    ["Threshold"] = kvp.Value.Threshold,
                    ["Comparison"] = kvp.Value.Comparison.ToString(),
                    ["AggregationWindow"] = kvp.Value.AggregationWindow,
                    ["AlertOnThresholdBreach"] = kvp.Value.AlertOnThresholdBreach,
                    ["RollbackOnThresholdBreach"] = kvp.Value.RollbackOnThresholdBreach
                }
            ),
            ["AlertConfiguration"] = new Dictionary<string, object>
            {
                ["Enabled"] = config.AlertConfiguration.Enabled,
                ["AlertChannels"] = config.AlertConfiguration.AlertChannels,
                ["SuppressDuplicateAlerts"] = config.AlertConfiguration.SuppressDuplicateAlerts,
                ["AlertSuppressionWindow"] = config.AlertConfiguration.AlertSuppressionWindow
            }
        };
    }

    public static RollingConfiguration FromDictionary(Dictionary<string, object> dict)
    {
        var config = new RollingConfiguration();

        if (dict.TryGetValue("RollingConfiguration", out var rollingConfigObj) && 
            rollingConfigObj is Dictionary<string, object> rollingConfig)
        {
            config.DeploymentConfiguration = ParseRollingDeploymentConfiguration(rollingConfig);
        }

        if (dict.TryGetValue("WaveConfiguration", out var waveConfigObj) && 
            waveConfigObj is Dictionary<string, object> waveConfig)
        {
            config.WaveConfiguration = ParseWaveConfiguration(waveConfig);
        }

        if (dict.TryGetValue("HealthCheckConfiguration", out var healthConfigObj) && 
            healthConfigObj is Dictionary<string, object> healthConfig)
        {
            config.HealthCheckConfiguration = ParseHealthCheckConfiguration(healthConfig);
        }

        if (dict.TryGetValue("MonitoringConfiguration", out var monitoringConfigObj) && 
            monitoringConfigObj is Dictionary<string, object> monitoringConfig)
        {
            config.MonitoringConfiguration = ParseMonitoringConfiguration(monitoringConfig);
        }

        if (dict.TryGetValue("ValidationConfiguration", out var validationConfigObj) && 
            validationConfigObj is Dictionary<string, object> validationConfig)
        {
            config.ValidationConfiguration = ParseValidationConfiguration(validationConfig);
        }

        return config;
    }

    private static RollingDeploymentConfiguration ParseRollingDeploymentConfiguration(Dictionary<string, object> dict)
    {
        var config = new RollingDeploymentConfiguration();

        if (dict.TryGetValue("MinimumAvailableInstances", out var minInstancesObj) && 
            minInstancesObj is int minInstances)
        {
            config.MinimumAvailableInstances = minInstances;
        }

        if (dict.TryGetValue("MinimumAvailablePercentage", out var minPercentageObj) && 
            minPercentageObj is double minPercentage)
        {
            config.MinimumAvailablePercentage = minPercentage;
        }

        if (dict.TryGetValue("ServiceDependencies", out var dependenciesObj) && 
            dependenciesObj is List<string> dependencies)
        {
            config.ServiceDependencies = dependencies;
        }

        if (dict.TryGetValue("RollbackOnAnyWaveFailure", out var rollbackObj) && 
            rollbackObj is bool rollback)
        {
            config.RollbackOnAnyWaveFailure = rollback;
        }

        return config;
    }

    private static WaveConfiguration ParseWaveConfiguration(Dictionary<string, object> dict)
    {
        var config = new WaveConfiguration();

        if (dict.TryGetValue("Strategy", out var strategyObj) && 
            Enum.TryParse<WaveStrategy>(strategyObj.ToString(), out var strategy))
        {
            config.Strategy = strategy;
        }

        if (dict.TryGetValue("WaveSize", out var waveSizeObj) && waveSizeObj is int waveSize)
        {
            config.WaveSize = waveSize;
        }

        if (dict.TryGetValue("WavePercentage", out var wavePercentageObj) && 
            wavePercentageObj is double wavePercentage)
        {
            config.WavePercentage = wavePercentage;
        }

        if (dict.TryGetValue("WaveInterval", out var waveIntervalObj) && 
            waveIntervalObj is TimeSpan waveInterval)
        {
            config.WaveInterval = waveInterval;
        }

        if (dict.TryGetValue("ParallelDeploymentWithinWave", out var parallelObj) && 
            parallelObj is bool parallel)
        {
            config.ParallelDeploymentWithinWave = parallel;
        }

        return config;
    }

    private static RollingHealthCheckConfiguration ParseHealthCheckConfiguration(Dictionary<string, object> dict)
    {
        var config = new RollingHealthCheckConfiguration();

        if (dict.TryGetValue("HealthCheckTimeout", out var timeoutObj) && 
            timeoutObj is TimeSpan timeout)
        {
            config.HealthCheckTimeout = timeout;
        }

        if (dict.TryGetValue("HealthCheckInterval", out var intervalObj) && 
            intervalObj is TimeSpan interval)
        {
            config.HealthCheckInterval = interval;
        }

        if (dict.TryGetValue("MaxHealthCheckRetries", out var retriesObj) && 
            retriesObj is int retries)
        {
            config.MaxHealthCheckRetries = retries;
        }

        return config;
    }

    private static RollingMonitoringConfiguration ParseMonitoringConfiguration(Dictionary<string, object> dict)
    {
        var config = new RollingMonitoringConfiguration();

        if (dict.TryGetValue("EnableContinuousMonitoring", out var enableObj) && 
            enableObj is bool enable)
        {
            config.EnableContinuousMonitoring = enable;
        }

        if (dict.TryGetValue("MonitoringInterval", out var intervalObj) && 
            intervalObj is TimeSpan interval)
        {
            config.MonitoringInterval = interval;
        }

        if (dict.TryGetValue("MetricsToMonitor", out var metricsObj) && 
            metricsObj is List<string> metrics)
        {
            config.MetricsToMonitor = metrics;
        }

        return config;
    }

    private static RollingValidationConfiguration ParseValidationConfiguration(Dictionary<string, object> dict)
    {
        var config = new RollingValidationConfiguration();

        if (dict.TryGetValue("SmokeTestSuite", out var smokeTestObj))
        {
            config.SmokeTestSuite = smokeTestObj.ToString() ?? string.Empty;
        }

        if (dict.TryGetValue("IntegrationTestSuite", out var integrationTestObj))
        {
            config.IntegrationTestSuite = integrationTestObj.ToString() ?? string.Empty;
        }

        if (dict.TryGetValue("PerformanceThreshold", out var thresholdObj) && 
            thresholdObj is double threshold)
        {
            config.PerformanceThreshold = threshold;
        }

        if (dict.TryGetValue("RunValidationBetweenWaves", out var runValidationObj) && 
            runValidationObj is bool runValidation)
        {
            config.RunValidationBetweenWaves = runValidation;
        }

        return config;
    }
}