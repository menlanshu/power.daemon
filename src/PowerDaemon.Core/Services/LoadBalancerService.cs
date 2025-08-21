using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Monitoring.Configuration;
using System.Collections.Concurrent;

namespace PowerDaemon.Core.Services;

public interface ILoadBalancerService
{
    Task<string> GetNextInstanceAsync(string serviceType, CancellationToken cancellationToken = default);
    Task RegisterInstanceAsync(string serviceType, string instanceId, ServiceInstance instance, CancellationToken cancellationToken = default);
    Task UnregisterInstanceAsync(string serviceType, string instanceId, CancellationToken cancellationToken = default);
    Task<List<ServiceInstance>> GetHealthyInstancesAsync(string serviceType, CancellationToken cancellationToken = default);
    Task<LoadBalancerStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task UpdateInstanceHealthAsync(string serviceType, string instanceId, bool isHealthy, CancellationToken cancellationToken = default);
}

public class LoadBalancerService : ILoadBalancerService
{
    private readonly ILogger<LoadBalancerService> _logger;
    private readonly MonitoringConfiguration _config;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ServiceInstance>> _serviceInstances;
    private readonly ConcurrentDictionary<string, int> _roundRobinCounters;
    private readonly Random _random;

    public LoadBalancerService(
        ILogger<LoadBalancerService> logger,
        IOptions<MonitoringConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
        _serviceInstances = new();
        _roundRobinCounters = new();
        _random = new Random();
    }

    public async Task<string> GetNextInstanceAsync(string serviceType, CancellationToken cancellationToken = default)
    {
        var healthyInstances = await GetHealthyInstancesAsync(serviceType, cancellationToken);
        
        if (!healthyInstances.Any())
        {
            _logger.LogWarning("No healthy instances available for service type: {ServiceType}", serviceType);
            throw new InvalidOperationException($"No healthy instances available for service type: {serviceType}");
        }

        var strategy = _config.ProductionScale.Performance.LoadBalancingStrategy.ToLower();
        return strategy switch
        {
            "round-robin" => GetRoundRobinInstance(serviceType, healthyInstances),
            "random" => GetRandomInstance(healthyInstances),
            "least-connections" => GetLeastConnectionsInstance(healthyInstances),
            "weighted" => GetWeightedInstance(healthyInstances),
            _ => GetRoundRobinInstance(serviceType, healthyInstances)
        };
    }

    public async Task RegisterInstanceAsync(string serviceType, string instanceId, ServiceInstance instance, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        var instances = _serviceInstances.GetOrAdd(serviceType, _ => new ConcurrentDictionary<string, ServiceInstance>());
        instance.RegisteredAt = DateTime.UtcNow;
        instance.LastHealthCheck = DateTime.UtcNow;
        
        instances.AddOrUpdate(instanceId, instance, (key, existing) => instance);
        
        _logger.LogInformation("Registered instance {InstanceId} for service {ServiceType} at {Endpoint}", 
            instanceId, serviceType, instance.Endpoint);
    }

    public async Task UnregisterInstanceAsync(string serviceType, string instanceId, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        if (_serviceInstances.TryGetValue(serviceType, out var instances))
        {
            if (instances.TryRemove(instanceId, out var removedInstance))
            {
                _logger.LogInformation("Unregistered instance {InstanceId} for service {ServiceType}", 
                    instanceId, serviceType);
            }
        }
    }

    public async Task<List<ServiceInstance>> GetHealthyInstancesAsync(string serviceType, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        if (!_serviceInstances.TryGetValue(serviceType, out var instances))
        {
            return new List<ServiceInstance>();
        }

        var healthyInstances = instances.Values
            .Where(i => i.IsHealthy && i.LastHealthCheck > DateTime.UtcNow.AddMinutes(-2))
            .ToList();

        return healthyInstances;
    }

    public async Task<LoadBalancerStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        var stats = new LoadBalancerStatistics
        {
            ServiceTypes = _serviceInstances.Keys.ToList(),
            TotalInstances = _serviceInstances.Values.Sum(instances => instances.Count),
            HealthyInstances = 0,
            UnhealthyInstances = 0,
            LoadDistribution = new Dictionary<string, Dictionary<string, int>>()
        };

