using PowerDaemon.Shared.Models;

namespace PowerDaemon.Shared.DTOs;

public class ServiceInfoDto
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public ServiceStatus Status { get; set; }
    public int? ProcessId { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public StartupType StartupType { get; set; }
    public string? ServiceAccount { get; set; }
    public DateTime? LastStartTime { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ServiceDiscoveryResult
{
    public Guid ServerId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public List<ServiceInfoDto> Services { get; set; } = new();
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}