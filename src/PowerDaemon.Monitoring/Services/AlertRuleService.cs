using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Monitoring.Models;
using PowerDaemon.Cache.Services;

namespace PowerDaemon.Monitoring.Services;

public class AlertRuleService : IAlertRuleService
{
    private readonly ILogger<AlertRuleService> _logger;
    private readonly MonitoringConfiguration _config;
    private readonly ICacheService _cacheService;
    private readonly Dictionary<string, AlertRule> _rules = new();
    private readonly object _rulesLock = new();

    public AlertRuleService(
        ILogger<AlertRuleService> logger,
        IOptions<MonitoringConfiguration> config,
        ICacheService cacheService)
    {
        _logger = logger;
        _config = config.Value;
        _cacheService = cacheService;

        // Initialize with built-in rules
        _ = Task.Run(InitializeBuiltInRulesAsync);
    }

    public async Task<AlertRule> CreateRuleAsync(CreateAlertRuleRequest request, CancellationToken cancellationToken = default)
    {
        var rule = new AlertRule
        {
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            Severity = request.Severity,
            Condition = request.Condition,
            EvaluationInterval = request.EvaluationInterval,
            EvaluationWindow = request.EvaluationWindow,
            MinimumDataPoints = request.MinimumDataPoints,
            Tags = request.Tags,
            NotificationChannels = request.NotificationChannels,
            SuppressionRules = request.SuppressionRules,
            CreatedBy = request.CreatedBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        lock (_rulesLock)
        {
            _rules[rule.Id] = rule;
        }

        await _cacheService.SetAsync($"alert_rule:{rule.Id}", rule, TimeSpan.FromDays(30));
        await _cacheService.SetAddAsync("alert_rules", rule.Id);

        _logger.LogInformation("Alert rule created: {RuleId} - {RuleName} by {CreatedBy}", 
            rule.Id, rule.Name, rule.CreatedBy);

        return rule;
    }

    public async Task<AlertRule> UpdateRuleAsync(string ruleId, UpdateAlertRuleRequest request, CancellationToken cancellationToken = default)
    {
        var rule = await GetRuleAsync(ruleId, cancellationToken);
        if (rule == null)
        {
            throw new ArgumentException($"Alert rule {ruleId} not found");
        }

        rule.Name = request.Name;
        rule.Description = request.Description;
        rule.Category = request.Category;
        rule.Severity = request.Severity;
        rule.Condition = request.Condition;
        rule.EvaluationInterval = request.EvaluationInterval;
        rule.EvaluationWindow = request.EvaluationWindow;
        rule.MinimumDataPoints = request.MinimumDataPoints;
        rule.Tags = request.Tags;
        rule.NotificationChannels = request.NotificationChannels;
        rule.SuppressionRules = request.SuppressionRules;
        rule.UpdatedBy = request.UpdatedBy;
        rule.UpdatedAt = DateTime.UtcNow;
        rule.Version++;

        lock (_rulesLock)
        {
            _rules[rule.Id] = rule;
        }

        await _cacheService.SetAsync($"alert_rule:{rule.Id}", rule, TimeSpan.FromDays(30));

        _logger.LogInformation("Alert rule updated: {RuleId} - {RuleName} by {UpdatedBy}", 
            rule.Id, rule.Name, rule.UpdatedBy);

        return rule;
    }

    public async Task<bool> DeleteRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await GetRuleAsync(ruleId, cancellationToken);
        if (rule == null) return false;

        lock (_rulesLock)
        {
            _rules.Remove(ruleId);
        }

        await _cacheService.DeleteAsync($"alert_rule:{ruleId}");
        await _cacheService.SetRemoveAsync("alert_rules", ruleId);

        _logger.LogInformation("Alert rule deleted: {RuleId} - {RuleName}", rule.Id, rule.Name);
        return true;
    }

    public async Task<AlertRule?> GetRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        // Try cache first
        var cachedRule = await _cacheService.GetAsync<AlertRule>($"alert_rule:{ruleId}");
        if (cachedRule != null)
        {
            return cachedRule;
        }

        // Try in-memory store
        lock (_rulesLock)
        {
            if (_rules.TryGetValue(ruleId, out var rule))
            {
                return rule;
            }
        }

