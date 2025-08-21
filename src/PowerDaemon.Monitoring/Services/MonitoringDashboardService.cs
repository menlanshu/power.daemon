using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Monitoring.Models;
using PowerDaemon.Monitoring.Services;
using PowerDaemon.Cache.Services;

namespace PowerDaemon.Monitoring.Services;

public class MonitoringDashboardService : IMonitoringDashboardService
{
    private readonly ILogger<MonitoringDashboardService> _logger;
    private readonly MonitoringConfiguration _config;
    private readonly ICacheService _cacheService;
    private readonly IMetricsAggregationService _metricsService;
    private readonly IAlertService _alertService;
    private readonly Dictionary<string, Dashboard> _dashboards = new();
    private readonly object _dashboardsLock = new();

    public MonitoringDashboardService(
        ILogger<MonitoringDashboardService> logger,
        IOptions<MonitoringConfiguration> config,
        ICacheService cacheService,
        IMetricsAggregationService metricsService,
        IAlertService alertService)
    {
        _logger = logger;
        _config = config.Value;
        _cacheService = cacheService;
        _metricsService = metricsService;
        _alertService = alertService;

        // Initialize with built-in dashboards
        _ = Task.Run(InitializeBuiltInDashboardsAsync);
    }

    public async Task<Dashboard> CreateDashboardAsync(CreateDashboardRequest request, CancellationToken cancellationToken = default)
    {
        var dashboard = new Dashboard
        {
            Name = request.Name,
            Description = request.Description,
            OwnerId = request.OwnerId,
            Widgets = request.Widgets,
            Layout = request.Layout,
            IsPublic = request.IsPublic,
            SharedWith = request.SharedWith,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        lock (_dashboardsLock)
        {
            _dashboards[dashboard.Id] = dashboard;
        }

        await _cacheService.SetAsync($"dashboard:{dashboard.Id}", dashboard, TimeSpan.FromDays(30));
        await _cacheService.SetAddAsync("dashboards", dashboard.Id);

        _logger.LogInformation("Dashboard created: {DashboardId} - {DashboardName} by {OwnerId}",
            dashboard.Id, dashboard.Name, dashboard.OwnerId);

        return dashboard;
    }

    public async Task<Dashboard> UpdateDashboardAsync(string dashboardId, UpdateDashboardRequest request, CancellationToken cancellationToken = default)
    {
        var dashboard = await GetDashboardAsync(dashboardId, cancellationToken);
        if (dashboard == null)
        {
            throw new ArgumentException($"Dashboard {dashboardId} not found");
        }

        dashboard.Name = request.Name;
        dashboard.Description = request.Description;
        dashboard.Widgets = request.Widgets;
        dashboard.Layout = request.Layout;
        dashboard.IsPublic = request.IsPublic;
        dashboard.SharedWith = request.SharedWith;
        dashboard.UpdatedAt = DateTime.UtcNow;

        lock (_dashboardsLock)
        {
            _dashboards[dashboard.Id] = dashboard;
        }

        await _cacheService.SetAsync($"dashboard:{dashboard.Id}", dashboard, TimeSpan.FromDays(30));

        _logger.LogInformation("Dashboard updated: {DashboardId} - {DashboardName}",
            dashboard.Id, dashboard.Name);

        return dashboard;
    }

    public async Task<bool> DeleteDashboardAsync(string dashboardId, CancellationToken cancellationToken = default)
    {
        var dashboard = await GetDashboardAsync(dashboardId, cancellationToken);
        if (dashboard == null) return false;

        lock (_dashboardsLock)
        {
            _dashboards.Remove(dashboardId);
        }

        await _cacheService.DeleteAsync($"dashboard:{dashboardId}");
        await _cacheService.SetRemoveAsync("dashboards", dashboardId);

        _logger.LogInformation("Dashboard deleted: {DashboardId} - {DashboardName}",
            dashboard.Id, dashboard.Name);
        return true;
    }

    public async Task<Dashboard?> GetDashboardAsync(string dashboardId, CancellationToken cancellationToken = default)
    {
        // Try cache first
        var cachedDashboard = await _cacheService.GetAsync<Dashboard>($"dashboard:{dashboardId}");
        if (cachedDashboard != null)
        {
            return cachedDashboard;
        }

        // Try in-memory store
        lock (_dashboardsLock)
        {
            if (_dashboards.TryGetValue(dashboardId, out var dashboard))
            {
                return dashboard;
            }
        }

        return null;
    }

    public async Task<List<Dashboard>> GetAllDashboardsAsync(CancellationToken cancellationToken = default)
    {
        var dashboardIds = await _cacheService.SetMembersAsync("dashboards");
        var dashboards = new List<Dashboard>();

        foreach (var dashboardId in dashboardIds)
        {
            var dashboard = await GetDashboardAsync(dashboardId, cancellationToken);
            if (dashboard != null)
            {
                dashboards.Add(dashboard);
            }
        }

        return dashboards.OrderBy(d => d.Name).ToList();
    }

    public async Task<List<Dashboard>> GetUserDashboardsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var allDashboards = await GetAllDashboardsAsync(cancellationToken);
        return allDashboards
            .Where(d => d.OwnerId == userId || d.IsPublic || d.SharedWith.Contains(userId))
            .ToList();
    }

