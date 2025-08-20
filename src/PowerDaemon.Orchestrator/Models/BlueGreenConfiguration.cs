using System.Text.Json.Serialization;

namespace PowerDaemon.Orchestrator.Models;

public class BlueGreenConfiguration
{
    [JsonPropertyName("blueEnvironment")]
    public BlueGreenEnvironment BlueEnvironment { get; set; } = new();

    [JsonPropertyName("greenEnvironment")]
    public BlueGreenEnvironment GreenEnvironment { get; set; } = new();

    [JsonPropertyName("loadBalancerConfig")]
    public LoadBalancerConfiguration LoadBalancerConfig { get; set; } = new();

    [JsonPropertyName("trafficSwitchStrategy")]
    public TrafficSwitchStrategy TrafficSwitchStrategy { get; set; } = TrafficSwitchStrategy.Immediate;

    [JsonPropertyName("healthCheckTimeout")]
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("validationTimeout")]
    public TimeSpan ValidationTimeout { get; set; } = TimeSpan.FromMinutes(10);

    [JsonPropertyName("monitoringDuration")]
    public TimeSpan MonitoringDuration { get; set; } = TimeSpan.FromMinutes(15);

    [JsonPropertyName("autoRollbackEnabled")]
    public bool AutoRollbackEnabled { get; set; } = true;

    [JsonPropertyName("rollbackTriggers")]
    public BlueGreenRollbackTriggers RollbackTriggers { get; set; } = new();
}

public class BlueGreenEnvironment
{
    [JsonPropertyName("servers")]
    public List<string> Servers { get; set; } = new();

    [JsonPropertyName("loadBalancerPool")]
    public string LoadBalancerPool { get; set; } = string.Empty;

    [JsonPropertyName("healthCheckEndpoint")]
    public string HealthCheckEndpoint { get; set; } = "/health";

    [JsonPropertyName("ports")]
    public List<int> Ports { get; set; } = new();

    [JsonPropertyName("environmentVariables")]
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class LoadBalancerConfiguration
{
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public LoadBalancerType Type { get; set; } = LoadBalancerType.NginxPlus;

    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    [JsonPropertyName("retryAttempts")]
    public int RetryAttempts { get; set; } = 3;

    [JsonPropertyName("healthCheckPath")]
    public string HealthCheckPath { get; set; } = "/health";

    [JsonPropertyName("sslConfiguration")]
    public SslConfiguration SslConfiguration { get; set; } = new();
}

public class SslConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("certificatePath")]
    public string? CertificatePath { get; set; }

    [JsonPropertyName("privateKeyPath")]
    public string? PrivateKeyPath { get; set; }

    [JsonPropertyName("verifyPeerCertificate")]
    public bool VerifyPeerCertificate { get; set; } = true;
}

public class BlueGreenRollbackTriggers
{
    [JsonPropertyName("errorRateThreshold")]
    public double ErrorRateThreshold { get; set; } = 5.0; // 5% error rate

    [JsonPropertyName("responseTimeThreshold")]
    public TimeSpan ResponseTimeThreshold { get; set; } = TimeSpan.FromSeconds(5);

    [JsonPropertyName("healthCheckFailureThreshold")]
    public int HealthCheckFailureThreshold { get; set; } = 3;

    [JsonPropertyName("monitoringWindow")]
    public TimeSpan MonitoringWindow { get; set; } = TimeSpan.FromMinutes(10);

    [JsonPropertyName("minimumTrafficBeforeValidation")]
    public int MinimumTrafficBeforeValidation { get; set; } = 100;
}

public enum TrafficSwitchStrategy
{
    Immediate,
    Gradual,
    Weighted
}

public enum LoadBalancerType
{
    NginxPlus,
    HAProxy,
    F5BigIP,
    AzureLoadBalancer,
    AwsApplicationLoadBalancer,
    Custom
}

