using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Monitoring.Models;
using PowerDaemon.Cache.Services;
using PowerDaemon.Shared.DTOs;

namespace PowerDaemon.Monitoring.Services;

public class MetricsAggregationService : IMetricsAggregationService
{
    private readonly ILogger<MetricsAggregationService> _logger;
    private readonly MonitoringConfiguration _config;
    private readonly ICacheService _cacheService;

    public MetricsAggregationService(
        ILogger<MetricsAggregationService> logger,
        IOptions<MonitoringConfiguration> config,
        ICacheService cacheService)
    {
        _logger = logger;
        _config = config.Value;
        _cacheService = cacheService;
    }

    public async Task<double?> EvaluateMetricAsync(string metric, ComparisonOperator operation, double threshold,
        TimeSpan evaluationWindow, AggregationType aggregation, Dictionary<string, string>? filters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endTime = DateTime.UtcNow;
            var startTime = endTime.Subtract(evaluationWindow);

            var values = await GetMetricValuesAsync(metric, startTime, endTime, filters, cancellationToken);
            
            if (!values.Any())
            {
                _logger.LogDebug("No values found for metric {Metric} in evaluation window", metric);
                return null;
            }

            var aggregatedValue = aggregation switch
            {
                AggregationType.Average => values.Average(),
                AggregationType.Sum => values.Sum(),
                AggregationType.Count => values.Count,
                AggregationType.Min => values.Min(),
                AggregationType.Max => values.Max(),
                AggregationType.P95 => CalculatePercentile(values, 95),
                AggregationType.P99 => CalculatePercentile(values, 99),
                _ => values.Average()
            };

            _logger.LogDebug("Evaluated metric {Metric}: {AggregatedValue} ({Aggregation}) from {ValueCount} values",
                metric, aggregatedValue, aggregation, values.Count);

            return aggregatedValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating metric {Metric}", metric);
            return null;
        }
    }

    public async Task<List<double>> GetMetricValuesAsync(string metric, DateTime from, DateTime to,
        Dictionary<string, string>? filters = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var values = new List<double>();

            // Build cache key pattern based on metric and filters
            var cachePattern = BuildCachePattern(metric, filters);
            
            // Get metric keys from cache
            var metricKeys = await _cacheService.GetKeysAsync($"metrics:{cachePattern}*");
            
            foreach (var key in metricKeys)
            {
                var metricData = await _cacheService.GetAsync<List<MetricDataDto>>(key);
                if (metricData != null)
                {
                    var filteredData = metricData
                        .Where(m => m.Timestamp >= from && m.Timestamp <= to)
                        .Where(m => MatchesFilters(m, filters));

                    values.AddRange(ExtractMetricValues(filteredData, metric));
                }
            }

            return values.OrderBy(v => v).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting metric values for {Metric}", metric);
            return new List<double>();
        }
    }

    public async Task<Dictionary<string, double>> GetMultipleMetricsAsync(List<string> metrics, DateTime from, DateTime to,
        AggregationType aggregation = AggregationType.Average, Dictionary<string, string>? filters = null,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, double>();

        var tasks = metrics.Select(async metric =>
        {
            var values = await GetMetricValuesAsync(metric, from, to, filters, cancellationToken);
            if (values.Any())
            {
                var aggregatedValue = aggregation switch
                {
                    AggregationType.Average => values.Average(),
                    AggregationType.Sum => values.Sum(),
                    AggregationType.Count => values.Count,
                    AggregationType.Min => values.Min(),
                    AggregationType.Max => values.Max(),
                    AggregationType.P95 => CalculatePercentile(values, 95),
                    AggregationType.P99 => CalculatePercentile(values, 99),
                    _ => values.Average()
                };
                return new { Metric = metric, Value = aggregatedValue };
            }
            return new { Metric = metric, Value = (double?)null };
        });

        var results = await Task.WhenAll(tasks);
        
        foreach (var res in results)
        {
            if (res.Value.HasValue)
            {
                result[res.Metric] = res.Value.Value;
            }
        }

        return result;
    }

    public async Task<bool> IsMetricAvailableAsync(string metric, CancellationToken cancellationToken = default)
    {
        try
        {
            var pattern = $"metrics:*:{metric}:*";
            var keys = await _cacheService.GetKeysAsync(pattern);
            return keys.Any();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking metric availability for {Metric}", metric);
            return false;
        }
    }

    public async Task<List<string>> GetAvailableMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var pattern = "metrics:*";
            var keys = await _cacheService.GetKeysAsync(pattern);
            
            var metrics = new HashSet<string>();
            
            foreach (var key in keys)
            {
                // Extract metric name from cache key pattern: metrics:server_id:metric_name:timestamp
                var parts = key.Split(':');
                if (parts.Length >= 3)
                {
                    metrics.Add(parts[2]);
                }
            }

            return metrics.OrderBy(m => m).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available metrics");
            return new List<string>();
        }
    }

    private static string BuildCachePattern(string metric, Dictionary<string, string>? filters)
    {
        if (filters == null || !filters.Any())
        {
            return $"*:{metric}";
        }

        // Build pattern based on filters
        var serverPattern = filters.TryGetValue("server_id", out var serverId) ? serverId : "*";
        return $"{serverPattern}:{metric}";
    }

    private static bool MatchesFilters(MetricDataDto metricData, Dictionary<string, string>? filters)
    {
        if (filters == null || !filters.Any())
        {
            return true;
        }

        foreach (var filter in filters)
        {
            switch (filter.Key.ToLower())
            {
                case "server_id":
                    if (metricData.ServerId.ToString() != filter.Value)
                        return false;
                    break;
                case "service_id":
                    if (metricData.ServiceId?.ToString() != filter.Value)
                        return false;
                    break;
                case "metric_type":
                    if (metricData.MetricType != filter.Value)
                        return false;
                    break;
            }
        }

        return true;
    }

    private static List<double> ExtractMetricValues(IEnumerable<MetricDataDto> metricData, string metricName)
    {
        var values = new List<double>();

        foreach (var data in metricData)
        {
            switch (metricName.ToLower())
            {
                case "cpu_usage_percent":
                    values.Add(data.CpuUsagePercent);
                    break;
                case "memory_usage_percent":
                    values.Add(data.MemoryUsagePercent);
                    break;
                case "disk_usage_percent":
                    values.Add(data.DiskUsagePercent);
                    break;
                case "network_usage_mbps":
                    values.Add((data.NetworkBytesReceived + data.NetworkBytesSent) / 1024.0 / 1024.0 * 8); // Convert to Mbps
                    break;
                case "disk_read_mbps":
                    values.Add(data.DiskBytesRead / 1024.0 / 1024.0);
                    break;
                case "disk_write_mbps":
                    values.Add(data.DiskBytesWritten / 1024.0 / 1024.0);
                    break;
                case "memory_usage_mb":
                    values.Add(data.MemoryUsageBytes / 1024.0 / 1024.0);
                    break;
                case "service_count":
                    values.Add(data.ServiceCount);
                    break;
                case "thread_count":
                    values.Add(data.ThreadCount);
                    break;
                case "handle_count":
                    values.Add(data.HandleCount);
                    break;
                default:
                    // For custom metrics, try to extract from additional properties if available
                    if (data.AdditionalProperties?.TryGetValue(metricName, out var value) == true)
                    {
                        if (double.TryParse(value?.ToString(), out var doubleValue))
                        {
                            values.Add(doubleValue);
                        }
                    }
                    break;
            }
        }

        return values;
    }

    private static double CalculatePercentile(List<double> values, int percentile)
    {
        if (!values.Any()) return 0;
        
        var sortedValues = values.OrderBy(v => v).ToList();
        var index = (percentile / 100.0) * (sortedValues.Count - 1);
        
        if (index == (int)index)
        {
            return sortedValues[(int)index];
        }
        
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        var fraction = index - lower;
        
        return sortedValues[lower] + (fraction * (sortedValues[upper] - sortedValues[lower]));
    }

    // Additional helper methods for custom metrics
    public async Task StoreCustomMetricAsync(string metricName, double value, Dictionary<string, string>? tags = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var timestamp = DateTime.UtcNow;
            var serverId = tags?.GetValueOrDefault("server_id", "unknown") ?? "unknown";
            
            var cacheKey = $"metrics:{serverId}:{metricName}:{timestamp:yyyyMMddHHmm}";
            
            var metricData = new
            {
                metric_name = metricName,
                value = value,
                timestamp = timestamp,
                tags = tags ?? new Dictionary<string, string>()
            };

            await _cacheService.SetAsync(cacheKey, metricData, TimeSpan.FromDays(_config.Metrics.RetentionDays));
            
            _logger.LogDebug("Stored custom metric {MetricName} = {Value} for server {ServerId}", 
                metricName, value, serverId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing custom metric {MetricName}", metricName);
        }
    }

    public async Task<List<string>> GetMetricTagsAsync(string metricName, CancellationToken cancellationToken = default)
    {
        try
        {
            var pattern = $"metrics:*:{metricName}:*";
            var keys = await _cacheService.GetKeysAsync(pattern);
            
            var tags = new HashSet<string>();
            
            foreach (var key in keys)
            {
                var metricData = await _cacheService.GetAsync<dynamic>(key);
                if (metricData?.tags != null)
                {
                    var metricTags = metricData.tags as Dictionary<string, string>;
                    if (metricTags != null)
                    {
                        foreach (var tag in metricTags.Keys)
                        {
                            tags.Add(tag);
                        }
                    }
                }
            }

            return tags.OrderBy(t => t).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting metric tags for {MetricName}", metricName);
            return new List<string>();
        }
    }

    public async Task<Dictionary<string, List<string>>> GetMetricTagValuesAsync(string metricName, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pattern = $"metrics:*:{metricName}:*";
            var keys = await _cacheService.GetKeysAsync(pattern);
            
            var tagValues = new Dictionary<string, HashSet<string>>();
            
            foreach (var key in keys)
            {
                var metricData = await _cacheService.GetAsync<dynamic>(key);
                if (metricData?.tags != null)
                {
                    var metricTags = metricData.tags as Dictionary<string, string>;
                    if (metricTags != null)
                    {
                        foreach (var kvp in metricTags)
                        {
                            if (!tagValues.ContainsKey(kvp.Key))
                            {
                                tagValues[kvp.Key] = new HashSet<string>();
                            }
                            tagValues[kvp.Key].Add(kvp.Value);
                        }
                    }
                }
            }

            return tagValues.ToDictionary(
                kvp => kvp.Key, 
                kvp => kvp.Value.OrderBy(v => v).ToList()
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting metric tag values for {MetricName}", metricName);
            return new Dictionary<string, List<string>>();
        }
    }
}