        return null;
    }

    public async Task<List<AlertRule>> GetAllRulesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var ruleIds = await _cacheService.SetMembersAsync("alert_rules");
        var rules = new List<AlertRule>();

        foreach (var ruleId in ruleIds)
        {
            var rule = await GetRuleAsync(ruleId, cancellationToken);
            if (rule != null && (includeDisabled || rule.Enabled))
            {
                rules.Add(rule);
            }
        }

        return rules.OrderBy(r => r.Name).ToList();
    }

    public async Task<List<AlertRule>> GetRulesByCategoryAsync(AlertCategory category, CancellationToken cancellationToken = default)
    {
        var allRules = await GetAllRulesAsync(false, cancellationToken);
        return allRules.Where(r => r.Category == category).ToList();
    }

    public async Task<List<AlertRule>> GetRulesByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        var allRules = await GetAllRulesAsync(false, cancellationToken);
        return allRules.Where(r => r.Tags.Contains(tag)).ToList();
    }

    public async Task<bool> EnableRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await GetRuleAsync(ruleId, cancellationToken);
        if (rule == null) return false;

        rule.Enabled = true;
        rule.UpdatedAt = DateTime.UtcNow;

        await UpdateRuleInStorage(rule);
        _logger.LogInformation("Alert rule enabled: {RuleId} - {RuleName}", rule.Id, rule.Name);
        return true;
    }

    public async Task<bool> DisableRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await GetRuleAsync(ruleId, cancellationToken);
        if (rule == null) return false;

        rule.Enabled = false;
        rule.UpdatedAt = DateTime.UtcNow;

        await UpdateRuleInStorage(rule);
        _logger.LogInformation("Alert rule disabled: {RuleId} - {RuleName}", rule.Id, rule.Name);
        return true;
    }

    public async Task<bool> TestRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await GetRuleAsync(ruleId, cancellationToken);
        if (rule == null) return false;

        // In a real implementation, this would test the rule against current metrics
        _logger.LogInformation("Testing alert rule: {RuleId} - {RuleName}", rule.Id, rule.Name);
        
        // Simulate test result
        await Task.Delay(100, cancellationToken);
        return true;
    }

    public async Task<AlertRule> DuplicateRuleAsync(string ruleId, string newName, CancellationToken cancellationToken = default)
    {
        var originalRule = await GetRuleAsync(ruleId, cancellationToken);
        if (originalRule == null)
        {
            throw new ArgumentException($"Alert rule {ruleId} not found");
        }

        var duplicatedRule = new AlertRule
        {
            Name = newName,
            Description = $"Copy of {originalRule.Description}",
            Category = originalRule.Category,
            Severity = originalRule.Severity,
            Condition = new AlertCondition
            {
                Metric = originalRule.Condition.Metric,
                Operator = originalRule.Condition.Operator,
                Threshold = originalRule.Condition.Threshold,
                Aggregation = originalRule.Condition.Aggregation,
                Filters = new Dictionary<string, string>(originalRule.Condition.Filters),
                GroupBy = new List<string>(originalRule.Condition.GroupBy)
            },
            EvaluationInterval = originalRule.EvaluationInterval,
            EvaluationWindow = originalRule.EvaluationWindow,
            MinimumDataPoints = originalRule.MinimumDataPoints,
            Tags = new List<string>(originalRule.Tags) { "duplicated" },
            NotificationChannels = new List<string>(originalRule.NotificationChannels),
            SuppressionRules = originalRule.SuppressionRules.Select(sr => new SuppressionRule
            {
                Name = sr.Name,
                Condition = sr.Condition,
                Duration = sr.Duration,
                Enabled = sr.Enabled
            }).ToList(),
            Enabled = false, // Start disabled
            CreatedBy = "System",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        lock (_rulesLock)
        {
            _rules[duplicatedRule.Id] = duplicatedRule;
        }

        await _cacheService.SetAsync($"alert_rule:{duplicatedRule.Id}", duplicatedRule, TimeSpan.FromDays(30));
        await _cacheService.SetAddAsync("alert_rules", duplicatedRule.Id);

        _logger.LogInformation("Alert rule duplicated: {OriginalRuleId} -> {NewRuleId} ({NewName})", 
            ruleId, duplicatedRule.Id, newName);

        return duplicatedRule;
    }

    private async Task UpdateRuleInStorage(AlertRule rule)
    {
        lock (_rulesLock)
        {
            _rules[rule.Id] = rule;
        }

        await _cacheService.SetAsync($"alert_rule:{rule.Id}", rule, TimeSpan.FromDays(30));
    }

    private async Task InitializeBuiltInRulesAsync()
    {
        try
        {
            var builtInRules = GetBuiltInAlertRules();
            
            foreach (var rule in builtInRules)
            {
                // Check if rule already exists
                var existingRule = await GetRuleAsync(rule.Id, CancellationToken.None);
                if (existingRule == null)
                {
                    lock (_rulesLock)
                    {
                        _rules[rule.Id] = rule;
                    }

                    await _cacheService.SetAsync($"alert_rule:{rule.Id}", rule, TimeSpan.FromDays(30));
                    await _cacheService.SetAddAsync("alert_rules", rule.Id);

                    _logger.LogInformation("Initialized built-in alert rule: {RuleName}", rule.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize built-in alert rules");
        }
    }

    private List<AlertRule> GetBuiltInAlertRules()
    {
        return new List<AlertRule>
        {
            // System-level rules
            new AlertRule
            {
                Id = "builtin-cpu-high",
                Name = "High CPU Usage",
                Description = "Triggers when CPU usage exceeds 80% for 5 minutes",
                Category = AlertCategory.System,
                Severity = AlertSeverity.Warning,
                Condition = new AlertCondition
                {
                    Metric = "cpu_usage_percent",
                    Operator = ComparisonOperator.GreaterThan,
                    Threshold = _config.Thresholds.Cpu.Warning,
                    Aggregation = AggregationType.Average
                },
                EvaluationInterval = TimeSpan.FromMinutes(1),
                EvaluationWindow = TimeSpan.FromMinutes(_config.Thresholds.Cpu.EvaluationWindowMinutes),
                MinimumDataPoints = _config.Thresholds.Cpu.MinimumDataPoints,
                Tags = new List<string> { "system", "cpu", "builtin" },
                CreatedBy = "System",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            
            new AlertRule
            {
                Id = "builtin-cpu-critical",
                Name = "Critical CPU Usage",
                Description = "Triggers when CPU usage exceeds 90% for 5 minutes",
                Category = AlertCategory.System,
                Severity = AlertSeverity.Critical,
                Condition = new AlertCondition
                {
                    Metric = "cpu_usage_percent",
                    Operator = ComparisonOperator.GreaterThan,
                    Threshold = _config.Thresholds.Cpu.Critical,
                    Aggregation = AggregationType.Average
                },
                EvaluationInterval = TimeSpan.FromMinutes(1),
                EvaluationWindow = TimeSpan.FromMinutes(_config.Thresholds.Cpu.EvaluationWindowMinutes),
                MinimumDataPoints = _config.Thresholds.Cpu.MinimumDataPoints,
                Tags = new List<string> { "system", "cpu", "builtin", "critical" },
                CreatedBy = "System",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },

            new AlertRule
            {
                Id = "builtin-memory-high",
                Name = "High Memory Usage",
                Description = "Triggers when memory usage exceeds 80% for 5 minutes",
                Category = AlertCategory.System,
                Severity = AlertSeverity.Warning,
                Condition = new AlertCondition
                {
                    Metric = "memory_usage_percent",
                    Operator = ComparisonOperator.GreaterThan,
                    Threshold = _config.Thresholds.Memory.Warning,
                    Aggregation = AggregationType.Average
                },
                EvaluationInterval = TimeSpan.FromMinutes(1),
                EvaluationWindow = TimeSpan.FromMinutes(_config.Thresholds.Memory.EvaluationWindowMinutes),
                MinimumDataPoints = _config.Thresholds.Memory.MinimumDataPoints,
                Tags = new List<string> { "system", "memory", "builtin" },
                CreatedBy = "System",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },

            new AlertRule
            {
                Id = "builtin-disk-high",
                Name = "High Disk Usage",
                Description = "Triggers when disk usage exceeds 85% for 10 minutes",
                Category = AlertCategory.System,
                Severity = AlertSeverity.Warning,
                Condition = new AlertCondition
                {
                    Metric = "disk_usage_percent",
                    Operator = ComparisonOperator.GreaterThan,
                    Threshold = _config.Thresholds.Disk.Warning,
                    Aggregation = AggregationType.Average
                },
                EvaluationInterval = TimeSpan.FromMinutes(2),
                EvaluationWindow = TimeSpan.FromMinutes(_config.Thresholds.Disk.EvaluationWindowMinutes),
                MinimumDataPoints = _config.Thresholds.Disk.MinimumDataPoints,
                Tags = new List<string> { "system", "disk", "builtin" },
                CreatedBy = "System",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },

            // Service-level rules
            new AlertRule
            {
                Id = "builtin-service-down",
                Name = "Service Down",
                Description = "Triggers when a service is not running",
                Category = AlertCategory.Service,
                Severity = AlertSeverity.Critical,
                Condition = new AlertCondition
                {
                    Metric = "service_status",
                    Operator = ComparisonOperator.Equal,
                    Threshold = 0, // 0 = stopped, 1 = running
                    Aggregation = AggregationType.Average
                },
                EvaluationInterval = TimeSpan.FromMinutes(1),
                EvaluationWindow = TimeSpan.FromMinutes(2),
                MinimumDataPoints = 1,
                Tags = new List<string> { "service", "availability", "builtin", "critical" },
                CreatedBy = "System",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },

            new AlertRule
            {
                Id = "builtin-service-response-time",
                Name = "High Service Response Time",
                Description = "Triggers when service response time exceeds 2 seconds",
                Category = AlertCategory.Performance,
                Severity = AlertSeverity.Warning,
                Condition = new AlertCondition
                {
                    Metric = "service_response_time_ms",
                    Operator = ComparisonOperator.GreaterThan,
                    Threshold = _config.Thresholds.Service.ResponseTimeWarningMs,
                    Aggregation = AggregationType.Average
                },
                EvaluationInterval = TimeSpan.FromMinutes(1),
                EvaluationWindow = TimeSpan.FromMinutes(_config.Thresholds.Service.EvaluationWindowMinutes),
                MinimumDataPoints = 3,
                Tags = new List<string> { "service", "performance", "builtin" },
                CreatedBy = "System",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },

            // Deployment-related rules
            new AlertRule
            {
                Id = "builtin-deployment-failure",
                Name = "Deployment Failure",
                Description = "Triggers when deployment failure rate exceeds 10%",
                Category = AlertCategory.Deployment,
                Severity = AlertSeverity.Critical,
                Condition = new AlertCondition
                {
                    Metric = "deployment_failure_rate",
                    Operator = ComparisonOperator.GreaterThan,
                    Threshold = _config.Thresholds.Deployment.FailureRateWarning,
                    Aggregation = AggregationType.Average
                },
                EvaluationInterval = TimeSpan.FromMinutes(2),
                EvaluationWindow = TimeSpan.FromMinutes(10),
                MinimumDataPoints = 2,
                Tags = new List<string> { "deployment", "builtin", "critical" },
                CreatedBy = "System",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },

            // Network-related rules
            new AlertRule
            {
                Id = "builtin-network-high",
                Name = "High Network Usage",
                Description = "Triggers when network usage exceeds 800 Mbps",
                Category = AlertCategory.Network,
                Severity = AlertSeverity.Warning,
                Condition = new AlertCondition
                {
                    Metric = "network_usage_mbps",
                    Operator = ComparisonOperator.GreaterThan,
                    Threshold = _config.Thresholds.Network.WarningMbps,
                    Aggregation = AggregationType.Average
                },
                EvaluationInterval = TimeSpan.FromMinutes(1),
                EvaluationWindow = TimeSpan.FromMinutes(_config.Thresholds.Network.EvaluationWindowMinutes),
                MinimumDataPoints = 3,
                Tags = new List<string> { "network", "builtin" },
                CreatedBy = "System",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };
    }
}