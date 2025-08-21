using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Monitoring.Models;
using PowerDaemon.Monitoring.Handlers;

namespace PowerDaemon.Monitoring.Services;

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly MonitoringConfiguration _config;
    private readonly IEnumerable<INotificationHandler> _handlers;
    private readonly Dictionary<string, NotificationChannel> _channels;

    public NotificationService(
        ILogger<NotificationService> logger,
        IOptions<MonitoringConfiguration> config,
        IEnumerable<INotificationHandler> handlers)
    {
        _logger = logger;
        _config = config.Value;
        _handlers = handlers;
        _channels = _config.Notifications.Channels.ToDictionary(c => c.Name, c => c);
    }

    public async Task<bool> SendNotificationAsync(Alert alert, NotificationChannel channel, CancellationToken cancellationToken = default)
    {
        if (!channel.Enabled)
        {
            _logger.LogDebug("Skipping disabled notification channel: {ChannelName}", channel.Name);
            return false;
        }

        var handler = _handlers.FirstOrDefault(h => h.ChannelType == channel.Type);
        if (handler == null)
        {
            _logger.LogError("No handler found for notification channel type: {ChannelType}", channel.Type);
            return false;
        }

        try
        {
            var result = await handler.SendAsync(alert, channel, cancellationToken);
            
            if (result.Success)
            {
                await RecordSuccessfulNotification(alert, channel);
                return true;
            }
            else
            {
                _logger.LogWarning("Notification failed for alert {AlertId} via {ChannelName}: {Error}", 
                    alert.Id, channel.Name, result.ErrorMessage);
                await RecordFailedNotification(alert, channel, result.ErrorMessage);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending notification for alert {AlertId} via {ChannelName}", 
                alert.Id, channel.Name);
            await RecordFailedNotification(alert, channel, ex.Message);
            return false;
        }
    }

    public async Task<bool> SendNotificationAsync(Alert alert, string channelName, CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(channelName, out var channel))
        {
            _logger.LogError("Notification channel not found: {ChannelName}", channelName);
            return false;
        }

        return await SendNotificationAsync(alert, channel, cancellationToken);
    }

    public async Task<List<NotificationResult>> SendBatchNotificationAsync(List<Alert> alerts, string channelName, CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(channelName, out var channel))
        {
            _logger.LogError("Notification channel not found: {ChannelName}", channelName);
            return alerts.Select(a => new NotificationResult
            {
                AlertId = a.Id,
                ChannelName = channelName,
                Success = false,
                ErrorMessage = "Channel not found"
            }).ToList();
        }

        var results = new List<NotificationResult>();
        var semaphore = new SemaphoreSlim(5, 5); // Limit concurrent notifications

        var tasks = alerts.Select(async alert =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var success = await SendNotificationAsync(alert, channel, cancellationToken);
                return new NotificationResult
                {
                    AlertId = alert.Id,
                    ChannelName = channelName,
                    Success = success,
                    ErrorMessage = success ? null : "Notification failed"
                };
            }
            finally
            {
                semaphore.Release();
            }
        });

        results.AddRange(await Task.WhenAll(tasks));
        semaphore.Dispose();

        return results;
    }

    public async Task<bool> TestChannelAsync(string channelName, CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(channelName, out var channel))
        {
            _logger.LogError("Notification channel not found: {ChannelName}", channelName);
            return false;
        }

        var handler = _handlers.FirstOrDefault(h => h.ChannelType == channel.Type);
        if (handler == null)
        {
            _logger.LogError("No handler found for notification channel type: {ChannelType}", channel.Type);
            return false;
        }

        try
        {
            return await handler.TestAsync(channel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while testing notification channel {ChannelName}", channelName);
            return false;
        }
    }

    public async Task<List<NotificationChannel>> GetChannelsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Placeholder for async operation
        return _channels.Values.ToList();
    }

    public async Task<NotificationChannel?> GetChannelAsync(string channelName, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Placeholder for async operation
        return _channels.TryGetValue(channelName, out var channel) ? channel : null;
    }

    public async Task<NotificationStatistics> GetStatisticsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would query a database or metrics store
        // For now, return placeholder statistics
        await Task.CompletedTask;
        
        return new NotificationStatistics
        {
            TotalNotifications = 0,
            SuccessfulNotifications = 0,
            FailedNotifications = 0,
            NotificationsByChannel = new Dictionary<NotificationChannelType, int>(),
            NotificationsByAlert = new Dictionary<string, int>(),
            AverageDeliveryTime = TimeSpan.Zero,
            CalculatedAt = DateTime.UtcNow
        };
    }

    private async Task RecordSuccessfulNotification(Alert alert, NotificationChannel channel)
    {
        var notification = new AlertNotification
        {
            ChannelName = channel.Name,
            ChannelType = channel.Type,
            Status = NotificationStatus.Sent,
            SentAt = DateTime.UtcNow
        };

        alert.Notifications.Add(notification);
        alert.NotificationsSent++;
        alert.LastNotificationAt = DateTime.UtcNow;

        _logger.LogInformation("Notification sent successfully for alert {AlertId} via {ChannelName}", 
            alert.Id, channel.Name);

        await Task.CompletedTask; // Placeholder for persistence
    }

    private async Task RecordFailedNotification(Alert alert, NotificationChannel channel, string? errorMessage)
    {
        var notification = new AlertNotification
        {
            ChannelName = channel.Name,
            ChannelType = channel.Type,
            Status = NotificationStatus.Failed,
            ErrorMessage = errorMessage,
            SentAt = DateTime.UtcNow
        };

        alert.Notifications.Add(notification);

        await Task.CompletedTask; // Placeholder for persistence
    }
}

// Background service for notification retries
public class NotificationRetryService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly ILogger<NotificationRetryService> _logger;
    private readonly INotificationService _notificationService;

    public NotificationRetryService(
        ILogger<NotificationRetryService> logger,
        INotificationService notificationService)
    {
        _logger = logger;
        _notificationService = notificationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification retry service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRetries(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in notification retry service");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("Notification retry service stopped");
    }

    private async Task ProcessRetries(CancellationToken cancellationToken)
    {
        // In a real implementation, this would query for failed notifications
        // and retry them based on retry policies
        await Task.CompletedTask;
        _logger.LogDebug("Processed notification retries");
    }
}

// Background service for alert cleanup
public class AlertCleanupService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly ILogger<AlertCleanupService> _logger;
    private readonly IAlertService _alertService;

    public AlertCleanupService(
        ILogger<AlertCleanupService> logger,
        IAlertService alertService)
    {
        _logger = logger;
        _alertService = alertService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Alert cleanup service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _alertService.CleanupExpiredAlertsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken); // Run hourly
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in alert cleanup service");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }

        _logger.LogInformation("Alert cleanup service stopped");
    }
}