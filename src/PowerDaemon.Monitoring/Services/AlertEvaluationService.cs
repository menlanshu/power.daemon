using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Monitoring.Models;
using PowerDaemon.Cache.Services;

namespace PowerDaemon.Monitoring.Services;

public class AlertEvaluationService : BackgroundService
{
    private readonly ILogger<AlertEvaluationService> _logger;
    private readonly MonitoringConfiguration _config;
    private readonly IAlertService _alertService;
    private readonly IAlertRuleService _alertRuleService;
    private readonly IMetricsAggregationService _metricsService;
    private readonly ICacheService _cacheService;
    private readonly Timer _evaluationTimer;
    private readonly SemaphoreSlim _evaluationSemaphore = new(1, 1);
    
    public AlertEvaluationService(
        ILogger<AlertEvaluationService> logger,
        IOptions<MonitoringConfiguration> config,
        IAlertService alertService,
        IAlertRuleService alertRuleService,
        IMetricsAggregationService metricsService,
        ICacheService cacheService)
    {
        _logger = logger;
        _config = config.Value;
        _alertService = alertService;
        _alertRuleService = alertRuleService;
        _metricsService = metricsService;
        _cacheService = cacheService;
        
        _evaluationTimer = new Timer(
            async _ => await EvaluateAlertsAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(_config.Alerting.EvaluationIntervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Alert evaluation service started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAlertsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_config.Alerting.EvaluationIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during alert evaluation cycle");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        
        _logger.LogInformation("Alert evaluation service stopped");
    }

    private async Task EvaluateAlertsAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.Alerting.Enabled)
        {
            return;
        }

        if (!await _evaluationSemaphore.WaitAsync(1000, cancellationToken))
        {
            _logger.LogWarning("Skipping alert evaluation - previous evaluation still running");
            return;
        }

        try
        {
            var startTime = DateTime.UtcNow;
            _logger.LogDebug("Starting alert evaluation cycle");

            var rules = await _alertRuleService.GetAllRulesAsync(false, cancellationToken);
            var evaluationTasks = rules.Select(rule => EvaluateRuleAsync(rule, cancellationToken));
            
            var results = await Task.WhenAll(evaluationTasks);
            var triggeredCount = results.Count(r => r);

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Completed alert evaluation cycle: {RuleCount} rules evaluated, {TriggeredCount} alerts triggered, duration: {Duration}ms",
                rules.Count, triggeredCount, duration.TotalMilliseconds);

            // Update evaluation metrics in cache
            await UpdateEvaluationMetricsAsync(rules.Count, triggeredCount, duration, cancellationToken);
        }
        finally
        {
            _evaluationSemaphore.Release();
        }
    }

    private async Task<bool> EvaluateRuleAsync(AlertRule rule, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!rule.Enabled)
            {
                return false;
            }

            // Check if rule should be evaluated based on interval
            var lastEvaluationKey = $"alert_rule_last_eval:{rule.Id}";
            var lastEvaluationTime = await _cacheService.GetAsync<DateTime?>(lastEvaluationKey);
            
            if (lastEvaluationTime.HasValue && 
                DateTime.UtcNow - lastEvaluationTime.Value < rule.EvaluationInterval)
            {
                return false;
            }

            // Evaluate the rule condition
            var metricValue = await _metricsService.EvaluateMetricAsync(
                rule.Condition.Metric,
                rule.Condition.Operator,
                rule.Condition.Threshold,
                rule.EvaluationWindow,
                rule.Condition.Aggregation,
                rule.Condition.Filters,
                cancellationToken);

            // Update last evaluation time
            await _cacheService.SetAsync(lastEvaluationKey, DateTime.UtcNow, TimeSpan.FromHours(1));

            if (!metricValue.HasValue)
            {
                _logger.LogDebug("No data available for metric {Metric} in rule {RuleName}", 
                    rule.Condition.Metric, rule.Name);
                return false;
            }

            // Check if condition is met
            var conditionMet = EvaluateCondition(rule.Condition, metricValue.Value);
            
            if (conditionMet)
            {
                // Check if we already have an active alert for this rule
                var existingAlert = await GetExistingAlertAsync(rule, cancellationToken);
                
                if (existingAlert == null)
                {
                    // Create new alert
                    await CreateAlertFromRuleAsync(rule, metricValue.Value, cancellationToken);
                    return true;
                }
                else
                {
                    // Update existing alert with new data point
                    await UpdateExistingAlertAsync(existingAlert, metricValue.Value, cancellationToken);
                    return false;
                }
            }
            else
            {
                // Check if we should resolve an existing alert
                var existingAlert = await GetExistingAlertAsync(rule, cancellationToken);
                if (existingAlert != null && existingAlert.Status == AlertStatus.Active)
                {
                    await _alertService.ResolveAlertAsync(existingAlert.Id, "System", "Condition no longer met", cancellationToken);
                    _logger.LogInformation("Auto-resolved alert {AlertId} for rule {RuleName}", existingAlert.Id, rule.Name);
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating alert rule {RuleId}: {RuleName}", rule.Id, rule.Name);
            return false;
        }
    }

    private static bool EvaluateCondition(AlertCondition condition, double actualValue)
    {
        return condition.Operator switch
        {
            ComparisonOperator.GreaterThan => actualValue > condition.Threshold,
            ComparisonOperator.GreaterThanOrEqual => actualValue >= condition.Threshold,
            ComparisonOperator.LessThan => actualValue < condition.Threshold,
            ComparisonOperator.LessThanOrEqual => actualValue <= condition.Threshold,
            ComparisonOperator.Equal => Math.Abs(actualValue - condition.Threshold) < 0.001,
            ComparisonOperator.NotEqual => Math.Abs(actualValue - condition.Threshold) >= 0.001,
            _ => false
        };
    }

    private async Task<Alert?> GetExistingAlertAsync(AlertRule rule, CancellationToken cancellationToken = default)
    {
        // Generate fingerprint for the rule to find existing alerts
        var fingerprint = GenerateRuleFingerprint(rule);
        
        // Check cache first
        var cacheKey = $"active_alert:{fingerprint}";
        var alertId = await _cacheService.GetAsync<string>(cacheKey);
        
        if (!string.IsNullOrEmpty(alertId))
        {
            var alert = await _alertService.GetAlertAsync(alertId, cancellationToken);
            if (alert != null && alert.Status == AlertStatus.Active)
            {
                return alert;
            }
        }

        // If not in cache or alert not found, search for active alerts
        var activeAlerts = await _alertService.GetActiveAlertsAsync(cancellationToken);
        var existingAlert = activeAlerts.FirstOrDefault(a => a.SourceRule == rule.Id);

        if (existingAlert != null)
        {
            // Cache the alert for future lookups
            await _cacheService.SetAsync(cacheKey, existingAlert.Id, TimeSpan.FromMinutes(5));
        }

        return existingAlert;
    }

    private async Task CreateAlertFromRuleAsync(AlertRule rule, double actualValue, CancellationToken cancellationToken = default)
    {
        var request = new CreateAlertRequest
        {
            Title = $"{rule.Name}",
            Message = $"Metric {rule.Condition.Metric} is {actualValue:F2}, threshold is {rule.Condition.Threshold:F2}",
            Severity = rule.Severity,
            Category = rule.Category,
            SourceRule = rule.Id,
            ThresholdValue = rule.Condition.Threshold,
            ActualValue = actualValue,
            Tags = rule.Tags,
            DataPoints = new List<AlertDataPoint>
            {
                new() { Timestamp = DateTime.UtcNow, Value = actualValue }
            },
            Metadata = new Dictionary<string, object>
            {
                ["rule_id"] = rule.Id,
                ["rule_name"] = rule.Name,
                ["metric"] = rule.Condition.Metric,
                ["operator"] = rule.Condition.Operator.ToString(),
                ["aggregation"] = rule.Condition.Aggregation.ToString(),
                ["evaluation_window"] = rule.EvaluationWindow.ToString(),
                ["filters"] = rule.Condition.Filters
            }
        };

        // Extract server/service info from filters if available
        if (rule.Condition.Filters.TryGetValue("server_id", out var serverId))
        {
            request.ServerId = serverId;
        }
        if (rule.Condition.Filters.TryGetValue("service_id", out var serviceId))
        {
            request.ServiceId = serviceId;
        }

        var alert = await _alertService.CreateAlertAsync(request, cancellationToken);
        
        _logger.LogWarning(
            "Alert triggered: {AlertId} for rule {RuleName}, {Metric} = {ActualValue} (threshold: {Threshold})",
            alert.Id, rule.Name, rule.Condition.Metric, actualValue, rule.Condition.Threshold);

        // Cache the alert for quick lookup
        var fingerprint = GenerateRuleFingerprint(rule);
        await _cacheService.SetAsync($"active_alert:{fingerprint}", alert.Id, TimeSpan.FromMinutes(5));
    }

    private async Task UpdateExistingAlertAsync(Alert existingAlert, double actualValue, CancellationToken cancellationToken = default)
    {
        // Add new data point
        existingAlert.DataPoints.Add(new AlertDataPoint 
        { 
            Timestamp = DateTime.UtcNow, 
            Value = actualValue 
        });

        // Limit data points to prevent unbounded growth
        if (existingAlert.DataPoints.Count > 100)
        {
            existingAlert.DataPoints = existingAlert.DataPoints
                .OrderByDescending(dp => dp.Timestamp)
                .Take(100)
                .ToList();
        }

        existingAlert.ActualValue = actualValue;
        existingAlert.UpdatedAt = DateTime.UtcNow;

        // Update alert (this would typically involve persisting the changes)
        _logger.LogDebug("Updated existing alert {AlertId} with new value {ActualValue}", 
            existingAlert.Id, actualValue);
    }

    private static string GenerateRuleFingerprint(AlertRule rule)
    {
        var components = new[]
        {
            rule.Id,
            rule.Condition.Metric,
            string.Join(",", rule.Condition.Filters.Select(f => $"{f.Key}={f.Value}"))
        };

        var content = string.Join("|", components);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash)[..16];
    }

    private async Task UpdateEvaluationMetricsAsync(int rulesEvaluated, int alertsTriggered, 
        TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var metrics = new
        {
            RulesEvaluated = rulesEvaluated,
            AlertsTriggered = alertsTriggered,
            EvaluationDurationMs = duration.TotalMilliseconds,
            Timestamp = DateTime.UtcNow
        };

        await _cacheService.SetAsync("alert_evaluation_metrics", metrics, TimeSpan.FromMinutes(5));
        
        // Also store in a time series for trending
        var timeSeriesKey = $"alert_evaluation_history:{DateTime.UtcNow:yyyyMMddHH}";
        await _cacheService.ListPushAsync(timeSeriesKey, metrics);
        await _cacheService.ExpireAsync(timeSeriesKey, TimeSpan.FromDays(7));
    }

    public async Task<Dictionary<string, object>> GetEvaluationMetricsAsync(CancellationToken cancellationToken = default)
    {
        var currentMetrics = await _cacheService.GetAsync<object>("alert_evaluation_metrics");
        var result = new Dictionary<string, object>();

        if (currentMetrics != null)
        {
            result["current"] = currentMetrics;
        }

        // Get historical data for trending
        var history = new List<object>();
        for (var i = 0; i < 24; i++) // Last 24 hours
        {
            var hour = DateTime.UtcNow.AddHours(-i);
            var timeSeriesKey = $"alert_evaluation_history:{hour:yyyyMMddHH}";
            var hourlyData = await _cacheService.ListGetAllAsync<object>(timeSeriesKey);
            history.AddRange(hourlyData);
        }

        result["history"] = history.OrderBy(h => h).Take(1000).ToList(); // Limit to 1000 points

        return result;
    }

    public override void Dispose()
    {
        _evaluationTimer?.Dispose();
        _evaluationSemaphore?.Dispose();
        base.Dispose();
    }
}