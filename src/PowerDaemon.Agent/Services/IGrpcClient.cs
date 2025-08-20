using PowerDaemon.Shared.DTOs;
using PowerDaemon.Protos;

namespace PowerDaemon.Agent.Services;

public interface IGrpcClient
{
    Task<bool> RegisterAgentAsync(CancellationToken cancellationToken = default);
    Task<bool> SendHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken = default);
    Task<bool> ReportServicesAsync(ServiceDiscoveryResult services, CancellationToken cancellationToken = default);
    Task<bool> StreamMetricsAsync(MetricBatchDto metrics, CancellationToken cancellationToken = default);
}