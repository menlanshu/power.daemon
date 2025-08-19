using Microsoft.AspNetCore.SignalR;
using PowerDaemon.Shared.Models;
using PowerDaemon.Web.Hubs;

namespace PowerDaemon.Web.Services;

public interface IRealTimeNotificationService
{
    Task SendServerStatusUpdate(Server server);
    Task SendServiceStatusUpdate(Service service);
    Task SendMetricsUpdate(string serverId, IEnumerable<Metric> metrics);
    Task SendSystemAlert(string alertType, string message, string? serverId = null);
}

public class RealTimeNotificationService : IRealTimeNotificationService
{
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly ILogger<RealTimeNotificationService> _logger;

    public RealTimeNotificationService(
        IHubContext<DashboardHub> hubContext,
        ILogger<RealTimeNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendServerStatusUpdate(Server server)
    {
        try
        {
            var update = new
            {
                Type = "ServerStatus",
                ServerId = server.Id.ToString(),
                Hostname = server.Hostname,
                Status = server.AgentStatus.ToString(),
                LastHeartbeat = server.LastHeartbeat,
                IsActive = server.IsActive
            };

            await _hubContext.Clients.Group("Dashboard").SendAsync("ServerStatusUpdate", update);
            await _hubContext.Clients.Group($"Server_{server.Id}").SendAsync("ServerStatusUpdate", update);

            _logger.LogDebug("Sent server status update for {Hostname}", server.Hostname);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending server status update for {Hostname}", server.Hostname);
        }
    }

    public async Task SendServiceStatusUpdate(Service service)
    {
        try
        {
            var update = new
            {
                Type = "ServiceStatus",
                ServiceId = service.Id.ToString(),
                ServerId = service.ServerId.ToString(),
                ServiceName = service.Name,
                Status = service.Status.ToString(),
                IsActive = service.IsActive,
                UpdatedAt = service.UpdatedAt
            };

            await _hubContext.Clients.Group("Dashboard").SendAsync("ServiceStatusUpdate", update);
            await _hubContext.Clients.Group($"Server_{service.ServerId}").SendAsync("ServiceStatusUpdate", update);

            _logger.LogDebug("Sent service status update for {ServiceName} on server {ServerId}", 
                service.Name, service.ServerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending service status update for {ServiceName}", service.Name);
        }
    }

    public async Task SendMetricsUpdate(string serverId, IEnumerable<Metric> metrics)
    {
        try
        {
            var metricGroups = metrics.GroupBy(m => m.MetricType).ToDictionary(
                g => g.Key,
                g => g.Select(m => new { m.MetricName, m.Value, m.Unit, m.Timestamp }).ToArray()
            );

            var update = new
            {
                Type = "MetricsUpdate",
                ServerId = serverId,
                Metrics = metricGroups,
                Timestamp = DateTime.UtcNow
            };

            await _hubContext.Clients.Group("Dashboard").SendAsync("MetricsUpdate", update);
            await _hubContext.Clients.Group($"Server_{serverId}").SendAsync("MetricsUpdate", update);

            _logger.LogDebug("Sent metrics update for server {ServerId} with {MetricCount} metrics", 
                serverId, metrics.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending metrics update for server {ServerId}", serverId);
        }
    }

    public async Task SendSystemAlert(string alertType, string message, string? serverId = null)
    {
        try
        {
            var alert = new
            {
                Type = "SystemAlert",
                AlertType = alertType,
                Message = message,
                ServerId = serverId,
                Timestamp = DateTime.UtcNow
            };

            if (!string.IsNullOrEmpty(serverId))
            {
                await _hubContext.Clients.Group($"Server_{serverId}").SendAsync("SystemAlert", alert);
            }

            await _hubContext.Clients.Group("Dashboard").SendAsync("SystemAlert", alert);

            _logger.LogInformation("Sent system alert: {AlertType} - {Message}", alertType, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending system alert: {Message}", message);
        }
    }
}