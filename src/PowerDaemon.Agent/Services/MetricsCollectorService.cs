using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Shared.Configuration;
using PowerDaemon.Shared.DTOs;
using PowerDaemon.Shared.Models;

namespace PowerDaemon.Agent.Services;

public class MetricsCollectorService : IMetricsCollector
{
    private readonly ILogger<MetricsCollectorService> _logger;
    private readonly AgentConfiguration _config;
    private readonly Timer? _collectionTimer;
    private readonly List<MetricDataDto> _bufferedMetrics = new();
    private readonly object _bufferLock = new();
    private CancellationTokenSource? _collectionCts;

    // Performance counters for Windows
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _memoryCounter;
    private readonly Dictionary<string, PerformanceCounter> _diskCounters = new();
    private readonly Dictionary<string, PerformanceCounter> _networkCounters = new();

    public MetricsCollectorService(
        ILogger<MetricsCollectorService> logger,
        IOptions<AgentConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;

        // Initialize performance counters for Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            InitializeWindowsPerformanceCounters();
        }
    }

    public async Task<MetricBatchDto> CollectMetricsAsync(CancellationToken cancellationToken = default)
    {
        var metrics = new List<MetricDataDto>();
        var timestamp = DateTime.UtcNow;

        try
        {
            _logger.LogDebug("Collecting system metrics at {Timestamp}", timestamp);

            // Collect system-level metrics
            await CollectSystemMetricsAsync(metrics, timestamp, cancellationToken);

            // Collect per-service metrics for running C# services
            await CollectServiceMetricsAsync(metrics, timestamp, cancellationToken);

            _logger.LogDebug("Collected {MetricCount} metrics", metrics.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during metrics collection");
        }

        return new MetricBatchDto
        {
            ServerId = _config.ServerId ?? Guid.NewGuid(),
            Hostname = _config.Hostname,
            Metrics = metrics,
            CollectedAt = timestamp
        };
    }

    public async Task StartCollectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting metrics collection with {IntervalSeconds}s interval", 
            _config.MetricsCollectionIntervalSeconds);
        
        _collectionCts = new CancellationTokenSource();
        
        // Start periodic collection
        _ = Task.Run(async () => await PeriodicCollectionAsync(_collectionCts.Token), cancellationToken);
        
        await Task.CompletedTask;
    }

    public async Task StopCollectionAsync()
    {
        _logger.LogInformation("Stopping metrics collection");
        _collectionCts?.Cancel();
        _collectionCts?.Dispose();
        
        // Dispose performance counters
        _cpuCounter?.Dispose();
        _memoryCounter?.Dispose();
        
        foreach (var counter in _diskCounters.Values)
            counter.Dispose();
        _diskCounters.Clear();
        
        foreach (var counter in _networkCounters.Values)
            counter.Dispose();
        _networkCounters.Clear();
        
        await Task.CompletedTask;
    }

    private async Task PeriodicCollectionAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var batch = await CollectMetricsAsync(cancellationToken);
                
                // Buffer metrics locally
                lock (_bufferLock)
                {
                    _bufferedMetrics.AddRange(batch.Metrics);
                    
                    // Clean old metrics if buffer is getting too large
                    if (_bufferedMetrics.Count > _config.MetricsBatchSize * 10)
                    {
                        var retentionTime = DateTime.UtcNow.AddDays(-_config.MetricsRetentionDays);
                        _bufferedMetrics.RemoveAll(m => m.Timestamp < retentionTime);
                        _logger.LogDebug("Cleaned old metrics, buffer size: {BufferSize}", _bufferedMetrics.Count);
                    }
                }
                
                await Task.Delay(TimeSpan.FromSeconds(_config.MetricsCollectionIntervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in periodic metrics collection");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken); // Wait before retrying
            }
        }
    }

    private async Task CollectSystemMetricsAsync(List<MetricDataDto> metrics, DateTime timestamp, CancellationToken cancellationToken)
    {
        var serverId = _config.ServerId ?? Guid.NewGuid();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await CollectWindowsSystemMetricsAsync(metrics, serverId, timestamp, cancellationToken);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await CollectLinuxSystemMetricsAsync(metrics, serverId, timestamp, cancellationToken);
        }
    }

    private async Task CollectWindowsSystemMetricsAsync(List<MetricDataDto> metrics, Guid serverId, DateTime timestamp, CancellationToken cancellationToken)
    {
        try
        {
            // CPU Usage
            if (_cpuCounter != null)
            {
                var cpuUsage = _cpuCounter.NextValue();
                metrics.Add(new MetricDataDto
                {
                    ServerId = serverId,
                    MetricType = MetricTypes.Cpu,
                    MetricName = MetricNames.CpuUsagePercent,
                    Value = cpuUsage,
                    Unit = "%",
                    Timestamp = timestamp
                });
            }

            // Memory Usage
            var memoryInfo = GC.GetGCMemoryInfo();
            var totalMemory = memoryInfo.TotalAvailableMemoryBytes;
            var usedMemory = totalMemory - memoryInfo.HighMemoryLoadThresholdBytes;
            
            metrics.Add(new MetricDataDto
            {
                ServerId = serverId,
                MetricType = MetricTypes.Memory,
                MetricName = MetricNames.MemoryUsageMb,
                Value = usedMemory / (1024.0 * 1024.0),
                Unit = "MB",
                Timestamp = timestamp
            });

            metrics.Add(new MetricDataDto
            {
                ServerId = serverId,
                MetricType = MetricTypes.Memory,
                MetricName = MetricNames.MemoryUsagePercent,
                Value = (usedMemory / (double)totalMemory) * 100,
                Unit = "%",
                Timestamp = timestamp
            });

            // Disk and Network metrics from performance counters
            foreach (var (name, counter) in _diskCounters)
            {
                var value = counter.NextValue();
                metrics.Add(new MetricDataDto
                {
                    ServerId = serverId,
                    MetricType = MetricTypes.Disk,
                    MetricName = name,
                    Value = value,
                    Unit = name.Contains("Bytes") ? "B/s" : "ops/s",
                    Timestamp = timestamp
                });
            }

            foreach (var (name, counter) in _networkCounters)
            {
                var value = counter.NextValue();
                metrics.Add(new MetricDataDto
                {
                    ServerId = serverId,
                    MetricType = MetricTypes.Network,
                    MetricName = name,
                    Value = value,
                    Unit = "B/s",
                    Timestamp = timestamp
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting Windows system metrics");
        }

        await Task.CompletedTask;
    }

    private async Task CollectLinuxSystemMetricsAsync(List<MetricDataDto> metrics, Guid serverId, DateTime timestamp, CancellationToken cancellationToken)
    {
        try
        {
            // CPU Usage from /proc/stat
            await CollectLinuxCpuMetricsAsync(metrics, serverId, timestamp, cancellationToken);

            // Memory Usage from /proc/meminfo
            await CollectLinuxMemoryMetricsAsync(metrics, serverId, timestamp, cancellationToken);

            // Disk Usage from /proc/diskstats
            await CollectLinuxDiskMetricsAsync(metrics, serverId, timestamp, cancellationToken);

            // Network Usage from /proc/net/dev
            await CollectLinuxNetworkMetricsAsync(metrics, serverId, timestamp, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting Linux system metrics");
        }
    }

    private async Task CollectLinuxCpuMetricsAsync(List<MetricDataDto> metrics, Guid serverId, DateTime timestamp, CancellationToken cancellationToken)
    {
        try
        {
            var cpuInfo = await File.ReadAllTextAsync("/proc/stat", cancellationToken);
            var lines = cpuInfo.Split('\n');
            var cpuLine = lines.FirstOrDefault(l => l.StartsWith("cpu "));
            
            if (cpuLine != null)
            {
                var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 8)
                {
                    var user = long.Parse(parts[1]);
                    var nice = long.Parse(parts[2]);
                    var system = long.Parse(parts[3]);
                    var idle = long.Parse(parts[4]);
                    var iowait = long.Parse(parts[5]);
                    var irq = long.Parse(parts[6]);
                    var softirq = long.Parse(parts[7]);

                    var total = user + nice + system + idle + iowait + irq + softirq;
                    var activeTime = total - idle;
                    var cpuUsage = (activeTime / (double)total) * 100;

                    metrics.Add(new MetricDataDto
                    {
                        ServerId = serverId,
                        MetricType = MetricTypes.Cpu,
                        MetricName = MetricNames.CpuUsagePercent,
                        Value = cpuUsage,
                        Unit = "%",
                        Timestamp = timestamp
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read CPU metrics from /proc/stat");
        }
    }

    private async Task CollectLinuxMemoryMetricsAsync(List<MetricDataDto> metrics, Guid serverId, DateTime timestamp, CancellationToken cancellationToken)
    {
        try
        {
            var memInfo = await File.ReadAllTextAsync("/proc/meminfo", cancellationToken);
            var lines = memInfo.Split('\n');

            var memTotal = ExtractMemoryValue(lines, "MemTotal:");
            var memFree = ExtractMemoryValue(lines, "MemFree:");
            var memAvailable = ExtractMemoryValue(lines, "MemAvailable:");

            if (memTotal > 0)
            {
                var memUsed = memTotal - (memAvailable > 0 ? memAvailable : memFree);
                var memUsedMb = memUsed / 1024.0; // Convert KB to MB
                var memUsedPercent = (memUsed / (double)memTotal) * 100;

                metrics.Add(new MetricDataDto
                {
                    ServerId = serverId,
                    MetricType = MetricTypes.Memory,
                    MetricName = MetricNames.MemoryUsageMb,
                    Value = memUsedMb,
                    Unit = "MB",
                    Timestamp = timestamp
                });

                metrics.Add(new MetricDataDto
                {
                    ServerId = serverId,
                    MetricType = MetricTypes.Memory,
                    MetricName = MetricNames.MemoryUsagePercent,
                    Value = memUsedPercent,
                    Unit = "%",
                    Timestamp = timestamp
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read memory metrics from /proc/meminfo");
        }
    }

    private async Task CollectLinuxDiskMetricsAsync(List<MetricDataDto> metrics, Guid serverId, DateTime timestamp, CancellationToken cancellationToken)
    {
        try
        {
            var diskStats = await File.ReadAllTextAsync("/proc/diskstats", cancellationToken);
            var lines = diskStats.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 14)
                {
                    var deviceName = parts[2];
                    
                    // Skip partitions and focus on main devices
                    if (deviceName.StartsWith("sd") && deviceName.Length == 3) // sda, sdb, etc.
                    {
                        var readKbps = long.Parse(parts[5]) * 512 / 1024.0; // sectors to KB
                        var writeKbps = long.Parse(parts[9]) * 512 / 1024.0;

                        metrics.Add(new MetricDataDto
                        {
                            ServerId = serverId,
                            MetricType = MetricTypes.Disk,
                            MetricName = MetricNames.DiskReadKbps,
                            Value = readKbps,
                            Unit = "KB/s",
                            Timestamp = timestamp,
                            Tags = new() { ["device"] = deviceName }
                        });

                        metrics.Add(new MetricDataDto
                        {
                            ServerId = serverId,
                            MetricType = MetricTypes.Disk,
                            MetricName = MetricNames.DiskWriteKbps,
                            Value = writeKbps,
                            Unit = "KB/s",
                            Timestamp = timestamp,
                            Tags = new() { ["device"] = deviceName }
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read disk metrics from /proc/diskstats");
        }
    }

    private async Task CollectLinuxNetworkMetricsAsync(List<MetricDataDto> metrics, Guid serverId, DateTime timestamp, CancellationToken cancellationToken)
    {
        try
        {
            var netDev = await File.ReadAllTextAsync("/proc/net/dev", cancellationToken);
            var lines = netDev.Split('\n').Skip(2); // Skip header lines

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var interfaceName = parts[0].Trim();
                    var stats = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (stats.Length >= 16 && !interfaceName.Equals("lo")) // Skip loopback
                    {
                        var rxBytes = long.Parse(stats[0]);
                        var txBytes = long.Parse(stats[8]);

                        metrics.Add(new MetricDataDto
                        {
                            ServerId = serverId,
                            MetricType = MetricTypes.Network,
                            MetricName = MetricNames.NetworkInKbps,
                            Value = rxBytes / 1024.0,
                            Unit = "KB/s",
                            Timestamp = timestamp,
                            Tags = new() { ["interface"] = interfaceName }
                        });

                        metrics.Add(new MetricDataDto
                        {
                            ServerId = serverId,
                            MetricType = MetricTypes.Network,
                            MetricName = MetricNames.NetworkOutKbps,
                            Value = txBytes / 1024.0,
                            Unit = "KB/s",
                            Timestamp = timestamp,
                            Tags = new() { ["interface"] = interfaceName }
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read network metrics from /proc/net/dev");
        }
    }

    private async Task CollectServiceMetricsAsync(List<MetricDataDto> metrics, DateTime timestamp, CancellationToken cancellationToken)
    {
        try
        {
            var serverId = _config.ServerId ?? Guid.NewGuid();
            
            // Get all running processes that might be .NET services
            var processes = Process.GetProcesses()
                .Where(p => !p.HasExited && p.ProcessName.Contains("dotnet", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var process in processes)
            {
                try
                {
                    var serviceId = Guid.NewGuid(); // In real implementation, map to actual service

                    // Thread count
                    metrics.Add(new MetricDataDto
                    {
                        ServerId = serverId,
                        ServiceId = serviceId,
                        MetricType = MetricTypes.Custom,
                        MetricName = MetricNames.ThreadCount,
                        Value = process.Threads.Count,
                        Unit = "count",
                        Timestamp = timestamp,
                        Tags = new() { ["process"] = process.ProcessName, ["pid"] = process.Id.ToString() }
                    });

                    // Handle count (Windows only)
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        metrics.Add(new MetricDataDto
                        {
                            ServerId = serverId,
                            ServiceId = serviceId,
                            MetricType = MetricTypes.Custom,
                            MetricName = MetricNames.HandleCount,
                            Value = process.HandleCount,
                            Unit = "count",
                            Timestamp = timestamp,
                            Tags = new() { ["process"] = process.ProcessName, ["pid"] = process.Id.ToString() }
                        });
                    }

                    // Memory usage
                    metrics.Add(new MetricDataDto
                    {
                        ServerId = serverId,
                        ServiceId = serviceId,
                        MetricType = MetricTypes.Memory,
                        MetricName = MetricNames.MemoryUsageMb,
                        Value = process.WorkingSet64 / (1024.0 * 1024.0),
                        Unit = "MB",
                        Timestamp = timestamp,
                        Tags = new() { ["process"] = process.ProcessName, ["pid"] = process.Id.ToString() }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not collect metrics for process {ProcessName}", process.ProcessName);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting service-specific metrics");
        }

        await Task.CompletedTask;
    }

    private void InitializeWindowsPerformanceCounters()
    {
        try
        {
            // CPU counter
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            
            // Initialize disk counters for logical disks
            var diskCategory = new PerformanceCounterCategory("LogicalDisk");
            foreach (var instanceName in diskCategory.GetInstanceNames())
            {
                if (instanceName != "_Total")
                {
                    try
                    {
                        _diskCounters[$"disk_read_bytes_per_sec_{instanceName}"] = 
                            new PerformanceCounter("LogicalDisk", "Disk Read Bytes/sec", instanceName);
                        _diskCounters[$"disk_write_bytes_per_sec_{instanceName}"] = 
                            new PerformanceCounter("LogicalDisk", "Disk Write Bytes/sec", instanceName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not create disk counter for {InstanceName}", instanceName);
                    }
                }
            }

            // Initialize network counters
            var networkCategory = new PerformanceCounterCategory("Network Interface");
            foreach (var instanceName in networkCategory.GetInstanceNames())
            {
                if (!instanceName.Contains("Loopback"))
                {
                    try
                    {
                        _networkCounters[$"network_bytes_received_per_sec_{instanceName}"] = 
                            new PerformanceCounter("Network Interface", "Bytes Received/sec", instanceName);
                        _networkCounters[$"network_bytes_sent_per_sec_{instanceName}"] = 
                            new PerformanceCounter("Network Interface", "Bytes Sent/sec", instanceName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not create network counter for {InstanceName}", instanceName);
                    }
                }
            }

            _logger.LogInformation("Initialized {CounterCount} Windows performance counters", 
                _diskCounters.Count + _networkCounters.Count + 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not initialize Windows performance counters");
        }
    }

    private static long ExtractMemoryValue(string[] lines, string prefix)
    {
        var line = lines.FirstOrDefault(l => l.StartsWith(prefix));
        if (line != null)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && long.TryParse(parts[1], out var value))
            {
                return value; // Value in KB
            }
        }
        return 0;
    }
}