public static class BlueGreenConfigurationExtensions
{
    public static Dictionary<string, object> ToDictionary(this BlueGreenConfiguration config)
    {
        return new Dictionary<string, object>
        {
            ["BlueEnvironment"] = new Dictionary<string, object>
            {
                ["Servers"] = config.BlueEnvironment.Servers,
                ["LoadBalancerPool"] = config.BlueEnvironment.LoadBalancerPool,
                ["HealthCheckEndpoint"] = config.BlueEnvironment.HealthCheckEndpoint,
                ["Ports"] = config.BlueEnvironment.Ports,
                ["EnvironmentVariables"] = config.BlueEnvironment.EnvironmentVariables,
                ["Metadata"] = config.BlueEnvironment.Metadata
            },
            ["GreenEnvironment"] = new Dictionary<string, object>
            {
                ["Servers"] = config.GreenEnvironment.Servers,
                ["LoadBalancerPool"] = config.GreenEnvironment.LoadBalancerPool,
                ["HealthCheckEndpoint"] = config.GreenEnvironment.HealthCheckEndpoint,
                ["Ports"] = config.GreenEnvironment.Ports,
                ["EnvironmentVariables"] = config.GreenEnvironment.EnvironmentVariables,
                ["Metadata"] = config.GreenEnvironment.Metadata
            },
            ["LoadBalancerConfig"] = new Dictionary<string, object>
            {
                ["Endpoint"] = config.LoadBalancerConfig.Endpoint,
                ["ApiKey"] = config.LoadBalancerConfig.ApiKey,
                ["Type"] = config.LoadBalancerConfig.Type.ToString(),
                ["Timeout"] = config.LoadBalancerConfig.Timeout,
                ["RetryAttempts"] = config.LoadBalancerConfig.RetryAttempts,
                ["HealthCheckPath"] = config.LoadBalancerConfig.HealthCheckPath
            },
            ["TrafficSwitchStrategy"] = config.TrafficSwitchStrategy.ToString(),
            ["HealthCheckTimeout"] = config.HealthCheckTimeout,
            ["ValidationTimeout"] = config.ValidationTimeout,
            ["MonitoringDuration"] = config.MonitoringDuration,
            ["AutoRollbackEnabled"] = config.AutoRollbackEnabled,
            ["RollbackTriggers"] = new Dictionary<string, object>
            {
                ["ErrorRateThreshold"] = config.RollbackTriggers.ErrorRateThreshold,
                ["ResponseTimeThreshold"] = config.RollbackTriggers.ResponseTimeThreshold,
                ["HealthCheckFailureThreshold"] = config.RollbackTriggers.HealthCheckFailureThreshold,
                ["MonitoringWindow"] = config.RollbackTriggers.MonitoringWindow,
                ["MinimumTrafficBeforeValidation"] = config.RollbackTriggers.MinimumTrafficBeforeValidation
            }
        };
    }

    public static BlueGreenConfiguration FromDictionary(Dictionary<string, object> dict)
    {
        var config = new BlueGreenConfiguration();

        if (dict.TryGetValue("BlueEnvironment", out var blueEnvObj) && blueEnvObj is Dictionary<string, object> blueEnv)
        {
            config.BlueEnvironment = ParseEnvironment(blueEnv);
        }

        if (dict.TryGetValue("GreenEnvironment", out var greenEnvObj) && greenEnvObj is Dictionary<string, object> greenEnv)
        {
            config.GreenEnvironment = ParseEnvironment(greenEnv);
        }

        if (dict.TryGetValue("LoadBalancerConfig", out var lbConfigObj) && lbConfigObj is Dictionary<string, object> lbConfig)
        {
            config.LoadBalancerConfig = ParseLoadBalancerConfig(lbConfig);
        }

        if (dict.TryGetValue("TrafficSwitchStrategy", out var strategyObj) && 
            Enum.TryParse<TrafficSwitchStrategy>(strategyObj.ToString(), out var strategy))
        {
            config.TrafficSwitchStrategy = strategy;
        }

        if (dict.TryGetValue("HealthCheckTimeout", out var healthTimeoutObj) && healthTimeoutObj is TimeSpan healthTimeout)
        {
            config.HealthCheckTimeout = healthTimeout;
        }

        if (dict.TryGetValue("ValidationTimeout", out var validationTimeoutObj) && validationTimeoutObj is TimeSpan validationTimeout)
        {
            config.ValidationTimeout = validationTimeout;
        }

        if (dict.TryGetValue("AutoRollbackEnabled", out var autoRollbackObj) && autoRollbackObj is bool autoRollback)
        {
            config.AutoRollbackEnabled = autoRollback;
        }

        return config;
    }

    private static BlueGreenEnvironment ParseEnvironment(Dictionary<string, object> envDict)
    {
        var env = new BlueGreenEnvironment();

        if (envDict.TryGetValue("Servers", out var serversObj) && serversObj is List<string> servers)
        {
            env.Servers = servers;
        }

        if (envDict.TryGetValue("LoadBalancerPool", out var poolObj))
        {
            env.LoadBalancerPool = poolObj.ToString() ?? string.Empty;
        }

        if (envDict.TryGetValue("HealthCheckEndpoint", out var healthObj))
        {
            env.HealthCheckEndpoint = healthObj.ToString() ?? "/health";
        }

        if (envDict.TryGetValue("Ports", out var portsObj) && portsObj is List<int> ports)
        {
            env.Ports = ports;
        }

        return env;
    }

    private static LoadBalancerConfiguration ParseLoadBalancerConfig(Dictionary<string, object> lbDict)
    {
        var config = new LoadBalancerConfiguration();

        if (lbDict.TryGetValue("Endpoint", out var endpointObj))
        {
            config.Endpoint = endpointObj.ToString() ?? string.Empty;
        }

        if (lbDict.TryGetValue("ApiKey", out var apiKeyObj))
        {
            config.ApiKey = apiKeyObj.ToString() ?? string.Empty;
        }

        if (lbDict.TryGetValue("Type", out var typeObj) && 
            Enum.TryParse<LoadBalancerType>(typeObj.ToString(), out var lbType))
        {
            config.Type = lbType;
        }

        if (lbDict.TryGetValue("Timeout", out var timeoutObj) && timeoutObj is TimeSpan timeout)
        {
            config.Timeout = timeout;
        }

        if (lbDict.TryGetValue("RetryAttempts", out var retriesObj) && retriesObj is int retries)
        {
            config.RetryAttempts = retries;
        }

        return config;
    }
}