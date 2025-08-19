using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using PowerDaemon.Central.Data;
using PowerDaemon.Protos;
using PowerDaemon.Shared.Models;
using Google.Protobuf.WellKnownTypes;

namespace PowerDaemon.Central.Services;

public class AgentServiceImplementation : AgentService.AgentServiceBase
{
    private readonly ILogger<AgentServiceImplementation> _logger;
    private readonly PowerDaemonContext _context;

    public AgentServiceImplementation(
        ILogger<AgentServiceImplementation> logger,
        PowerDaemonContext context)
    {
        _logger = logger;
        _context = context;
    }

    public override async Task<RegistrationResponse> RegisterAgent(
        AgentRegistration request, 
        ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Agent registration request from {Hostname} ({OsType})", 
                request.Hostname, request.OsType);

            // Check if server already exists by hostname
            var existingServer = await _context.Servers
                .FirstOrDefaultAsync(s => s.Hostname == request.Hostname, context.CancellationToken);

            var server = existingServer ?? new Server();
            
            // Update server information
            server.Hostname = request.Hostname;
            server.IpAddress = request.IpAddress;
            server.OsType = System.Enum.Parse<OsType>(request.OsType);
            server.OsVersion = request.OsVersion;
            server.AgentVersion = request.AgentVersion;
            server.CpuCores = request.CpuCores;
            server.TotalMemoryMb = (int)request.TotalMemoryMb;
            server.Location = request.Location;
            server.Environment = request.Environment;
            server.AgentStatus = AgentStatus.Connected;
            server.LastHeartbeat = DateTime.UtcNow;
            server.UpdatedAt = DateTime.UtcNow;
            server.IsActive = true;

            // Parse tags if provided
            if (request.Tags.Count > 0)
            {
                server.Tags = System.Text.Json.JsonSerializer.Serialize(request.Tags.ToDictionary(t => t.Key, t => t.Value));
            }

            if (existingServer == null)
            {
                server.CreatedAt = DateTime.UtcNow;
                _context.Servers.Add(server);
                _logger.LogInformation("Registering new server: {Hostname}", request.Hostname);
            }
            else
            {
                _logger.LogInformation("Updating existing server: {Hostname}", request.Hostname);
            }

            await _context.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation("Agent registered successfully with ID: {ServerId}", server.Id);

            return new RegistrationResponse
            {
                Success = true,
                ServerId = server.Id.ToString(),
                Message = "Agent registered successfully",
                Configuration = new AgentSettings
                {
                    MetricsCollectionIntervalSeconds = 300, // 5 minutes
                    HeartbeatIntervalSeconds = 30,
                    ServiceDiscoveryIntervalSeconds = 600 // 10 minutes
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during agent registration for {Hostname}", request.Hostname);
            return new RegistrationResponse
            {
                Success = false,
                Message = $"Registration failed: {ex.Message}"
            };
        }
    }

