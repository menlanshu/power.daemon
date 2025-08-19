using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PowerDaemon.Shared.DTOs;
using PowerDaemon.Shared.Models;

namespace PowerDaemon.Agent.Services;

[SupportedOSPlatform("linux")]
public class LinuxServiceDiscovery
{
    private readonly ILogger<LinuxServiceDiscovery> _logger;
    private static readonly Regex ServiceNameRegex = new(@"^[a-zA-Z0-9_\-\.@]+\.service$", RegexOptions.Compiled);

    public LinuxServiceDiscovery(ILogger<LinuxServiceDiscovery> logger)
    {
        _logger = logger;
    }

    public async Task<List<ServiceInfo>> DiscoverServicesAsync(CancellationToken cancellationToken = default)
    {
        var services = new List<ServiceInfo>();

        try
        {
            _logger.LogInformation("Starting Linux systemd service discovery");

            // Get all systemd services
            var allServices = await GetSystemdServicesAsync(cancellationToken);
            
            foreach (var serviceName in allServices)
            {
                try
                {
                    var serviceInfo = await GetServiceInfoAsync(serviceName, cancellationToken);
                    if (serviceInfo != null && await IsCSharpServiceAsync(serviceInfo, cancellationToken))
                    {
                        services.Add(serviceInfo);
                        _logger.LogDebug("Discovered C# service: {ServiceName}", serviceInfo.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing service {ServiceName}", serviceName);
                }
            }

            _logger.LogInformation("Discovered {ServiceCount} C# services on Linux", services.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Linux service discovery");
        }

        return services;
    }

    private async Task<List<string>> GetSystemdServicesAsync(CancellationToken cancellationToken)
    {
        var services = new List<string>();

        try
        {
            // Use systemctl to list all services
            var result = await RunCommandAsync("systemctl", "list-unit-files --type=service --no-pager --no-legend", cancellationToken);
            
            if (result.ExitCode == 0)
            {
                var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1 && ServiceNameRegex.IsMatch(parts[0]))
                    {
                        services.Add(parts[0]);
                    }
                }
            }
            else
            {
                _logger.LogWarning("Failed to list systemd services: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting systemd services list");
        }

        return services;
    }

    private async Task<ServiceInfo?> GetServiceInfoAsync(string serviceName, CancellationToken cancellationToken)
    {
        try
        {
            var serviceInfo = new ServiceInfo
            {
                Name = serviceName,
                DisplayName = serviceName
            };

            // Get service status
            await EnrichServiceWithStatusAsync(serviceInfo, cancellationToken);

            // Get service unit file information
            await EnrichServiceWithUnitFileAsync(serviceInfo, cancellationToken);

            // Get process information if service is running
            if (serviceInfo.Status == ServiceStatus.Running)
            {
                await EnrichServiceWithProcessAsync(serviceInfo, cancellationToken);
            }

            return serviceInfo;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get info for service {ServiceName}", serviceName);
            return null;
        }
    }

    private async Task EnrichServiceWithStatusAsync(ServiceInfo serviceInfo, CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunCommandAsync("systemctl", $"is-active {serviceInfo.Name}", cancellationToken);
            serviceInfo.Status = MapServiceStatus(result.Output.Trim());

            // Get additional status information
            result = await RunCommandAsync("systemctl", $"show {serviceInfo.Name} --property=MainPID,ExecStart,Description,Type,User", cancellationToken);
            
            if (result.ExitCode == 0)
            {
                var properties = ParseSystemctlShowOutput(result.Output);
                
                if (properties.TryGetValue("MainPID", out var pidStr) && int.TryParse(pidStr, out var pid) && pid > 0)
                {
                    serviceInfo.ProcessId = pid;
                }

                if (properties.TryGetValue("ExecStart", out var execStart))
                {
                    serviceInfo.ExecutablePath = ExtractExecutableFromExecStart(execStart);
                }

                if (properties.TryGetValue("Description", out var description))
                {
                    serviceInfo.Description = description;
                }

                if (properties.TryGetValue("User", out var user))
                {
                    serviceInfo.ServiceAccount = user;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get status for service {ServiceName}", serviceInfo.Name);
        }
    }

    private async Task EnrichServiceWithUnitFileAsync(ServiceInfo serviceInfo, CancellationToken cancellationToken)
    {
        try
        {
            // Get unit file path
            var result = await RunCommandAsync("systemctl", $"show {serviceInfo.Name} --property=FragmentPath", cancellationToken);
            
            if (result.ExitCode == 0)
            {
                var properties = ParseSystemctlShowOutput(result.Output);
                if (properties.TryGetValue("FragmentPath", out var unitFilePath) && File.Exists(unitFilePath))
                {
                    var unitFileContent = await File.ReadAllTextAsync(unitFilePath, cancellationToken);
                    
                    // Parse unit file for additional information
                    var workingDirMatch = Regex.Match(unitFileContent, @"WorkingDirectory=(.+)");
                    if (workingDirMatch.Success)
                    {
                        serviceInfo.WorkingDirectory = workingDirMatch.Groups[1].Value.Trim();
                    }

                    // Determine startup type from WantedBy section
                    if (unitFileContent.Contains("WantedBy=multi-user.target") || 
                        unitFileContent.Contains("WantedBy=default.target"))
                    {
                        serviceInfo.StartupType = StartupType.Automatic;
                    }
                }
            }

            // Check if service is enabled
            result = await RunCommandAsync("systemctl", $"is-enabled {serviceInfo.Name}", cancellationToken);
            serviceInfo.StartupType = result.Output.Trim() switch
            {
                "enabled" => StartupType.Automatic,
                "disabled" => StartupType.Disabled,
                _ => StartupType.Manual
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not get unit file info for service {ServiceName}", serviceInfo.Name);
        }
    }

    private async Task EnrichServiceWithProcessAsync(ServiceInfo serviceInfo, CancellationToken cancellationToken)
    {
        try
        {
            if (serviceInfo.ProcessId > 0)
            {
                var process = Process.GetProcessById(serviceInfo.ProcessId.Value);
                if (process != null)
                {
                    // Get process start time
                    try
                    {
                        serviceInfo.LastStartTime = process.StartTime;
                    }
                    catch
                    {
                        // Some processes don't allow access to start time
                    }

                    // Get actual executable path from /proc/pid/exe
                    try
                    {
                        var exeLink = $"/proc/{serviceInfo.ProcessId}/exe";
                        if (File.Exists(exeLink))
                        {
                            var target = await RunCommandAsync("readlink", exeLink, cancellationToken);
                            if (target.ExitCode == 0 && !string.IsNullOrWhiteSpace(target.Output))
                            {
                                serviceInfo.ExecutablePath = target.Output.Trim();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not read executable path for PID {ProcessId}", serviceInfo.ProcessId);
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
    }

    private async Task<bool> IsCSharpServiceAsync(ServiceInfo serviceInfo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(serviceInfo.ExecutablePath))
            return false;

        try
        {
            // Check if it's a dotnet application
            if (serviceInfo.ExecutablePath.Contains("dotnet") || serviceInfo.ExecutablePath.EndsWith(".dll"))
                return true;

            // Check if the executable is a .NET binary
            if (File.Exists(serviceInfo.ExecutablePath))
            {
                var result = await RunCommandAsync("file", serviceInfo.ExecutablePath, cancellationToken);
                if (result.ExitCode == 0 && 
                    (result.Output.Contains(".NET") || result.Output.Contains("Mono")))
                {
                    return true;
                }

                // Check for .NET runtime dependencies
                result = await RunCommandAsync("ldd", serviceInfo.ExecutablePath, cancellationToken);
                if (result.ExitCode == 0 && 
                    (result.Output.Contains("libcoreclr") || result.Output.Contains("libmonosgen")))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not determine if service {ServiceName} is C# service", serviceInfo.Name);
        }

        return false;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(string command, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync(cancellationToken);
            
            var output = await outputTask;
            var error = await errorTask;
            
            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to run command: {Command} {Arguments}", command, arguments);
            return (-1, string.Empty, ex.Message);
        }
    }

    private static Dictionary<string, string> ParseSystemctlShowOutput(string output)
    {
        var properties = new Dictionary<string, string>();
        
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalIndex = line.IndexOf('=');
            if (equalIndex > 0)
            {
                var key = line[..equalIndex];
                var value = line[(equalIndex + 1)..];
                properties[key] = value;
            }
        }
        
        return properties;
    }

    private static string ExtractExecutableFromExecStart(string execStart)
    {
        // ExecStart format: { path=/usr/bin/dotnet ; argv[]=/usr/bin/dotnet /app/MyApp.dll ; ignore_errors=no }
        // Or simple: /usr/bin/myapp
        
        if (execStart.StartsWith("{ path="))
        {
            var match = Regex.Match(execStart, @"path=([^;]+)");
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }
        
        // Simple case - just return the first part before any arguments
        var parts = execStart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : execStart;
    }

    private static ServiceStatus MapServiceStatus(string status) => status switch
    {
        "active" => ServiceStatus.Running,
        "inactive" => ServiceStatus.Stopped,
        "activating" => ServiceStatus.Starting,
        "deactivating" => ServiceStatus.Stopping,
        "failed" => ServiceStatus.Error,
        _ => ServiceStatus.Unknown
    };
}