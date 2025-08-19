using Grpc.Core;
using Grpc.Net.Client;
using PowerDaemon.Protos;
using PowerDaemon.Shared.Models;
using Google.Protobuf.WellKnownTypes;

namespace PowerDaemon.Web.Services;

public interface IServiceManagementService
{
    Task<(bool Success, string Message)> StartServiceAsync(Guid serverId, string serviceName);
    Task<(bool Success, string Message)> StopServiceAsync(Guid serverId, string serviceName);
    Task<(bool Success, string Message)> RestartServiceAsync(Guid serverId, string serviceName);
    Task<(bool Success, string Message)> CheckServiceStatusAsync(Guid serverId, string serviceName);
}

public class ServiceManagementService : IServiceManagementService, IDisposable
{
    private readonly ILogger<ServiceManagementService> _logger;
    private readonly IConfiguration _configuration;
    private GrpcChannel? _channel;
    private AgentService.AgentServiceClient? _client;

    public ServiceManagementService(
        ILogger<ServiceManagementService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        InitializeGrpcClient();
    }

    public async Task<(bool Success, string Message)> StartServiceAsync(Guid serverId, string serviceName)
    {
        return await ExecuteServiceCommandAsync(serverId, serviceName, "start");
    }

    public async Task<(bool Success, string Message)> StopServiceAsync(Guid serverId, string serviceName)
    {
        return await ExecuteServiceCommandAsync(serverId, serviceName, "stop");
    }

    public async Task<(bool Success, string Message)> RestartServiceAsync(Guid serverId, string serviceName)
    {
        return await ExecuteServiceCommandAsync(serverId, serviceName, "restart");
    }

    public async Task<(bool Success, string Message)> CheckServiceStatusAsync(Guid serverId, string serviceName)
    {
        return await ExecuteServiceCommandAsync(serverId, serviceName, "status");
    }

    private async Task<(bool Success, string Message)> ExecuteServiceCommandAsync(
        Guid serverId, 
        string serviceName, 
        string command)
    {
        try
        {
            if (_client == null)
            {
                _logger.LogError("gRPC client not initialized");
                return (false, "Service management client not available");
            }

            _logger.LogInformation("Executing {Command} command for service {ServiceName} on server {ServerId}", 
                command, serviceName, serverId);

            var serviceCommand = new ServiceCommand
            {
                CommandId = Guid.NewGuid().ToString(),
                ServerId = serverId.ToString(),
                ServiceName = serviceName,
                Command = command,
                IssuedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            var response = await _client.ExecuteServiceCommandAsync(serviceCommand, 
                deadline: DateTime.UtcNow.AddSeconds(30),
                cancellationToken: CancellationToken.None);

            if (response.Success)
            {
                _logger.LogInformation("Command {Command} executed successfully for service {ServiceName}: {Message}", 
                    command, serviceName, response.Message);
                return (true, response.Message);
            }
            else
            {
                _logger.LogWarning("Command {Command} failed for service {ServiceName}: {Message}", 
                    command, serviceName, response.Message);
                return (false, response.Message);
            }
        }
        catch (RpcException ex)
        {
            var errorMessage = $"gRPC error executing {command} command: {ex.StatusCode} - {ex.Status.Detail}";
            _logger.LogError(ex, errorMessage);
            return (false, errorMessage);
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error executing {command} command: {ex.Message}";
            _logger.LogError(ex, errorMessage);
            return (false, errorMessage);
        }
    }

    private void InitializeGrpcClient()
    {
        try
        {
            // For now, we'll connect to the Central service directly
            // In a real deployment, this would be configured via appsettings
            var grpcEndpoint = _configuration.GetValue<string>("GrpcService:Endpoint") ?? "http://localhost:5000";
            
            var options = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 4 * 1024 * 1024, // 4MB
                MaxSendMessageSize = 4 * 1024 * 1024,    // 4MB
            };

            // For development without TLS
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            options.Credentials = ChannelCredentials.Insecure;

            _channel = GrpcChannel.ForAddress(grpcEndpoint, options);
            _client = new AgentService.AgentServiceClient(_channel);

            _logger.LogInformation("Service management gRPC client initialized for endpoint: {Endpoint}", grpcEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize service management gRPC client");
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        GC.SuppressFinalize(this);
    }
}