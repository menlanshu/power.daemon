using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using PowerDaemon.Monitoring.Models;
using PowerDaemon.Monitoring.Configuration;
using PowerDaemon.Monitoring.Services;

namespace PowerDaemon.Monitoring.Handlers;

public class WebhookNotificationHandler : BaseNotificationHandler
{
    private readonly ILogger<WebhookNotificationHandler> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public WebhookNotificationHandler(
        ILogger<WebhookNotificationHandler> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public override NotificationChannelType ChannelType => NotificationChannelType.Webhook;

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

            var config = ParseWebhookConfiguration(channel.Configuration);
            if (config == null)
            {
                result.Success = false;
                result.ErrorMessage = "Invalid webhook configuration";
                return result;
            }

            var payload = BuildWebhookPayload(alert, config);
            var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            using var httpClient = _httpClientFactory.CreateClient("PowerDaemon.Monitoring");
            httpClient.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

            using var request = new HttpRequestMessage(HttpMethod.Post, config.Url);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Add custom headers
            foreach (var header in config.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Add authentication
            if (!string.IsNullOrEmpty(config.AuthToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.AuthToken);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                result.Success = true;
                _logger.LogInformation("Webhook notification sent for alert {AlertId} to {Url} via {ChannelName}", 
                    alert.Id, config.Url, channel.Name);
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                result.Success = false;
                result.ErrorMessage = $"HTTP {(int)response.StatusCode} {response.StatusCode}: {responseBody}";
                _logger.LogError("Webhook notification failed for alert {AlertId} to {Url}: {StatusCode} {ResponseBody}", 
                    alert.Id, config.Url, response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to send webhook notification for alert {AlertId} via {ChannelName}", 
                alert.Id, channel.Name);
        }

        return result;
    }

    public override async Task<bool> TestAsync(NotificationChannel channel, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = ParseWebhookConfiguration(channel.Configuration);
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
        var config = ParseWebhookConfiguration(channel.Configuration);
        return Task.FromResult(config != null && 
                              !string.IsNullOrEmpty(config.Url) &&
                              Uri.TryCreate(config.Url, UriKind.Absolute, out _));
    }

    private static WebhookConfiguration? ParseWebhookConfiguration(Dictionary<string, object> config)
    {
        try
        {
            return new WebhookConfiguration
            {
                Url = GetConfigValue<string>(config, "url") ?? "",
                Method = GetConfigValue<string>(config, "method") ?? "POST",
                TimeoutSeconds = GetConfigValue<int>(config, "timeout_seconds", 30),
                AuthToken = GetConfigValue<string>(config, "auth_token"),
                Headers = GetConfigValue<Dictionary<string, string>>(config, "headers") ?? new(),
                PayloadTemplate = GetConfigValue<string>(config, "payload_template"),
                IncludeMetadata = GetConfigValue<bool>(config, "include_metadata", true),
                IncludeDataPoints = GetConfigValue<bool>(config, "include_data_points", false),
                MaxDataPoints = GetConfigValue<int>(config, "max_data_points", 10)
            };
        }
        catch
        {
            return null;
        }
    }

    private object BuildWebhookPayload(Alert alert, WebhookConfiguration config)
    {
        if (!string.IsNullOrEmpty(config.PayloadTemplate))
        {
            // Use custom template
            var customPayload = FormatAlertMessage(alert, 
                new NotificationChannel { Configuration = new Dictionary<string, object> { ["payload_template"] = config.PayloadTemplate } }, 
                config.PayloadTemplate);
            
            try
            {
                return JsonSerializer.Deserialize<object>(customPayload) ?? new { };
            }
            catch
            {
                // Fall through to default payload if custom template is invalid
            }
        }

        // Build default payload
        var payload = new
        {
            alert = new
            {
                id = alert.Id,
                title = alert.Title,
                message = alert.Message,
                severity = alert.Severity.ToString().ToLower(),
                category = alert.Category.ToString().ToLower(),
                status = alert.Status.ToString().ToLower(),
                server_id = alert.ServerId,
                server_name = alert.ServerName,
                service_id = alert.ServiceId,
                service_name = alert.ServiceName,
                created_at = alert.CreatedAt.ToString("O"),
                updated_at = alert.UpdatedAt.ToString("O"),
                acknowledged_at = alert.AcknowledgedAt?.ToString("O"),
                acknowledged_by = alert.AcknowledgedBy,
                resolved_at = alert.ResolvedAt?.ToString("O"),
                resolved_by = alert.ResolvedBy,
                escalation_level = alert.EscalationLevel,
                source_rule = alert.SourceRule,
                threshold_value = alert.ThresholdValue,
                actual_value = alert.ActualValue,
                unit = alert.Unit,
                tags = alert.Tags,
                correlation_id = alert.CorrelationId,
                fingerprint = alert.Fingerprint
            },
            metadata = config.IncludeMetadata ? alert.Metadata : null,
            data_points = config.IncludeDataPoints && alert.DataPoints.Any()
                ? alert.DataPoints
                    .OrderByDescending(dp => dp.Timestamp)
                    .Take(config.MaxDataPoints)
                    .Select(dp => new
                    {
                        timestamp = dp.Timestamp.ToString("O"),
                        value = dp.Value,
                        metadata = dp.Metadata
                    })
                    .ToList()
                : null,
            system = new
            {
                name = "PowerDaemon",
                version = "1.0.0",
                timestamp = DateTime.UtcNow.ToString("O")
            }
        };

        return payload;
    }

    private static T GetConfigValue<T>(Dictionary<string, object> config, string key, T defaultValue = default!)
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
                return defaultValue;
            }
        }
        return defaultValue;
    }

    private static Alert CreateTestAlert()
    {
        return new Alert
        {
            Id = "test-webhook-alert",
            Title = "Test Webhook Alert",
            Message = "This is a test alert from PowerDaemon monitoring system via webhook",
            Severity = AlertSeverity.Info,
            Category = AlertCategory.System,
            ServerName = "test-server",
            ServiceName = "test-service",
            CreatedAt = DateTime.UtcNow,
            Status = AlertStatus.Active,
            Tags = new List<string> { "test", "webhook" },
            Metadata = new Dictionary<string, object>
            {
                ["test"] = true,
                ["channel"] = "webhook"
            }
        };
    }

    private class WebhookConfiguration
    {
        public string Url { get; set; } = string.Empty;
        public string Method { get; set; } = "POST";
        public int TimeoutSeconds { get; set; } = 30;
        public string? AuthToken { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new();
        public string? PayloadTemplate { get; set; }
        public bool IncludeMetadata { get; set; } = true;
        public bool IncludeDataPoints { get; set; } = false;
        public int MaxDataPoints { get; set; } = 10;
    }
}