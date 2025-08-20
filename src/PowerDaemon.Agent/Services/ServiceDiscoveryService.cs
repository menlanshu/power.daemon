using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerDaemon.Shared.Configuration;
using PowerDaemon.Shared.DTOs;

namespace PowerDaemon.Agent.Services;

public class ServiceDiscoveryService : IServiceDiscovery
{
    private readonly ILogger<ServiceDiscoveryService> _logger;
    private readonly AgentConfiguration _config;
    private readonly WindowsServiceDiscovery? _windowsDiscovery;
    private readonly LinuxServiceDiscovery? _linuxDiscovery;

    public ServiceDiscoveryService(
        ILogger<ServiceDiscoveryService> logger,
        ILoggerFactory loggerFactory,
        IOptions<AgentConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;

        // Initialize platform-specific discovery services
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _windowsDiscovery = new WindowsServiceDiscovery(
                loggerFactory.CreateLogger<WindowsServiceDiscovery>());
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _linuxDiscovery = new LinuxServiceDiscovery(
                loggerFactory.CreateLogger<LinuxServiceDiscovery>());
        }
    }

    public async Task<ServiceDiscoveryResult> DiscoverServicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting cross-platform service discovery on {OS}", 
            RuntimeInformation.OSDescription);

        var services = new List<ServiceInfoDto>();

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _windowsDiscovery != null)
            {
                services.AddRange(await _windowsDiscovery.DiscoverServicesAsync(cancellationToken));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _linuxDiscovery != null)
            {
                services.AddRange(await _linuxDiscovery.DiscoverServicesAsync(cancellationToken));
            }
            else
            {
                _logger.LogWarning("Service discovery not implemented for platform: {Platform}", 
                    RuntimeInformation.OSDescription);
            }

            // Apply filters if configured
            if (_config.ServiceDiscoveryFilters.Any())
            {
                services = ApplyFilters(services);
            }

            _logger.LogInformation("Service discovery completed. Found {ServiceCount} C# services", 
                services.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during service discovery");
        }

        return new ServiceDiscoveryResult
        {
            ServerId = _config.ServerId ?? Guid.NewGuid(),
            Hostname = _config.Hostname,
            Services = services,
            DiscoveredAt = DateTime.UtcNow
        };
    }

    private List<ServiceInfoDto> ApplyFilters(List<ServiceInfoDto> services)
    {
        var filteredServices = new List<ServiceInfoDto>();

        foreach (var service in services)
        {
            var shouldInclude = false;

            foreach (var filter in _config.ServiceDiscoveryFilters)
            {
                if (string.IsNullOrWhiteSpace(filter))
                    continue;

                // Support wildcards and regex-like patterns
                if (filter.Contains('*'))
                {
                    var pattern = "^" + filter.Replace("*", ".*") + "$";
                    if (System.Text.RegularExpressions.Regex.IsMatch(service.Name, pattern, 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        shouldInclude = true;
                        break;
                    }
                }
                else if (service.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                         (service.DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true))
                {
                    shouldInclude = true;
                    break;
                }
            }

            if (shouldInclude)
            {
                filteredServices.Add(service);
            }
        }

        _logger.LogInformation("Applied filters, reduced from {OriginalCount} to {FilteredCount} services",
            services.Count, filteredServices.Count);

        return filteredServices;
    }
}