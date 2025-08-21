using PowerDaemon.Monitoring.Models;
using PowerDaemon.Monitoring.Configuration;

namespace PowerDaemon.Monitoring.Handlers;

public interface INotificationHandler
{
    NotificationChannelType ChannelType { get; }
    Task<NotificationResult> SendAsync(Alert alert, NotificationChannel channel, CancellationToken cancellationToken = default);
    Task<bool> TestAsync(NotificationChannel channel, CancellationToken cancellationToken = default);
    Task<bool> IsConfigurationValidAsync(NotificationChannel channel);
}

public abstract class BaseNotificationHandler : INotificationHandler
{
    public abstract NotificationChannelType ChannelType { get; }

    public abstract Task<NotificationResult> SendAsync(Alert alert, NotificationChannel channel, CancellationToken cancellationToken = default);

    public abstract Task<bool> TestAsync(NotificationChannel channel, CancellationToken cancellationToken = default);

    public abstract Task<bool> IsConfigurationValidAsync(NotificationChannel channel);

    protected virtual string FormatAlertMessage(Alert alert, NotificationChannel channel, string template)
    {
        var message = template
            .Replace("{Severity}", alert.Severity.ToString())
            .Replace("{Category}", alert.Category.ToString())
            .Replace("{Title}", alert.Title)
            .Replace("{Message}", alert.Message)
            .Replace("{ServerName}", alert.ServerName ?? "Unknown")
            .Replace("{ServiceName}", alert.ServiceName ?? "N/A")
            .Replace("{Timestamp}", alert.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"))
            .Replace("{Details}", FormatAlertDetails(alert));

        return message;
    }

    protected virtual string FormatAlertDetails(Alert alert)
    {
        var details = new List<string>();

        if (alert.ThresholdValue.HasValue && alert.ActualValue.HasValue)
        {
            var unit = !string.IsNullOrEmpty(alert.Unit) ? $" {alert.Unit}" : "";
            details.Add($"Threshold: {alert.ThresholdValue.Value:F2}{unit}");
            details.Add($"Actual: {alert.ActualValue.Value:F2}{unit}");
        }

        if (!string.IsNullOrEmpty(alert.SourceRule))
        {
            details.Add($"Rule: {alert.SourceRule}");
        }

        if (alert.Tags.Any())
        {
            details.Add($"Tags: {string.Join(", ", alert.Tags)}");
        }

        if (alert.DataPoints.Any())
        {
            var latestPoint = alert.DataPoints.OrderByDescending(dp => dp.Timestamp).First();
            details.Add($"Latest Value: {latestPoint.Value:F2} at {latestPoint.Timestamp:HH:mm:ss}");
        }

        return string.Join("\n", details);
    }

    protected virtual bool ShouldSendNotification(Alert alert, NotificationChannel channel)
    {
        // Check severity levels
        if (channel.SeverityLevels.Any() && !channel.SeverityLevels.Contains(alert.Severity))
        {
            return false;
        }

        // Check time restrictions
        if (channel.TimeRestrictions.Any())
        {
            var now = DateTime.UtcNow;
            var currentTime = now.TimeOfDay;
            var currentDay = now.DayOfWeek;

            var hasActiveRestriction = channel.TimeRestrictions.Any(tr =>
                tr.Enabled &&
                (tr.DayOfWeek == null || tr.DayOfWeek == currentDay) &&
                IsTimeInRange(currentTime, tr.StartTime, tr.EndTime));

            if (!hasActiveRestriction)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTimeInRange(TimeSpan current, TimeSpan start, TimeSpan end)
    {
        if (start <= end)
        {
            return current >= start && current <= end;
        }
        else
        {
            // Handle overnight ranges (e.g., 22:00 to 06:00)
            return current >= start || current <= end;
        }
    }
}