    public async Task<DashboardData> GetDashboardDataAsync(string dashboardId, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        var dashboard = await GetDashboardAsync(dashboardId, cancellationToken);
        if (dashboard == null)
        {
            throw new ArgumentException($"Dashboard {dashboardId} not found");
        }

        from ??= DateTime.UtcNow.AddHours(-1);
        to ??= DateTime.UtcNow;

        var dashboardData = new DashboardData
        {
            DashboardId = dashboardId
        };

        // Get data for each widget
        var widgetTasks = dashboard.Widgets.Select(async widget =>
        {
            var widgetData = await GetWidgetDataAsync(widget.Id, from, to, cancellationToken);
            return new { WidgetId = widget.Id, Data = widgetData };
        });

        var results = await Task.WhenAll(widgetTasks);
        
        foreach (var result in results)
        {
            dashboardData.WidgetData[result.WidgetId] = result.Data;
        }

        return dashboardData;
    }

    public async Task<WidgetData> GetWidgetDataAsync(string widgetId, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        from ??= DateTime.UtcNow.AddHours(-1);
        to ??= DateTime.UtcNow;

        // Find the widget across all dashboards
        var allDashboards = await GetAllDashboardsAsync(cancellationToken);
        var widget = allDashboards.SelectMany(d => d.Widgets).FirstOrDefault(w => w.Id == widgetId);
        
        if (widget == null)
        {
            throw new ArgumentException($"Widget {widgetId} not found");
        }

        var widgetData = new WidgetData
        {
            WidgetId = widgetId
        };

        try
        {
            switch (widget.Type)
            {
                case WidgetType.LineChart:
                case WidgetType.BarChart:
                    widgetData.Series = await GetTimeSeriesData(widget, from.Value, to.Value, cancellationToken);
                    break;

                case WidgetType.Gauge:
                    widgetData.Series = await GetGaugeData(widget, from.Value, to.Value, cancellationToken);
                    break;

                case WidgetType.Counter:
                    widgetData.Series = await GetCounterData(widget, from.Value, to.Value, cancellationToken);
                    break;

                case WidgetType.Status:
                    widgetData.Series = await GetStatusData(widget, cancellationToken);
                    break;

                case WidgetType.Table:
                    widgetData.Series = await GetTableData(widget, from.Value, to.Value, cancellationToken);
                    break;

                default:
                    _logger.LogWarning("Unsupported widget type: {WidgetType}", widget.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting data for widget {WidgetId}", widgetId);
            widgetData.Metadata["error"] = ex.Message;
        }

        return widgetData;
    }

    private async Task<List<DataSeries>> GetTimeSeriesData(DashboardWidget widget, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var series = new List<DataSeries>();
        
        if (!widget.Configuration.TryGetValue("metrics", out var metricsObj) || 
            metricsObj is not List<string> metrics)
        {
            return series;
        }

        foreach (var metric in metrics)
        {
            var values = await _metricsService.GetMetricValuesAsync(metric, from, to, null, cancellationToken);
            
            var dataSeries = new DataSeries
            {
                Name = metric,
                Points = new List<DataPoint>()
            };

            // Group values by time intervals for better visualization
            var interval = CalculateInterval(from, to, values.Count);
            var groupedValues = GroupValuesByInterval(values, from, to, interval);

            foreach (var group in groupedValues)
            {
                dataSeries.Points.Add(new DataPoint
                {
                    Timestamp = group.Key,
                    Value = group.Value
                });
            }

            series.Add(dataSeries);
        }

        return series;
    }

    private async Task<List<DataSeries>> GetGaugeData(DashboardWidget widget, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var series = new List<DataSeries>();
        
        if (!widget.Configuration.TryGetValue("metric", out var metricObj) || 
            metricObj is not string metric)
        {
            return series;
        }

        var values = await _metricsService.GetMetricValuesAsync(metric, from, to, null, cancellationToken);
        
        if (values.Any())
        {
            var latestValue = values.Last(); // Use the most recent value for gauge
            
            series.Add(new DataSeries
            {
                Name = metric,
                Points = new List<DataPoint>
                {
                    new DataPoint
                    {
                        Timestamp = DateTime.UtcNow,
                        Value = latestValue
                    }
                }
            });
        }

        return series;
    }

    private async Task<List<DataSeries>> GetCounterData(DashboardWidget widget, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var series = new List<DataSeries>();
        
        if (!widget.Configuration.TryGetValue("metric", out var metricObj) || 
            metricObj is not string metric)
        {
            return series;
        }

        var values = await _metricsService.GetMetricValuesAsync(metric, from, to, null, cancellationToken);
        
        if (values.Any())
        {
            var sum = values.Sum();
            
            series.Add(new DataSeries
            {
                Name = metric,
                Points = new List<DataPoint>
                {
                    new DataPoint
                    {
                        Timestamp = DateTime.UtcNow,
                        Value = sum
                    }
                }
            });
        }

        return series;
    }

    private async Task<List<DataSeries>> GetStatusData(DashboardWidget widget, CancellationToken cancellationToken)
    {
        var series = new List<DataSeries>();
        
        // Get alert statistics for status display
        var alertStats = await _alertService.GetStatisticsAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, cancellationToken);
        
        series.Add(new DataSeries
        {
            Name = "Alerts",
            Points = new List<DataPoint>
            {
                new DataPoint { Timestamp = DateTime.UtcNow, Value = alertStats.ActiveAlerts, Metadata = { ["label"] = "Active" } },
                new DataPoint { Timestamp = DateTime.UtcNow, Value = alertStats.CriticalAlerts, Metadata = { ["label"] = "Critical" } },
                new DataPoint { Timestamp = DateTime.UtcNow, Value = alertStats.WarningAlerts, Metadata = { ["label"] = "Warning" } }
            }
        });

        return series;
    }

    private async Task<List<DataSeries>> GetTableData(DashboardWidget widget, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var series = new List<DataSeries>();
        
        // For table data, we could show server status, top alerts, etc.
        // This is a simplified implementation
        var alertStats = await _alertService.GetStatisticsAsync(from, to, cancellationToken);
        
        var serverSeries = new DataSeries
        {
            Name = "Servers",
            Points = new List<DataPoint>()
        };

        foreach (var serverAlert in alertStats.AlertsByServer.Take(10))
        {
            serverSeries.Points.Add(new DataPoint
            {
                Timestamp = DateTime.UtcNow,
                Value = serverAlert.Value,
                Metadata = { ["server_name"] = serverAlert.Key }
            });
        }

        series.Add(serverSeries);
        return series;
    }

    private static TimeSpan CalculateInterval(DateTime from, DateTime to, int dataPointCount)
    {
        var totalDuration = to - from;
        var targetPoints = Math.Min(Math.Max(dataPointCount, 10), 100); // Between 10 and 100 points
        return TimeSpan.FromMilliseconds(totalDuration.TotalMilliseconds / targetPoints);
    }

    private static Dictionary<DateTime, double> GroupValuesByInterval(List<double> values, DateTime from, DateTime to, TimeSpan interval)
    {
        var grouped = new Dictionary<DateTime, double>();
        var current = from;
        var valueIndex = 0;

        while (current < to && valueIndex < values.Count)
        {
            var endInterval = current.Add(interval);
            var intervalValues = new List<double>();

            while (valueIndex < values.Count && current < endInterval)
            {
                intervalValues.Add(values[valueIndex]);
                valueIndex++;
                current = current.AddSeconds(1); // Assuming 1-second resolution
            }

            if (intervalValues.Any())
            {
                grouped[current] = intervalValues.Average();
            }

            current = endInterval;
        }

        return grouped;
    }

    private async Task InitializeBuiltInDashboardsAsync()
    {
        try
        {
            var builtInDashboards = GetBuiltInDashboards();
            
            foreach (var dashboard in builtInDashboards)
            {
                // Check if dashboard already exists
                var existingDashboard = await GetDashboardAsync(dashboard.Id, CancellationToken.None);
                if (existingDashboard == null)
                {
                    lock (_dashboardsLock)
                    {
                        _dashboards[dashboard.Id] = dashboard;
                    }

                    await _cacheService.SetAsync($"dashboard:{dashboard.Id}", dashboard, TimeSpan.FromDays(30));
                    await _cacheService.SetAddAsync("dashboards", dashboard.Id);

                    _logger.LogInformation("Initialized built-in dashboard: {DashboardName}", dashboard.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize built-in dashboards");
        }
    }

    private List<Dashboard> GetBuiltInDashboards()
    {
        return new List<Dashboard>
        {
            new Dashboard
            {
                Id = "builtin-system-overview",
                Name = "System Overview",
                Description = "System-wide metrics and status overview",
                OwnerId = "system",
                IsPublic = true,
                Layout = new DashboardLayout { Columns = 12, Rows = 8 },
                Widgets = new List<DashboardWidget>
                {
                    new DashboardWidget
                    {
                        Id = "cpu-usage-chart",
                        Name = "CPU Usage",
                        Type = WidgetType.LineChart,
                        Position = new WidgetPosition { X = 0, Y = 0, Width = 6, Height = 3 },
                        Configuration = new Dictionary<string, object>
                        {
                            ["metrics"] = new List<string> { "cpu_usage_percent" },
                            ["title"] = "CPU Usage (%)"
                        }
                    },
                    new DashboardWidget
                    {
                        Id = "memory-usage-chart",
                        Name = "Memory Usage",
                        Type = WidgetType.LineChart,
                        Position = new WidgetPosition { X = 6, Y = 0, Width = 6, Height = 3 },
                        Configuration = new Dictionary<string, object>
                        {
                            ["metrics"] = new List<string> { "memory_usage_percent" },
                            ["title"] = "Memory Usage (%)"
                        }
                    },
                    new DashboardWidget
                    {
                        Id = "alert-status",
                        Name = "Alert Status",
                        Type = WidgetType.Status,
                        Position = new WidgetPosition { X = 0, Y = 3, Width = 4, Height = 2 },
                        Configuration = new Dictionary<string, object>
                        {
                            ["title"] = "Current Alerts"
                        }
                    },
                    new DashboardWidget
                    {
                        Id = "disk-usage-gauge",
                        Name = "Disk Usage",
                        Type = WidgetType.Gauge,
                        Position = new WidgetPosition { X = 4, Y = 3, Width = 4, Height = 2 },
                        Configuration = new Dictionary<string, object>
                        {
                            ["metric"] = "disk_usage_percent",
                            ["title"] = "Disk Usage",
                            ["min"] = 0,
                            ["max"] = 100
                        }
                    },
                    new DashboardWidget
                    {
                        Id = "network-usage-gauge",
                        Name = "Network Usage",
                        Type = WidgetType.Gauge,
                        Position = new WidgetPosition { X = 8, Y = 3, Width = 4, Height = 2 },
                        Configuration = new Dictionary<string, object>
                        {
                            ["metric"] = "network_usage_mbps",
                            ["title"] = "Network Usage (Mbps)",
                            ["min"] = 0,
                            ["max"] = 1000
                        }
                    }
                }
            },

            new Dashboard
            {
                Id = "builtin-alerts-dashboard",
                Name = "Alerts Dashboard",
                Description = "Comprehensive alert monitoring and management",
                OwnerId = "system",
                IsPublic = true,
                Layout = new DashboardLayout { Columns = 12, Rows = 8 },
                Widgets = new List<DashboardWidget>
                {
                    new DashboardWidget
                    {
                        Id = "active-alerts-counter",
                        Name = "Active Alerts",
                        Type = WidgetType.Counter,
                        Position = new WidgetPosition { X = 0, Y = 0, Width = 3, Height = 2 },
                        Configuration = new Dictionary<string, object>
                        {
                            ["metric"] = "active_alerts_count",
                            ["title"] = "Active Alerts"
                        }
                    },
                    new DashboardWidget
                    {
                        Id = "critical-alerts-counter",
                        Name = "Critical Alerts",
                        Type = WidgetType.Counter,
                        Position = new WidgetPosition { X = 3, Y = 0, Width = 3, Height = 2 },
                        Configuration = new Dictionary<string, object>
                        {
                            ["metric"] = "critical_alerts_count",
                            ["title"] = "Critical Alerts"
                        }
                    },
                    new DashboardWidget
                    {
                        Id = "alerts-by-server",
                        Name = "Alerts by Server",
                        Type = WidgetType.Table,
                        Position = new WidgetPosition { X = 0, Y = 2, Width = 12, Height = 4 },
                        Configuration = new Dictionary<string, object>
                        {
                            ["title"] = "Top Servers by Alert Count"
                        }
                    }
                }
            }
        };
    }
}