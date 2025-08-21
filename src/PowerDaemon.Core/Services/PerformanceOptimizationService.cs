using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Cache.Configuration;
using PowerDaemon.Messaging.Configuration;
using System.Diagnostics;

namespace PowerDaemon.Core.Services;

public interface IPerformanceOptimizationService
{
    Task OptimizeForProductionScaleAsync(int serverCount, CancellationToken cancellationToken = default);
    Task MonitorPerformanceAsync(CancellationToken cancellationToken = default);
    Task<PerformanceMetrics> GetPerformanceMetricsAsync(CancellationToken cancellationToken = default);
    Task ApplyAutoScalingAsync(CancellationToken cancellationToken = default);
}

public class PerformanceOptimizationService : IPerformanceOptimizationService
{
    private readonly ILogger<PerformanceOptimizationService> _logger;
    private readonly MonitoringConfiguration _monitoringConfig;
    private readonly RedisConfiguration _redisConfig;
    private readonly RabbitMQConfiguration _rabbitmqConfig;
    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter _memoryCounter;

    public PerformanceOptimizationService(
        ILogger<PerformanceOptimizationService> logger,
        IOptions<MonitoringConfiguration> monitoringConfig,
        IOptions<RedisConfiguration> redisConfig,
        IOptions<RabbitMQConfiguration> rabbitmqConfig)
    {
        _logger = logger;
        _monitoringConfig = monitoringConfig.Value;
        _redisConfig = redisConfig.Value;
        _rabbitmqConfig = rabbitmqConfig.Value;
        
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
    }

    public async Task OptimizeForProductionScaleAsync(int serverCount, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Optimizing system for production scale: {ServerCount} servers", serverCount);

        await OptimizeRabbitMQSettingsAsync(serverCount);
        await OptimizeRedisSettingsAsync(serverCount);
        await OptimizeMonitoringSettingsAsync(serverCount);
        
        _logger.LogInformation("Production scale optimization completed for {ServerCount} servers", serverCount);
    }

    public async Task MonitorPerformanceAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var metrics = await GetPerformanceMetricsAsync(cancellationToken);
                
                if (metrics.CpuUsagePercent > _monitoringConfig.ProductionScale.Performance.ScaleUpThreshold)
                {
                    _logger.LogWarning("High CPU usage detected: {CpuUsage}%", metrics.CpuUsagePercent);
                    await ApplyAutoScalingAsync(cancellationToken);
                }

                if (metrics.MemoryUsageMB > _monitoringConfig.ProductionScale.Performance.MemoryThresholdMB)
                {
                    _logger.LogWarning("High memory usage detected: {MemoryUsage}MB", metrics.MemoryUsageMB);
                    await OptimizeMemoryUsageAsync();
                }

