using System.Net.Mail;
using System.Net;
using Microsoft.Extensions.Logging;
using PowerDaemon.Monitoring.Models;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Monitoring.Services;

namespace PowerDaemon.Monitoring.Handlers;

public class EmailNotificationHandler : BaseNotificationHandler
{
    private readonly ILogger<EmailNotificationHandler> _logger;

    public EmailNotificationHandler(ILogger<EmailNotificationHandler> logger)
    {
        _logger = logger;
    }

    public override NotificationChannelType ChannelType => NotificationChannelType.Email;

    public override async Task<NotificationResult> SendAsync(Alert alert, NotificationChannel channel, CancellationToken cancellationToken = default)
    {
        var result = new NotificationResult
        {
            AlertId = alert.Id,
            ChannelName = channel.Name
        };

        try
        {
            if (!ShouldSendNotification(alert, channel))
            {
                result.Success = false;
                result.ErrorMessage = "Notification filtered by channel restrictions";
                return result;
            }

            var config = ParseEmailConfiguration(channel.Configuration);
            if (config == null)
            {
                result.Success = false;
                result.ErrorMessage = "Invalid email configuration";
                return result;
            }

            var subject = FormatAlertMessage(alert, channel, GetSubjectTemplate(alert));
            var body = FormatAlertMessage(alert, channel, GetBodyTemplate(alert));

            using var message = new MailMessage();
            
            // Set recipients
            foreach (var recipient in config.Recipients)
            {
                message.To.Add(recipient);
            }

            if (config.CcRecipients.Any())
            {
                foreach (var cc in config.CcRecipients)
                {
                    message.CC.Add(cc);
                }
            }

            message.From = new MailAddress(config.FromAddress, config.FromName);
            message.Subject = subject;
            message.Body = body;
            message.IsBodyHtml = config.IsHtml;

            // Set priority based on alert severity
            message.Priority = alert.Severity switch
            {
                AlertSeverity.Critical => MailPriority.High,
                AlertSeverity.Warning => MailPriority.Normal,
                _ => MailPriority.Low
            };

            using var client = new SmtpClient(config.SmtpHost, config.SmtpPort);
            client.EnableSsl = config.EnableSsl;
            
            if (!string.IsNullOrEmpty(config.Username))
            {
                client.Credentials = new NetworkCredential(config.Username, config.Password);
            }

            await client.SendMailAsync(message, cancellationToken);

            result.Success = true;
            _logger.LogInformation("Email notification sent for alert {AlertId} to {RecipientCount} recipients via {ChannelName}", 
                alert.Id, config.Recipients.Count, channel.Name);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to send email notification for alert {AlertId} via {ChannelName}", 
                alert.Id, channel.Name);
        }

        return result;
    }

    public override async Task<bool> TestAsync(NotificationChannel channel, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = ParseEmailConfiguration(channel.Configuration);
            if (config == null) return false;

            var testAlert = CreateTestAlert();
            var result = await SendAsync(testAlert, channel, cancellationToken);
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    public override Task<bool> IsConfigurationValidAsync(NotificationChannel channel)
    {
        var config = ParseEmailConfiguration(channel.Configuration);
        return Task.FromResult(config != null && 
                              !string.IsNullOrEmpty(config.SmtpHost) &&
                              config.SmtpPort > 0 &&
                              !string.IsNullOrEmpty(config.FromAddress) &&
                              config.Recipients.Any());
    }

    private static EmailConfiguration? ParseEmailConfiguration(Dictionary<string, object> config)
    {
        try
        {
            return new EmailConfiguration
            {
                SmtpHost = GetConfigValue<string>(config, "smtp_host") ?? "",
                SmtpPort = GetConfigValue<int>(config, "smtp_port"),
                EnableSsl = GetConfigValue<bool>(config, "enable_ssl"),
                Username = GetConfigValue<string>(config, "username"),
                Password = GetConfigValue<string>(config, "password"),
                FromAddress = GetConfigValue<string>(config, "from_address") ?? "",
                FromName = GetConfigValue<string>(config, "from_name") ?? "PowerDaemon Monitoring",
                Recipients = GetConfigValue<List<string>>(config, "recipients") ?? new(),
                CcRecipients = GetConfigValue<List<string>>(config, "cc_recipients") ?? new(),
                IsHtml = GetConfigValue<bool>(config, "is_html")
            };
        }
        catch
        {
            return null;
        }
    }

    private static T? GetConfigValue<T>(Dictionary<string, object> config, string key)
    {
        if (config.TryGetValue(key, out var value))
        {
            try
            {
                if (value is T directValue)
                    return directValue;

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }
        return default;
    }

    private static string GetSubjectTemplate(Alert alert)
    {
        return alert.Status == AlertStatus.Resolved
            ? "[RESOLVED] {Category}: {Title}"
            : "[{Severity}] {Category}: {Title}";
    }

    private static string GetBodyTemplate(Alert alert)
    {
        if (alert.Status == AlertStatus.Resolved)
        {
            return @"
Alert has been resolved.

Server: {ServerName}
Service: {ServiceName}
Category: {Category}
Resolved: {Timestamp}

Original Message: {Message}

Details:
{Details}

--
PowerDaemon Monitoring System
";
        }

        return @"
ALERT: {Title}

Server: {ServerName}
Service: {ServiceName}
Category: {Category}
Severity: {Severity}
Time: {Timestamp}

Message: {Message}

Details:
{Details}

--
PowerDaemon Monitoring System
";
    }

    private static Alert CreateTestAlert()
    {
        return new Alert
        {
            Id = "test-alert",
            Title = "Test Alert",
            Message = "This is a test alert from PowerDaemon monitoring system",
            Severity = AlertSeverity.Info,
            Category = AlertCategory.System,
            ServerName = "test-server",
            ServiceName = "test-service",
            CreatedAt = DateTime.UtcNow,
            Status = AlertStatus.Active,
            Tags = new List<string> { "test" }
        };
    }

    private class EmailConfiguration
    {
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string FromAddress { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public List<string> Recipients { get; set; } = new();
        public List<string> CcRecipients { get; set; } = new();
        public bool IsHtml { get; set; } = false;
    }
}