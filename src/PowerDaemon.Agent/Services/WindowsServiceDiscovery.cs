using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using PowerDaemon.Shared.DTOs;
using PowerDaemon.Shared.Models;

namespace PowerDaemon.Agent.Services;

[SupportedOSPlatform("windows")]
public class WindowsServiceDiscovery
{
    private readonly ILogger<WindowsServiceDiscovery> _logger;

    public WindowsServiceDiscovery(ILogger<WindowsServiceDiscovery> logger)
    {
        _logger = logger;
    }

    public async Task<List<ServiceInfoDto>> DiscoverServicesAsync(CancellationToken cancellationToken = default)
    {
        var services = new List<ServiceInfoDto>();

        try
        {
            _logger.LogInformation("Starting Windows service discovery");

            // Get all Windows services
            var windowsServices = ServiceController.GetServices();
            
            foreach (var service in windowsServices)
            {
                try
                {
                    // Check if this is potentially a C# service by examining the executable path
                    var serviceInfo = await GetServiceInfoAsync(service, cancellationToken);
                    if (serviceInfo != null && IsCSharpService(serviceInfo))
                    {
                        services.Add(serviceInfo);
                        _logger.LogDebug("Discovered C# service: {ServiceName}", serviceInfo.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing service {ServiceName}", service.ServiceName);
                }
                finally
                {
                    service.Dispose();
                }
            }

            _logger.LogInformation("Discovered {ServiceCount} C# services on Windows", services.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Windows service discovery");
        }

        return services;
    }

    private async Task<ServiceInfoDto?> GetServiceInfoAsync(ServiceController service, CancellationToken cancellationToken)
    {
        try
        {
            var serviceInfo = new ServiceInfoDto
            {
                Name = service.ServiceName,
                DisplayName = service.DisplayName,
                Status = MapServiceStatus(service.Status)
            };

            // Get additional service details using WMI
            await EnrichServiceInfoWithWmiAsync(serviceInfo, cancellationToken);

            // Get process information if service is running
            if (service.Status == ServiceControllerStatus.Running)
            {
                await EnrichServiceInfoWithProcessAsync(serviceInfo, cancellationToken);
            }

            return serviceInfo;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get info for service {ServiceName}", service.ServiceName);
            return null;
        }
    }

    private async Task EnrichServiceInfoWithWmiAsync(ServiceInfoDto serviceInfo, CancellationToken cancellationToken)
    {
        try
        {
            var query = $"SELECT * FROM Win32_Service WHERE Name = '{serviceInfo.Name.Replace("'", "''")}'";
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();

            foreach (ManagementObject service in results)
            {
                serviceInfo.ExecutablePath = service["PathName"]?.ToString() ?? string.Empty;
                serviceInfo.ServiceAccount = service["StartName"]?.ToString();
                serviceInfo.Description = service["Description"]?.ToString();
                serviceInfo.StartupType = MapStartupType(service["StartMode"]?.ToString());
                serviceInfo.ProcessId = Convert.ToInt32(service["ProcessId"] ?? 0);
                break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get WMI info for service {ServiceName}", serviceInfo.Name);
        }

        await Task.CompletedTask;
    }

    private async Task EnrichServiceInfoWithProcessAsync(ServiceInfoDto serviceInfo, CancellationToken cancellationToken)
    {
        try
        {
            if (serviceInfo.ProcessId > 0)
            {
                var process = Process.GetProcessById(serviceInfo.ProcessId.Value);
                if (process != null)
                {
                    serviceInfo.WorkingDirectory = process.StartInfo.WorkingDirectory;
                    
                    // Try to get the actual executable path from the process
                    try
                    {
                        serviceInfo.ExecutablePath = process.MainModule?.FileName ?? serviceInfo.ExecutablePath;
                    }
                    catch
                    {
                        // Access denied or process not accessible
                    }

                    // Check if process started recently
                    try
                    {
                        serviceInfo.LastStartTime = process.StartTime;
                    }
                    catch
                    {
                        // Some system services don't allow access to start time
                    }
                }
            }
        }
        catch (ArgumentException)
        {
            // Process not found
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get process info for service {ServiceName}", serviceInfo.Name);
        }

        await Task.CompletedTask;
    }

    private static bool IsCSharpService(ServiceInfoDto serviceInfo)
    {
        if (string.IsNullOrEmpty(serviceInfo.ExecutablePath))
            return false;

        var executablePath = serviceInfo.ExecutablePath.ToLowerInvariant();

        // Check for .NET executable indicators
        return executablePath.EndsWith(".exe") && (
            executablePath.Contains("dotnet") ||
            executablePath.Contains(".net") ||
            IsLikelyDotNetExecutable(serviceInfo.ExecutablePath)
        );
    }

    private static bool IsLikelyDotNetExecutable(string executablePath)
    {
        try
        {
            if (!File.Exists(executablePath))
                return false;

            // Check if the file is a .NET assembly by looking for PE header and .NET metadata
            using var stream = new FileStream(executablePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);

            // Check PE signature
            stream.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();
            stream.Seek(peOffset, SeekOrigin.Begin);
            var peSignature = reader.ReadInt32();

            if (peSignature != 0x00004550) // "PE\0\0"
                return false;

            // Look for .NET CLI header
            stream.Seek(peOffset + 0x18, SeekOrigin.Begin); // Optional header
            var magic = reader.ReadInt16();
            
            int cliHeaderRva = 0;
            if (magic == 0x010b) // PE32
            {
                stream.Seek(peOffset + 0x88, SeekOrigin.Begin);
                cliHeaderRva = reader.ReadInt32();
            }
            else if (magic == 0x020b) // PE32+
            {
                stream.Seek(peOffset + 0x98, SeekOrigin.Begin);
                cliHeaderRva = reader.ReadInt32();
            }

            return cliHeaderRva != 0; // Has .NET metadata
        }
        catch
        {
            return false; // If we can't read it, assume it's not .NET
        }
    }

    private static ServiceStatus MapServiceStatus(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => ServiceStatus.Running,
        ServiceControllerStatus.Stopped => ServiceStatus.Stopped,
        ServiceControllerStatus.StartPending => ServiceStatus.Starting,
        ServiceControllerStatus.StopPending => ServiceStatus.Stopping,
        ServiceControllerStatus.Paused => ServiceStatus.Stopped,
        ServiceControllerStatus.PausePending => ServiceStatus.Stopping,
        ServiceControllerStatus.ContinuePending => ServiceStatus.Starting,
        _ => ServiceStatus.Unknown
    };

    private static StartupType MapStartupType(string? startMode) => startMode?.ToLowerInvariant() switch
    {
        "auto" => StartupType.Automatic,
        "manual" => StartupType.Manual,
        "disabled" => StartupType.Disabled,
        _ => StartupType.Manual
    };
}