                await Task.Delay(TimeSpan.FromSeconds(_monitoringConfig.ProductionScale.Performance.HealthCheckIntervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring performance");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    public async Task<PerformanceMetrics> GetPerformanceMetricsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var process = Process.GetCurrentProcess();
        var cpuUsage = _cpuCounter.NextValue();
        var availableMemory = _memoryCounter.NextValue();
        var usedMemory = process.WorkingSet64 / 1024 / 1024; // Convert to MB

        return new PerformanceMetrics
        {
            CpuUsagePercent = cpuUsage,
            MemoryUsageMB = usedMemory,
            AvailableMemoryMB = availableMemory,
            ThreadCount = process.Threads.Count,
            HandleCount = process.HandleCount,
            ProcessorCount = Environment.ProcessorCount,
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task ApplyAutoScalingAsync(CancellationToken cancellationToken = default)
    {
        if (!_monitoringConfig.ProductionScale.Performance.EnableAutoScaling)
        {
            return;
        }

        _logger.LogInformation("Applying auto-scaling optimizations");

        // Increase batch sizes to improve throughput
        _monitoringConfig.ProductionScale.DataProcessing.BatchSize = Math.Min(
            _monitoringConfig.ProductionScale.DataProcessing.BatchSize * 2, 
            10000);

        // Increase parallel processing
        _monitoringConfig.ProductionScale.DataProcessing.ProcessingThreads = Math.Min(
            _monitoringConfig.ProductionScale.DataProcessing.ProcessingThreads + 2,
            Environment.ProcessorCount * 2);

        // Optimize caching
        if (_redisConfig.ProductionScale.EnableConnectionPooling)
        {
            _redisConfig.ProductionScale.MaxConnectionPoolSize = Math.Min(
                _redisConfig.ProductionScale.MaxConnectionPoolSize + 10,
                200);
        }

        await Task.CompletedTask;
        _logger.LogInformation("Auto-scaling optimizations applied");
    }

    private async Task OptimizeRabbitMQSettingsAsync(int serverCount)
    {
        var config = _rabbitmqConfig.ProductionScale;
        
        // Scale connection pool based on server count
        config.MaxConnectionPoolSize = Math.Max(50, serverCount / 4);
        config.ChannelPoolSize = Math.Max(100, serverCount / 2);
        
        // Optimize prefetch count for high throughput
        config.PrefetchCount = Math.Min(100, Math.Max(10, serverCount / 10));
        
        // Scale consumer threads
        config.ConsumerThreadCount = Math.Min(20, Math.Max(5, serverCount / 20));
        
        // Adjust rate limiting
        config.MaxMessagesPerSecond = Math.Max(1000, serverCount * 5);
        config.MaxConcurrentOperations = Math.Max(200, serverCount);

        _logger.LogInformation("RabbitMQ optimized for {ServerCount} servers", serverCount);
        await Task.CompletedTask;
    }

    private async Task OptimizeRedisSettingsAsync(int serverCount)
    {
        var config = _redisConfig.ProductionScale;
        
        // Scale connection pooling
        config.MaxConnectionPoolSize = Math.Max(100, serverCount / 2);
        config.MinConnectionPoolSize = Math.Max(20, serverCount / 10);
        
        // Optimize concurrent operations
        config.MaxConcurrentOperations = Math.Max(500, serverCount * 2);
        
        // Scale sharding if enabled
        if (config.EnableSharding)
        {
            config.ShardCount = Math.Max(8, (int)Math.Ceiling(serverCount / 25.0));
        }
        
        // Adjust memory limits
        config.MaxMemoryUsageMB = Math.Max(4096, serverCount * 20);
        
        // Scale rate limiting
        config.MaxRequestsPerSecond = Math.Max(10000, serverCount * 50);
        config.MaxBatchSize = Math.Max(1000, serverCount * 5);

        _logger.LogInformation("Redis optimized for {ServerCount} servers", serverCount);
        await Task.CompletedTask;
    }

    private async Task OptimizeMonitoringSettingsAsync(int serverCount)
    {
        var config = _monitoringConfig.ProductionScale;
        
        // Scale data processing
        config.DataProcessing.BatchSize = Math.Max(1000, serverCount * 5);
        config.DataProcessing.ProcessingThreads = Math.Min(50, Math.Max(10, serverCount / 20));
        config.DataProcessing.QueueCapacity = Math.Max(50000, serverCount * 250);
        config.DataProcessing.MaxConcurrentOperations = Math.Max(500, serverCount * 2);
        
        // Scale alerting
        config.AlertingScale.MaxAlertsPerSecond = Math.Max(100, serverCount / 2);
        config.AlertingScale.AlertBatchSize = Math.Max(50, serverCount / 4);
        config.AlertingScale.EvaluationParallelism = Math.Min(20, Math.Max(8, serverCount / 25));
        
        // Scale metrics collection
        config.MetricsScale.CollectionParallelism = Math.Min(50, Math.Max(16, serverCount / 12));
        config.MetricsScale.AggregationBatchSize = Math.Max(1000, serverCount * 5);
        config.MetricsScale.MaxMetricsPerServer = Math.Max(1000, Math.Min(10000, serverCount * 5));
        
        // Scale dashboard handling
        config.DashboardScale.MaxConcurrentDashboards = Math.Max(200, serverCount);
        config.DashboardScale.MaxDataPointsPerWidget = Math.Max(2000, Math.Min(10000, serverCount * 10));

        _logger.LogInformation("Monitoring optimized for {ServerCount} servers", serverCount);
        await Task.CompletedTask;
    }

    private async Task OptimizeMemoryUsageAsync()
    {
        _logger.LogInformation("Optimizing memory usage");
        
        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Reduce cache sizes temporarily
        _redisConfig.ProductionScale.MaxMemoryUsageMB = (int)(_redisConfig.ProductionScale.MaxMemoryUsageMB * 0.8);
        
        // Reduce batch sizes to lower memory pressure
        _monitoringConfig.ProductionScale.DataProcessing.BatchSize = 
            Math.Max(100, _monitoringConfig.ProductionScale.DataProcessing.BatchSize / 2);
            
        await Task.CompletedTask;
    }
}

public class PerformanceMetrics
{
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageMB { get; set; }
    public double AvailableMemoryMB { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public int ProcessorCount { get; set; }
    public DateTime Timestamp { get; set; }
}

// Background service for continuous performance monitoring
public class PerformanceMonitoringService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly ILogger<PerformanceMonitoringService> _logger;
    private readonly IPerformanceOptimizationService _performanceService;

    public PerformanceMonitoringService(
        ILogger<PerformanceMonitoringService> logger,
        IPerformanceOptimizationService performanceService)
    {
        _logger = logger;
        _performanceService = performanceService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Performance monitoring service started");

        try
        {
            await _performanceService.MonitorPerformanceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Performance monitoring service encountered an error");
        }

        _logger.LogInformation("Performance monitoring service stopped");
    }
}