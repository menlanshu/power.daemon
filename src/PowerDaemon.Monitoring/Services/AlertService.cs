using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Monitoring.Models;
using PowerDaemon.Cache.Services;
using PowerDaemon.Messaging.Services;
using PowerDaemon.Messaging.Messages;

namespace PowerDaemon.Monitoring.Services;

public class AlertService : IAlertService
{
    private readonly ILogger<AlertService> _logger;
    private readonly MonitoringConfiguration _config;
    private readonly ICacheService _cacheService;
    private readonly IMessagePublisher _messagePublisher;
    private readonly Dictionary<string, Alert> _alerts = new();
    private readonly object _alertsLock = new();

    public event EventHandler<AlertCreatedEventArgs>? AlertCreated;
    public event EventHandler<AlertUpdatedEventArgs>? AlertUpdated;
    public event EventHandler<AlertResolvedEventArgs>? AlertResolved;

    public AlertService(
        ILogger<AlertService> logger,
        IOptions<MonitoringConfiguration> config,
        ICacheService cacheService,
        IMessagePublisher messagePublisher)
    {
        _logger = logger;
        _config = config.Value;
        _cacheService = cacheService;
        _messagePublisher = messagePublisher;
    }

    public async Task<Alert> CreateAlertAsync(CreateAlertRequest request, CancellationToken cancellationToken = default)
    {
        var alert = new Alert
        {
            Title = request.Title,
            Message = request.Message,
            Severity = request.Severity,
            Category = request.Category,
            ServerId = request.ServerId,
            ServiceId = request.ServiceId,
            SourceRule = request.SourceRule,
            ThresholdValue = request.ThresholdValue,
            ActualValue = request.ActualValue,
            Unit = request.Unit,
            Metadata = request.Metadata,
            Tags = request.Tags,
            DataPoints = request.DataPoints
        };

        alert.Fingerprint = alert.GenerateFingerprint();

        // Check for existing alert with same fingerprint
        var existingAlert = await GetAlertByFingerprintAsync(alert.Fingerprint, cancellationToken);
        if (existingAlert != null && existingAlert.Status == AlertStatus.Active)
        {
            // Update existing alert instead of creating new one
            return await UpdateExistingAlertAsync(existingAlert, alert, cancellationToken);
        }

        lock (_alertsLock)
        {
            _alerts[alert.Id] = alert;
        }

        // Cache the alert
        await _cacheService.SetAsync($"alert:{alert.Id}", alert, TimeSpan.FromDays(1));
        await _cacheService.SetAsync($"alert_fingerprint:{alert.Fingerprint}", alert.Id, TimeSpan.FromDays(1));

        // Add to active alerts set
        await _cacheService.SetAddAsync("active_alerts", alert.Id);

        // Publish alert created event
        await PublishAlertEventAsync("alert.created", alert, cancellationToken);

        // Log alert creation
        _logger.LogWarning("Alert created: {AlertId} - {Title} [{Severity}] {Category}", 
            alert.Id, alert.Title, alert.Severity, alert.Category);

        // Fire event
        AlertCreated?.Invoke(this, new AlertCreatedEventArgs(alert));

        return alert;
    }

    public async Task<Alert?> GetAlertAsync(string alertId, CancellationToken cancellationToken = default)
    {
        // Try cache first
        var cachedAlert = await _cacheService.GetAsync<Alert>($"alert:{alertId}");
        if (cachedAlert != null)
        {
            return cachedAlert;
        }

        // Try in-memory store
        lock (_alertsLock)
        {
            if (_alerts.TryGetValue(alertId, out var alert))
            {
                return alert;
            }
        }

        return null;
    }

    public async Task<List<Alert>> GetActiveAlertsAsync(CancellationToken cancellationToken = default)
    {
        var activeAlertIds = await _cacheService.SetMembersAsync("active_alerts");
        var alerts = new List<Alert>();

        foreach (var alertId in activeAlertIds)
        {
            var alert = await GetAlertAsync(alertId, cancellationToken);
            if (alert != null && alert.Status == AlertStatus.Active)
            {
                alerts.Add(alert);
            }
        }

        return alerts.OrderByDescending(a => a.CreatedAt).ToList();
    }

