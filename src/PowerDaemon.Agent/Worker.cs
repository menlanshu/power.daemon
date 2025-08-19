using Microsoft.Extensions.Options;
using PowerDaemon.Agent.Services;
using PowerDaemon.Shared.Configuration;
using PowerDaemon.Shared.DTOs;

namespace PowerDaemon.Agent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly AgentConfiguration _config;
    private readonly IServiceDiscovery _serviceDiscovery;
    private readonly IMetricsCollector _metricsCollector;
    private readonly IGrpcClient _grpcClient;

    public Worker(
        ILogger<Worker> logger,
        IOptions<AgentConfiguration> config,
        IServiceDiscovery serviceDiscovery,
        IMetricsCollector metricsCollector,
        IGrpcClient grpcClient)
    {
        _logger = logger;
        _config = config.Value;
        _serviceDiscovery = serviceDiscovery;
        _metricsCollector = metricsCollector;
        _grpcClient = grpcClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PowerDaemon Agent starting execution");

        try
        {
            // Register with central service
            var registered = await _grpcClient.RegisterAgentAsync(stoppingToken);
            if (!registered)
            {
                _logger.LogError("Failed to register with central service. Agent will continue but may not function properly.");
            }

            // Start metrics collection
            await _metricsCollector.StartCollectionAsync(stoppingToken);

            // Main execution loop
            var heartbeatTimer = new Timer(SendHeartbeatCallback, null, 
                TimeSpan.Zero, TimeSpan.FromSeconds(_config.HeartbeatIntervalSeconds));

            var serviceDiscoveryTimer = new Timer(DiscoverServicesCallback, null,
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(_config.ServiceDiscoveryIntervalSeconds));

            // Keep the worker alive
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    _logger.LogDebug("PowerDaemon Agent heartbeat - {Time}", DateTimeOffset.Now);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Agent execution cancelled");
                    break;
                }
            }

            // Cleanup
            heartbeatTimer?.Dispose();
            serviceDiscoveryTimer?.Dispose();
            await _metricsCollector.StopCollectionAsync();

            _logger.LogInformation("PowerDaemon Agent stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in PowerDaemon Agent execution");
            throw;
        }
    }

    private async void SendHeartbeatCallback(object? state)
    {
        try
        {
            // Collect current system status for heartbeat
            var metricsSnapshot = await _metricsCollector.CollectMetricsAsync();
            
            var cpuMetric = metricsSnapshot.Metrics.FirstOrDefault(m => m.MetricName == "cpu_usage_percent");
            var memoryMetric = metricsSnapshot.Metrics.FirstOrDefault(m => m.MetricName == "memory_usage_mb");

            var heartbeat = new AgentHeartbeat
            {
                ServerId = _config.ServerId ?? Guid.NewGuid(),
                Hostname = _config.Hostname,
                AgentVersion = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
                Timestamp = DateTime.UtcNow,
                Status = AgentHealthStatus.Healthy,
                ServiceCount = 0, // Will be updated after service discovery
                CpuUsagePercent = cpuMetric?.Value ?? 0,
                MemoryUsageMb = (long)(memoryMetric?.Value ?? 0)
            };

            var success = await _grpcClient.SendHeartbeatAsync(heartbeat);
            if (success)
            {
                _logger.LogDebug("Heartbeat sent successfully");
            }
            else
            {
                _logger.LogWarning("Failed to send heartbeat");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during heartbeat");
        }
    }

    private async void DiscoverServicesCallback(object? state)
    {
        try
        {
            _logger.LogDebug("Starting periodic service discovery");

            var discoveryResult = await _serviceDiscovery.DiscoverServicesAsync();
            
            _logger.LogInformation("Discovered {ServiceCount} services", discoveryResult.Services.Count);

            // Report services to central service
            var success = await _grpcClient.ReportServicesAsync(discoveryResult);
            if (success)
            {
                _logger.LogDebug("Services reported successfully to central service");
            }
            else
            {
                _logger.LogWarning("Failed to report services to central service");
            }

            // Also stream current metrics
            var metrics = await _metricsCollector.CollectMetricsAsync();
            if (metrics.Metrics.Count > 0)
            {
                var metricsSuccess = await _grpcClient.StreamMetricsAsync(metrics);
                if (metricsSuccess)
                {
                    _logger.LogDebug("Metrics streamed successfully");
                }
                else
                {
                    _logger.LogWarning("Failed to stream metrics");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during service discovery");
        }
    }
}