    public override async Task<HeartbeatResponse> SendHeartbeat(
        HeartbeatRequest request, 
        ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.ServerId, out var serverId))
            {
                return new HeartbeatResponse
                {
                    Success = false,
                    Message = "Invalid server ID"
                };
            }

            _logger.LogDebug("Heartbeat received from server {ServerId} ({Hostname})", 
                serverId, request.Hostname);

            var server = await _context.Servers
                .FirstOrDefaultAsync(s => s.Id == serverId, context.CancellationToken);

            if (server == null)
            {
                _logger.LogWarning("Heartbeat from unknown server {ServerId}", serverId);
                return new HeartbeatResponse
                {
                    Success = false,
                    Message = "Server not registered"
                };
            }

            // Update server status
            server.AgentStatus = System.Enum.TryParse<AgentStatus>(request.AgentStatus, out var status) ? status : AgentStatus.Unknown;
            server.LastHeartbeat = DateTime.UtcNow;
            server.UpdatedAt = DateTime.UtcNow;

            // Store heartbeat data if needed (could be separate table for history)
            // For now, just update the server record

            await _context.SaveChangesAsync(context.CancellationToken);

            // Check for pending commands (placeholder for future implementation)
            var response = new HeartbeatResponse
            {
                Success = true,
                Message = "Heartbeat received"
            };

            // TODO: Add logic to fetch pending commands for this agent
            // response.PendingCommands.Add(...);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat from server {ServerId}", request.ServerId);
            return new HeartbeatResponse
            {
                Success = false,
                Message = $"Heartbeat processing failed: {ex.Message}"
            };
        }
    }

    public override async Task<ServiceDiscoveryResponse> ReportServices(
        ServiceDiscovery request, 
        ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.ServerId, out var serverId))
            {
                return new ServiceDiscoveryResponse
                {
                    Success = false,
                    Message = "Invalid server ID"
                };
            }

            _logger.LogInformation("Service discovery report from server {ServerId} with {ServiceCount} services", 
                serverId, request.Services.Count);

            var server = await _context.Servers
                .Include(s => s.Services)
                .FirstOrDefaultAsync(s => s.Id == serverId, context.CancellationToken);

            if (server == null)
            {
                return new ServiceDiscoveryResponse
                {
                    Success = false,
                    Message = "Server not registered"
                };
            }

            // Get existing services for this server
            var existingServices = server.Services.ToList();
            var reportedServiceNames = request.Services.Select(s => s.Name).ToHashSet();

            // Mark services that are no longer reported as inactive
            foreach (var existingService in existingServices)
            {
                if (!reportedServiceNames.Contains(existingService.Name))
                {
                    existingService.IsActive = false;
                    existingService.Status = ServiceStatus.Unknown;
                    existingService.UpdatedAt = DateTime.UtcNow;
                }
            }

            // Process reported services
            foreach (var reportedService in request.Services)
            {
                var existingService = existingServices.FirstOrDefault(s => s.Name == reportedService.Name);
                
                if (existingService != null)
                {
                    // Update existing service
                    UpdateServiceFromReport(existingService, reportedService);
                }
                else
                {
                    // Create new service
                    var newService = CreateServiceFromReport(serverId, reportedService);
                    _context.Services.Add(newService);
                }
            }

            await _context.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation("Service discovery processed successfully for server {ServerId}", serverId);

            return new ServiceDiscoveryResponse
            {
                Success = true,
                Message = $"Processed {request.Services.Count} services"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing service discovery from server {ServerId}", request.ServerId);
            return new ServiceDiscoveryResponse
            {
                Success = false,
                Message = $"Service discovery processing failed: {ex.Message}"
            };
        }
    }

    public override async Task<MetricsResponse> StreamMetrics(
        IAsyncStreamReader<MetricsBatch> requestStream, 
        ServerCallContext context)
    {
        try
        {
            var totalMetrics = 0;
            var processedBatches = 0;

            await foreach (var batch in requestStream.ReadAllAsync(context.CancellationToken))
            {
                if (!Guid.TryParse(batch.ServerId, out var serverId))
                {
                    _logger.LogWarning("Invalid server ID in metrics batch: {ServerId}", batch.ServerId);
                    continue;
                }

                _logger.LogDebug("Received metrics batch from server {ServerId} with {MetricCount} metrics", 
                    serverId, batch.Metrics.Count);

                // Convert and store metrics
                var metrics = new List<Metric>();
                foreach (var metricData in batch.Metrics)
                {
                    var metric = new Metric
                    {
                        ServerId = serverId,
                        ServiceId = !string.IsNullOrEmpty(metricData.ServiceId) && Guid.TryParse(metricData.ServiceId, out var svcId) ? svcId : null,
                        MetricType = metricData.MetricType,
                        MetricName = metricData.MetricName,
                        Value = metricData.Value,
                        Unit = metricData.Unit,
                        Timestamp = metricData.Timestamp.ToDateTime(),
                        Tags = metricData.Tags.Count > 0 ? 
                            System.Text.Json.JsonSerializer.Serialize(metricData.Tags.ToDictionary(t => t.Key, t => t.Value)) : 
                            null
                    };

                    metrics.Add(metric);
                }

                // Batch insert metrics for performance
                _context.Metrics.AddRange(metrics);
                await _context.SaveChangesAsync(context.CancellationToken);

                totalMetrics += metrics.Count;
                processedBatches++;

                _logger.LogDebug("Stored {MetricCount} metrics for server {ServerId}", 
                    metrics.Count, serverId);
            }

            _logger.LogInformation("Metrics streaming completed. Processed {BatchCount} batches with {TotalMetrics} total metrics", 
                processedBatches, totalMetrics);

            return new MetricsResponse
            {
                Success = true,
                Message = $"Processed {totalMetrics} metrics from {processedBatches} batches"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing metrics stream");
            return new MetricsResponse
            {
                Success = false,
                Message = $"Metrics processing failed: {ex.Message}"
            };
        }
    }

    public override async Task<CommandResult> ExecuteServiceCommand(
        ServiceCommand request, 
        ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Service command received: {Command} for service {ServiceName} on server {ServerId}", 
                request.Command, request.ServiceName, request.ServerId);

            // TODO: Implement actual command execution
            // This would typically:
            // 1. Validate the command and permissions
            // 2. Queue the command for execution on the target agent
            // 3. Return status or wait for execution result

            await Task.Delay(100, context.CancellationToken); // Placeholder

            return new CommandResult
            {
                CommandId = request.CommandId,
                Success = true,
                Message = $"Command '{request.Command}' queued for execution",
                ExitCode = 0,
                ExecutedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing service command {Command} for service {ServiceName}", 
                request.Command, request.ServiceName);
            
            return new CommandResult
            {
                CommandId = request.CommandId,
                Success = false,
                Message = $"Command execution failed: {ex.Message}",
                ExitCode = -1,
                ExecutedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }

    public override async Task DeployService(
        IAsyncStreamReader<DeploymentPackage> requestStream, 
        IServerStreamWriter<DeploymentProgress> responseStream, 
        ServerCallContext context)
    {
        try
        {
            // TODO: Implement deployment functionality
            // This is a complex bidirectional streaming operation that would:
            // 1. Receive deployment package chunks
            // 2. Assemble the complete package
            // 3. Validate checksums
            // 4. Deploy to target service
            // 5. Stream status updates back to client

            await responseStream.WriteAsync(new DeploymentProgress
            {
                Status = "Pending",
                Message = "Deployment functionality not yet implemented",
                ProgressPercent = 0,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            }, context.CancellationToken);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in service deployment");
            
            await responseStream.WriteAsync(new DeploymentProgress
            {
                Status = "Failed",
                Message = $"Deployment failed: {ex.Message}",
                ProgressPercent = 0,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            }, context.CancellationToken);
        }
    }

    public override async Task<RollbackResult> RollbackService(
        RollbackRequest request, 
        ServerCallContext context)
    {
        try
        {
            _logger.LogInformation("Service rollback requested for {ServiceName} to version {TargetVersion}", 
                request.ServiceName, request.TargetVersion);

            // TODO: Implement rollback functionality
            await Task.Delay(100, context.CancellationToken); // Placeholder

            return new RollbackResult
            {
                Success = true,
                Message = "Rollback functionality not yet implemented",
                PreviousVersion = "unknown",
                CurrentVersion = request.TargetVersion
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during service rollback for {ServiceName}", request.ServiceName);
            
            return new RollbackResult
            {
                Success = false,
                Message = $"Rollback failed: {ex.Message}",
                PreviousVersion = "unknown",
                CurrentVersion = "unknown"
            };
        }
    }

    private static void UpdateServiceFromReport(Service existingService, ServiceInfo reportedService)
    {
        existingService.DisplayName = reportedService.DisplayName;
        existingService.Description = reportedService.Description;
        existingService.Version = reportedService.Version;
        existingService.Status = System.Enum.TryParse<ServiceStatus>(reportedService.Status, out var status) ? status : ServiceStatus.Unknown;
        existingService.ProcessId = reportedService.ProcessId != 0 ? reportedService.ProcessId : null;
        existingService.ExecutablePath = reportedService.ExecutablePath;
        existingService.WorkingDirectory = reportedService.WorkingDirectory;
        existingService.ConfigFilePath = reportedService.ConfigFilePath;
        existingService.StartupType = System.Enum.TryParse<StartupType>(reportedService.StartupType, out var startupType) ? startupType : StartupType.Manual;
        existingService.ServiceAccount = reportedService.ServiceAccount;
        existingService.LastStartTime = reportedService.LastStartTime?.ToDateTime();
        existingService.IsActive = reportedService.IsActive;
        existingService.Port = reportedService.Port != 0 ? reportedService.Port : null;
        existingService.UpdatedAt = DateTime.UtcNow;
    }

    private static Service CreateServiceFromReport(Guid serverId, ServiceInfo reportedService)
    {
        return new Service
        {
            ServerId = serverId,
            Name = reportedService.Name,
            DisplayName = reportedService.DisplayName,
            Description = reportedService.Description,
            Version = reportedService.Version,
            Status = System.Enum.TryParse<ServiceStatus>(reportedService.Status, out var status) ? status : ServiceStatus.Unknown,
            ProcessId = reportedService.ProcessId != 0 ? reportedService.ProcessId : null,
            Port = reportedService.Port != 0 ? reportedService.Port : null,
            ExecutablePath = reportedService.ExecutablePath,
            WorkingDirectory = reportedService.WorkingDirectory,
            ConfigFilePath = reportedService.ConfigFilePath,
            StartupType = System.Enum.TryParse<StartupType>(reportedService.StartupType, out var startupType) ? startupType : StartupType.Manual,
            ServiceAccount = reportedService.ServiceAccount,
            LastStartTime = reportedService.LastStartTime?.ToDateTime(),
            IsActive = reportedService.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}