    public async Task<List<Alert>> GetAlertsByServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var activeAlerts = await GetActiveAlertsAsync(cancellationToken);
        return activeAlerts.Where(a => a.ServerId == serverId).ToList();
    }

    public async Task<List<Alert>> GetAlertsByServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        var activeAlerts = await GetActiveAlertsAsync(cancellationToken);
        return activeAlerts.Where(a => a.ServiceId == serviceId).ToList();
    }

    public async Task<List<Alert>> GetAlertsByCategoryAsync(AlertCategory category, CancellationToken cancellationToken = default)
    {
        var activeAlerts = await GetActiveAlertsAsync(cancellationToken);
        return activeAlerts.Where(a => a.Category == category).ToList();
    }

    public async Task<List<Alert>> GetAlertsBySeverityAsync(AlertSeverity severity, CancellationToken cancellationToken = default)
    {
        var activeAlerts = await GetActiveAlertsAsync(cancellationToken);
        return activeAlerts.Where(a => a.Severity == severity).ToList();
    }

    public async Task<Alert> AcknowledgeAlertAsync(string alertId, string acknowledgedBy, string? comment = null, CancellationToken cancellationToken = default)
    {
        var alert = await GetAlertAsync(alertId, cancellationToken);
        if (alert == null)
        {
            throw new ArgumentException($"Alert {alertId} not found");
        }

        if (alert.Status != AlertStatus.Active)
        {
            throw new InvalidOperationException($"Alert {alertId} is not active (current status: {alert.Status})");
        }

        alert.Status = AlertStatus.Acknowledged;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedBy = acknowledgedBy;
        alert.UpdatedAt = DateTime.UtcNow;

        var action = new AlertAction
        {
            Type = AlertActionType.Acknowledged,
            PerformedBy = acknowledgedBy,
            Comment = comment
        };
        alert.Actions.Add(action);

        await UpdateAlertAsync(alert, cancellationToken);
        await PublishAlertEventAsync("alert.acknowledged", alert, cancellationToken);

        _logger.LogInformation("Alert acknowledged: {AlertId} by {AcknowledgedBy}", alertId, acknowledgedBy);
        AlertUpdated?.Invoke(this, new AlertUpdatedEventArgs(alert, AlertActionType.Acknowledged));

        return alert;
    }

    public async Task<Alert> ResolveAlertAsync(string alertId, string resolvedBy, string? comment = null, CancellationToken cancellationToken = default)
    {
        var alert = await GetAlertAsync(alertId, cancellationToken);
        if (alert == null)
        {
            throw new ArgumentException($"Alert {alertId} not found");
        }

        if (alert.Status == AlertStatus.Resolved)
        {
            return alert; // Already resolved
        }

        alert.Status = AlertStatus.Resolved;
        alert.ResolvedAt = DateTime.UtcNow;
        alert.ResolvedBy = resolvedBy;
        alert.UpdatedAt = DateTime.UtcNow;

        var action = new AlertAction
        {
            Type = AlertActionType.Resolved,
            PerformedBy = resolvedBy,
            Comment = comment
        };
        alert.Actions.Add(action);

        await UpdateAlertAsync(alert, cancellationToken);
        await _cacheService.SetRemoveAsync("active_alerts", alertId);
        await PublishAlertEventAsync("alert.resolved", alert, cancellationToken);

        _logger.LogInformation("Alert resolved: {AlertId} by {ResolvedBy}", alertId, resolvedBy);
        AlertResolved?.Invoke(this, new AlertResolvedEventArgs(alert, resolvedBy));

        return alert;
    }

    public async Task<Alert> EscalateAlertAsync(string alertId, string escalatedBy, string? comment = null, CancellationToken cancellationToken = default)
    {
        var alert = await GetAlertAsync(alertId, cancellationToken);
        if (alert == null)
        {
            throw new ArgumentException($"Alert {alertId} not found");
        }

        if (alert.Status != AlertStatus.Active && alert.Status != AlertStatus.Acknowledged)
        {
            throw new InvalidOperationException($"Alert {alertId} cannot be escalated (current status: {alert.Status})");
        }

        alert.EscalationLevel++;
        alert.EscalatedAt = DateTime.UtcNow;
        alert.UpdatedAt = DateTime.UtcNow;

        var action = new AlertAction
        {
            Type = AlertActionType.Escalated,
            PerformedBy = escalatedBy,
            Comment = comment,
            Metadata = new Dictionary<string, object>
            {
                ["escalation_level"] = alert.EscalationLevel
            }
        };
        alert.Actions.Add(action);

        await UpdateAlertAsync(alert, cancellationToken);
        await PublishAlertEventAsync("alert.escalated", alert, cancellationToken);

        _logger.LogWarning("Alert escalated: {AlertId} to level {EscalationLevel} by {EscalatedBy}", 
            alertId, alert.EscalationLevel, escalatedBy);
        AlertUpdated?.Invoke(this, new AlertUpdatedEventArgs(alert, AlertActionType.Escalated));

        return alert;
    }

    public async Task<Alert> AddCommentAsync(string alertId, string author, string comment, CancellationToken cancellationToken = default)
    {
        var alert = await GetAlertAsync(alertId, cancellationToken);
        if (alert == null)
        {
            throw new ArgumentException($"Alert {alertId} not found");
        }

        var action = new AlertAction
        {
            Type = AlertActionType.CommentAdded,
            PerformedBy = author,
            Comment = comment
        };
        alert.Actions.Add(action);
        alert.UpdatedAt = DateTime.UtcNow;

        await UpdateAlertAsync(alert, cancellationToken);

        _logger.LogInformation("Comment added to alert {AlertId} by {Author}", alertId, author);
        AlertUpdated?.Invoke(this, new AlertUpdatedEventArgs(alert, AlertActionType.CommentAdded));

        return alert;
    }

    public async Task<AlertStatistics> GetStatisticsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        from ??= DateTime.UtcNow.AddDays(-7);
        to ??= DateTime.UtcNow;

        var allAlerts = await GetAllAlertsInRangeAsync(from.Value, to.Value, cancellationToken);

        var stats = new AlertStatistics
        {
            TotalAlerts = allAlerts.Count,
            ActiveAlerts = allAlerts.Count(a => a.Status == AlertStatus.Active),
            AcknowledgedAlerts = allAlerts.Count(a => a.Status == AlertStatus.Acknowledged),
            ResolvedAlerts = allAlerts.Count(a => a.Status == AlertStatus.Resolved),
            CriticalAlerts = allAlerts.Count(a => a.Severity == AlertSeverity.Critical),
            WarningAlerts = allAlerts.Count(a => a.Severity == AlertSeverity.Warning),
            InfoAlerts = allAlerts.Count(a => a.Severity == AlertSeverity.Info),
            AlertsByCategory = allAlerts.GroupBy(a => a.Category).ToDictionary(g => g.Key, g => g.Count()),
            AlertsByServer = allAlerts.Where(a => !string.IsNullOrEmpty(a.ServerName))
                .GroupBy(a => a.ServerName!)
                .ToDictionary(g => g.Key, g => g.Count()),
            AlertsByService = allAlerts.Where(a => !string.IsNullOrEmpty(a.ServiceName))
                .GroupBy(a => a.ServiceName!)
                .ToDictionary(g => g.Key, g => g.Count())
        };

        // Calculate average resolution time
        var resolvedAlerts = allAlerts.Where(a => a.ResolvedAt.HasValue).ToList();
        if (resolvedAlerts.Any())
        {
            var totalResolutionTime = resolvedAlerts.Sum(a => (a.ResolvedAt!.Value - a.CreatedAt).TotalMinutes);
            stats.AverageResolutionTime = TimeSpan.FromMinutes(totalResolutionTime / resolvedAlerts.Count);
        }

        return stats;
    }

    public async Task<List<Alert>> SearchAlertsAsync(AlertSearchRequest request, CancellationToken cancellationToken = default)
    {
        var allAlerts = new List<Alert>();

        // Get alerts from cache and in-memory store
        var activeAlertIds = await _cacheService.SetMembersAsync("active_alerts");
        foreach (var alertId in activeAlertIds)
        {
            var alert = await GetAlertAsync(alertId, cancellationToken);
            if (alert != null)
            {
                allAlerts.Add(alert);
            }
        }

        // Apply filters
        var filteredAlerts = allAlerts.AsQueryable();

        if (request.Severity.HasValue)
            filteredAlerts = filteredAlerts.Where(a => a.Severity == request.Severity.Value);

        if (request.Category.HasValue)
            filteredAlerts = filteredAlerts.Where(a => a.Category == request.Category.Value);

        if (request.Status.HasValue)
            filteredAlerts = filteredAlerts.Where(a => a.Status == request.Status.Value);

        if (!string.IsNullOrEmpty(request.ServerId))
            filteredAlerts = filteredAlerts.Where(a => a.ServerId == request.ServerId);

        if (!string.IsNullOrEmpty(request.ServiceId))
            filteredAlerts = filteredAlerts.Where(a => a.ServiceId == request.ServiceId);

        if (request.From.HasValue)
            filteredAlerts = filteredAlerts.Where(a => a.CreatedAt >= request.From.Value);

        if (request.To.HasValue)
            filteredAlerts = filteredAlerts.Where(a => a.CreatedAt <= request.To.Value);

        if (request.Tags?.Any() == true)
            filteredAlerts = filteredAlerts.Where(a => request.Tags.Any(tag => a.Tags.Contains(tag)));

        if (!string.IsNullOrEmpty(request.Query))
        {
            var query = request.Query.ToLower();
            filteredAlerts = filteredAlerts.Where(a => 
                a.Title.ToLower().Contains(query) || 
                a.Message.ToLower().Contains(query) ||
                (a.ServerName?.ToLower().Contains(query) ?? false) ||
                (a.ServiceName?.ToLower().Contains(query) ?? false));
        }

        // Apply sorting
        if (!string.IsNullOrEmpty(request.SortBy))
        {
            filteredAlerts = request.SortBy.ToLower() switch
            {
                "created_at" => request.SortDescending ? 
                    filteredAlerts.OrderByDescending(a => a.CreatedAt) : 
                    filteredAlerts.OrderBy(a => a.CreatedAt),
                "severity" => request.SortDescending ? 
                    filteredAlerts.OrderByDescending(a => a.Severity) : 
                    filteredAlerts.OrderBy(a => a.Severity),
                "title" => request.SortDescending ? 
                    filteredAlerts.OrderByDescending(a => a.Title) : 
                    filteredAlerts.OrderBy(a => a.Title),
                _ => filteredAlerts.OrderByDescending(a => a.CreatedAt)
            };
        }
        else
        {
            filteredAlerts = filteredAlerts.OrderByDescending(a => a.CreatedAt);
        }

        // Apply pagination
        return filteredAlerts.Skip(request.Skip).Take(request.Take).ToList();
    }

    public async Task<bool> SuppressAlertAsync(string alertId, TimeSpan duration, string reason, CancellationToken cancellationToken = default)
    {
        var alert = await GetAlertAsync(alertId, cancellationToken);
        if (alert == null) return false;

        alert.Status = AlertStatus.Suppressed;
        alert.UpdatedAt = DateTime.UtcNow;

        var action = new AlertAction
        {
            Type = AlertActionType.Suppressed,
            PerformedBy = "System",
            Comment = reason,
            Metadata = new Dictionary<string, object>
            {
                ["suppression_duration"] = duration.ToString(),
                ["suppression_until"] = DateTime.UtcNow.Add(duration).ToString("O")
            }
        };
        alert.Actions.Add(action);

        await UpdateAlertAsync(alert, cancellationToken);

        // Schedule unsuppression
        var suppressionKey = $"alert_suppression:{alertId}";
        await _cacheService.SetAsync(suppressionKey, DateTime.UtcNow.Add(duration), duration.Add(TimeSpan.FromMinutes(1)));

        _logger.LogInformation("Alert suppressed: {AlertId} for {Duration}, reason: {Reason}", 
            alertId, duration, reason);

        return true;
    }

    public async Task<bool> UnsuppressAlertAsync(string alertId, CancellationToken cancellationToken = default)
    {
        var alert = await GetAlertAsync(alertId, cancellationToken);
        if (alert == null || alert.Status != AlertStatus.Suppressed) return false;

        alert.Status = AlertStatus.Active;
        alert.UpdatedAt = DateTime.UtcNow;

        var action = new AlertAction
        {
            Type = AlertActionType.Updated,
            PerformedBy = "System",
            Comment = "Alert unsuppressed"
        };
        alert.Actions.Add(action);

        await UpdateAlertAsync(alert, cancellationToken);
        await _cacheService.DeleteAsync($"alert_suppression:{alertId}");

        _logger.LogInformation("Alert unsuppressed: {AlertId}", alertId);

        return true;
    }

    public async Task CleanupExpiredAlertsAsync(CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_config.Alerting.AlertRetentionDays);
        var allAlerts = new List<Alert>();

        // Get all alerts (this is simplified - in production, you'd want to batch this)
        lock (_alertsLock)
        {
            allAlerts.AddRange(_alerts.Values);
        }

        var expiredAlerts = allAlerts
            .Where(a => a.CreatedAt < cutoffDate && a.Status == AlertStatus.Resolved)
            .ToList();

        foreach (var alert in expiredAlerts)
        {
            lock (_alertsLock)
            {
                _alerts.Remove(alert.Id);
            }
            
            await _cacheService.DeleteAsync($"alert:{alert.Id}");
            await _cacheService.DeleteAsync($"alert_fingerprint:{alert.Fingerprint}");
        }

        if (expiredAlerts.Any())
        {
            _logger.LogInformation("Cleaned up {Count} expired alerts", expiredAlerts.Count);
        }
    }

    private async Task<Alert?> GetAlertByFingerprintAsync(string fingerprint, CancellationToken cancellationToken)
    {
        var alertId = await _cacheService.GetAsync<string>($"alert_fingerprint:{fingerprint}");
        return !string.IsNullOrEmpty(alertId) ? await GetAlertAsync(alertId, cancellationToken) : null;
    }

    private async Task<Alert> UpdateExistingAlertAsync(Alert existingAlert, Alert newAlert, CancellationToken cancellationToken)
    {
        existingAlert.UpdatedAt = DateTime.UtcNow;
        existingAlert.ActualValue = newAlert.ActualValue;
        existingAlert.DataPoints.AddRange(newAlert.DataPoints);

        // Limit data points
        if (existingAlert.DataPoints.Count > 100)
        {
            existingAlert.DataPoints = existingAlert.DataPoints
                .OrderByDescending(dp => dp.Timestamp)
                .Take(100)
                .ToList();
        }

        await UpdateAlertAsync(existingAlert, cancellationToken);
        return existingAlert;
    }

    private async Task UpdateAlertAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        lock (_alertsLock)
        {
            _alerts[alert.Id] = alert;
        }

        await _cacheService.SetAsync($"alert:{alert.Id}", alert, TimeSpan.FromDays(1));
    }

    private async Task<List<Alert>> GetAllAlertsInRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var allAlerts = new List<Alert>();
        
        lock (_alertsLock)
        {
            allAlerts.AddRange(_alerts.Values.Where(a => a.CreatedAt >= from && a.CreatedAt <= to));
        }

        return allAlerts;
    }

    private async Task PublishAlertEventAsync(string eventType, Alert alert, CancellationToken cancellationToken)
    {
        try
        {
            var alertEvent = new
            {
                event_type = eventType,
                alert_id = alert.Id,
                alert = alert,
                timestamp = DateTime.UtcNow
            };

            await _messagePublisher.PublishAsync(alertEvent, $"alerts.{eventType}", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish alert event {EventType} for alert {AlertId}", eventType, alert.Id);
        }
    }
}