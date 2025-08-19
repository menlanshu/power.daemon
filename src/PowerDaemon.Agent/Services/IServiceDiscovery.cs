using PowerDaemon.Shared.DTOs;

namespace PowerDaemon.Agent.Services;

public interface IServiceDiscovery
{
    Task<ServiceDiscoveryResult> DiscoverServicesAsync(CancellationToken cancellationToken = default);
}