        foreach (var (serviceType, instances) in _serviceInstances)
        {
            var healthy = instances.Values.Count(i => i.IsHealthy);
            var unhealthy = instances.Count - healthy;
            
            stats.HealthyInstances += healthy;
            stats.UnhealthyInstances += unhealthy;
            
            stats.LoadDistribution[serviceType] = instances.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.RequestCount
            );
        }

        return stats;
    }

    public async Task UpdateInstanceHealthAsync(string serviceType, string instanceId, bool isHealthy, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        if (_serviceInstances.TryGetValue(serviceType, out var instances))
        {
            if (instances.TryGetValue(instanceId, out var instance))
            {
                var wasHealthy = instance.IsHealthy;
                instance.IsHealthy = isHealthy;
                instance.LastHealthCheck = DateTime.UtcNow;
                
                if (wasHealthy != isHealthy)
                {
                    _logger.LogInformation("Instance {InstanceId} health changed from {WasHealthy} to {IsHealthy}",
                        instanceId, wasHealthy, isHealthy);
                }
            }
        }
    }

    private string GetRoundRobinInstance(string serviceType, List<ServiceInstance> instances)
    {
        var counter = _roundRobinCounters.AddOrUpdate(serviceType, 0, (key, value) => (value + 1) % instances.Count);
        var selectedInstance = instances[counter];
        selectedInstance.RequestCount++;
        return selectedInstance.InstanceId;
    }

    private string GetRandomInstance(List<ServiceInstance> instances)
    {
        var index = _random.Next(instances.Count);
        var selectedInstance = instances[index];
        selectedInstance.RequestCount++;
        return selectedInstance.InstanceId;
    }

    private string GetLeastConnectionsInstance(List<ServiceInstance> instances)
    {
        var selectedInstance = instances.OrderBy(i => i.RequestCount).First();
        selectedInstance.RequestCount++;
        return selectedInstance.InstanceId;
    }

    private string GetWeightedInstance(List<ServiceInstance> instances)
    {
        var totalWeight = instances.Sum(i => i.Weight);
        var randomValue = _random.NextDouble() * totalWeight;
        
        double currentWeight = 0;
        foreach (var instance in instances)
        {
            currentWeight += instance.Weight;
            if (randomValue <= currentWeight)
            {
                instance.RequestCount++;
                return instance.InstanceId;
            }
        }
        
        // Fallback to first instance
        var fallbackInstance = instances.First();
        fallbackInstance.RequestCount++;
        return fallbackInstance.InstanceId;
    }
}

public class ServiceInstance
{
    public string InstanceId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public bool IsHealthy { get; set; } = true;
    public DateTime RegisteredAt { get; set; }
    public DateTime LastHealthCheck { get; set; }
    public int RequestCount { get; set; }
    public double Weight { get; set; } = 1.0;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public int CpuUsagePercent { get; set; }
    public int MemoryUsagePercent { get; set; }
    public TimeSpan AverageResponseTime { get; set; }
}

public class LoadBalancerStatistics
{
    public List<string> ServiceTypes { get; set; } = new();
    public int TotalInstances { get; set; }
    public int HealthyInstances { get; set; }
    public int UnhealthyInstances { get; set; }
    public Dictionary<string, Dictionary<string, int>> LoadDistribution { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

// Background service for health checking instances
public class InstanceHealthCheckService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly ILogger<InstanceHealthCheckService> _logger;
    private readonly ILoadBalancerService _loadBalancer;
    private readonly HttpClient _httpClient;

    public InstanceHealthCheckService(
        ILogger<InstanceHealthCheckService> logger,
        ILoadBalancerService loadBalancer,
        HttpClient httpClient)
    {
        _logger = logger;
        _loadBalancer = loadBalancer;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Instance health check service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformHealthChecksAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during health check cycle");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Instance health check service stopped");
    }

    private async Task PerformHealthChecksAsync(CancellationToken cancellationToken)
    {
        var stats = await _loadBalancer.GetStatisticsAsync(cancellationToken);
        
        foreach (var serviceType in stats.ServiceTypes)
        {
            var instances = await _loadBalancer.GetHealthyInstancesAsync(serviceType, cancellationToken);
            
            var healthCheckTasks = instances.Select(async instance =>
            {
                try
                {
                    var healthCheckUrl = $"{instance.Endpoint.TrimEnd('/')}/health";
                    var response = await _httpClient.GetAsync(healthCheckUrl, cancellationToken);
                    var isHealthy = response.IsSuccessStatusCode;
                    
                    await _loadBalancer.UpdateInstanceHealthAsync(serviceType, instance.InstanceId, isHealthy, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Health check failed for instance {InstanceId}", instance.InstanceId);
                    await _loadBalancer.UpdateInstanceHealthAsync(serviceType, instance.InstanceId, false, cancellationToken);
                }
            });

            await Task.WhenAll(healthCheckTasks);
        }
    }
}