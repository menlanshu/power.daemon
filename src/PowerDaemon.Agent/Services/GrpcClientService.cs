using System.Runtime.InteropServices;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Protos;
using PowerDaemon.Shared.Configuration;
using PowerDaemon.Shared.DTOs;
using PowerDaemon.Shared.Models;
using Google.Protobuf.WellKnownTypes;

namespace PowerDaemon.Agent.Services;

public class GrpcClientService : IGrpcClient, IDisposable
{
    private readonly ILogger<GrpcClientService> _logger;
    private readonly AgentConfiguration _config;
    private GrpcChannel? _channel;
    private AgentService.AgentServiceClient? _client;
    private string? _registeredServerId;

    public GrpcClientService(
        ILogger<GrpcClientService> logger,
        IOptions<AgentConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
        InitializeGrpcClient();
    }

    public async Task<bool> RegisterAgentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Registering agent with central service at {Endpoint}", _config.GrpcEndpoint);

            if (_client == null)
            {
                _logger.LogError("gRPC client not initialized");
                return false;
            }

            var registration = new AgentRegistration
            {
                Hostname = _config.Hostname,
                IpAddress = GetLocalIpAddress(),
                OsType = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : "Linux",
                OsVersion = RuntimeInformation.OSDescription,
                AgentVersion = GetAgentVersion(),
                CpuCores = Environment.ProcessorCount,
                TotalMemoryMb = GetTotalMemoryMb(),
                Location = "Unknown", // Could be configured
                Environment = "Production"
            };

            var response = await _client.RegisterAgentAsync(registration, 
                deadline: DateTime.UtcNow.AddSeconds(30),
                cancellationToken: cancellationToken);

