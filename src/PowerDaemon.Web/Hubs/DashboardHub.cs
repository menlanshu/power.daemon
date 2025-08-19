using Microsoft.AspNetCore.SignalR;

namespace PowerDaemon.Web.Hubs;

public class DashboardHub : Hub
{
    private readonly ILogger<DashboardHub> _logger;

    public DashboardHub(ILogger<DashboardHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, "Dashboard");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Dashboard");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinServerGroup(string serverId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Server_{serverId}");
        _logger.LogDebug("Client {ConnectionId} joined server group: {ServerId}", Context.ConnectionId, serverId);
    }

    public async Task LeaveServerGroup(string serverId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Server_{serverId}");
        _logger.LogDebug("Client {ConnectionId} left server group: {ServerId}", Context.ConnectionId, serverId);
    }
}