            if (response.Success)
            {
                _registeredServerId = response.ServerId;
                _config.ServerId = Guid.Parse(_registeredServerId);
                _logger.LogInformation("Agent registered successfully with ID: {ServerId}", _registeredServerId);
                return true;
            }
            else
            {
                _logger.LogError("Agent registration failed: {Message}", response.Message);
                return false;
            }
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC error during agent registration: {Status}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during agent registration");
            return false;
        }
    }

    public async Task<bool> SendHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_client == null || string.IsNullOrEmpty(_registeredServerId))
            {
                _logger.LogWarning("Agent not registered, attempting registration before heartbeat");
                if (!await RegisterAgentAsync(cancellationToken))
                {
                    return false;
                }
            }

            _logger.LogDebug("Sending heartbeat to central service");

            var request = new HeartbeatRequest
            {
                ServerId = _registeredServerId!,
                Hostname = heartbeat.Hostname,
                Timestamp = Timestamp.FromDateTime(heartbeat.Timestamp),
                AgentStatus = heartbeat.Status.ToString(),
                ServiceCount = heartbeat.ServiceCount,
                CpuUsagePercent = heartbeat.CpuUsagePercent,
                MemoryUsageMb = heartbeat.MemoryUsageMb,
                ErrorMessage = heartbeat.ErrorMessage ?? string.Empty
            };

            var response = await _client.SendHeartbeatAsync(request,
                deadline: DateTime.UtcNow.AddSeconds(10),
                cancellationToken: cancellationToken);

            if (response.Success)
            {
                _logger.LogDebug("Heartbeat sent successfully");
                
                // Process any pending commands from the central service
                if (response.PendingCommands.Count > 0)
                {
                    _logger.LogInformation("Received {CommandCount} pending commands", response.PendingCommands.Count);
                    // TODO: Process commands
                }
                
                return true;
            }
            else
            {
                _logger.LogWarning("Heartbeat rejected: {Message}", response.Message);
                return false;
            }
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "gRPC error during heartbeat: {Status}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during heartbeat");
            return false;
        }
    }

    public async Task<bool> ReportServicesAsync(ServiceDiscoveryResult services, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_client == null || string.IsNullOrEmpty(_registeredServerId))
            {
                _logger.LogWarning("Agent not registered, cannot report services");
                return false;
            }

            _logger.LogInformation("Reporting {ServiceCount} discovered services", services.Services.Count);

            var discovery = new ServiceDiscovery
            {
                ServerId = _registeredServerId,
                Hostname = services.Hostname,
                DiscoveredAt = Timestamp.FromDateTime(services.DiscoveredAt)
            };

            // Convert services to protobuf format
            foreach (var service in services.Services)
            {
                var serviceInfo = new ServiceInfo
                {
                    Name = service.Name,
                    DisplayName = service.DisplayName ?? string.Empty,
                    Description = service.Description ?? string.Empty,
                    Version = service.Version ?? string.Empty,
                    Status = service.Status.ToString(),
                    ProcessId = service.ProcessId ?? 0,
                    ExecutablePath = service.ExecutablePath,
                    WorkingDirectory = service.WorkingDirectory ?? string.Empty,
                    StartupType = service.StartupType.ToString(),
                    ServiceAccount = service.ServiceAccount ?? string.Empty,
                    IsActive = service.IsActive
                };

                if (service.LastStartTime.HasValue)
                {
                    serviceInfo.LastStartTime = Timestamp.FromDateTime(service.LastStartTime.Value);
                }

                discovery.Services.Add(serviceInfo);
            }

            var response = await _client.ReportServicesAsync(discovery,
                deadline: DateTime.UtcNow.AddSeconds(60),
                cancellationToken: cancellationToken);

            if (response.Success)
            {
                _logger.LogInformation("Services reported successfully");
                return true;
            }
            else
            {
                _logger.LogWarning("Service reporting failed: {Message}", response.Message);
                return false;
            }
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "gRPC error during service reporting: {Status}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during service reporting");
            return false;
        }
    }

    public async Task<bool> StreamMetricsAsync(MetricBatchDto metrics, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_client == null || string.IsNullOrEmpty(_registeredServerId))
            {
                _logger.LogWarning("Agent not registered, cannot stream metrics");
                return false;
            }

            _logger.LogDebug("Streaming {MetricCount} metrics", metrics.Metrics.Count);

            // Create a streaming call
            using var call = _client.StreamMetrics();

            var batch = new MetricsBatch
            {
                ServerId = _registeredServerId,
                Hostname = metrics.Hostname,
                CollectedAt = Timestamp.FromDateTime(metrics.CollectedAt)
            };

            // Convert metrics to protobuf format
            foreach (var metric in metrics.Metrics)
            {
                var metricData = new MetricData
                {
                    ServerId = metric.ServerId.ToString(),
                    ServiceId = metric.ServiceId?.ToString() ?? string.Empty,
                    MetricType = metric.MetricType,
                    MetricName = metric.MetricName,
                    Value = metric.Value,
                    Unit = metric.Unit,
                    Timestamp = Timestamp.FromDateTime(metric.Timestamp)
                };

                // Add tags if present
                if (metric.Tags != null)
                {
                    foreach (var tag in metric.Tags)
                    {
                        metricData.Tags[tag.Key] = tag.Value;
                    }
                }

                batch.Metrics.Add(metricData);
            }

            // Send the batch
            await call.RequestStream.WriteAsync(batch, cancellationToken);
            await call.RequestStream.CompleteAsync();

            // Get response
            var response = await call;
            
            if (response.Success)
            {
                _logger.LogDebug("Metrics streamed successfully");
                return true;
            }
            else
            {
                _logger.LogWarning("Metrics streaming failed: {Message}", response.Message);
                return false;
            }
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "gRPC error during metrics streaming: {Status}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during metrics streaming");
            return false;
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void InitializeGrpcClient()
    {
        try
        {
            var options = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 4 * 1024 * 1024, // 4MB
                MaxSendMessageSize = 4 * 1024 * 1024,    // 4MB
            };

            if (!_config.UseTls)
            {
                // For development/testing without TLS
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                options.Credentials = ChannelCredentials.Insecure;
            }

            _channel = GrpcChannel.ForAddress(_config.GrpcEndpoint, options);
            _client = new AgentService.AgentServiceClient(_channel);

            _logger.LogInformation("gRPC client initialized for endpoint: {Endpoint}", _config.GrpcEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize gRPC client");
        }
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            var localIp = host.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return localIp?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private static string GetAgentVersion()
    {
        try
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "1.0.0.0";
        }
        catch
        {
            return "1.0.0.0";
        }
    }

    private static long GetTotalMemoryMb()
    {
        try
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            return memoryInfo.TotalAvailableMemoryBytes / (1024 * 1024);
        }
        catch
        {
            return 0;
        }
    }
}