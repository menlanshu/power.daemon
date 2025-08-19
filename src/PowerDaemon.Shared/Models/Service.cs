using System.ComponentModel.DataAnnotations;

namespace PowerDaemon.Shared.Models;

public class Service
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    public Guid ServerId { get; set; }
    
    public Guid? ServiceTypeId { get; set; }
    
    [Required]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(255)]
    public string? DisplayName { get; set; }
    
    public string? Description { get; set; }
    
    [StringLength(50)]
    public string? Version { get; set; }
    
    public ServiceStatus Status { get; set; } = ServiceStatus.Unknown;
    
    public int? ProcessId { get; set; }
    
    public int? Port { get; set; }
    
    [Required]
    public string ExecutablePath { get; set; } = string.Empty;
    
    public string? WorkingDirectory { get; set; }
    
    public string? ConfigFilePath { get; set; }
    
    public StartupType StartupType { get; set; } = StartupType.Automatic;
    
    [StringLength(255)]
    public string? ServiceAccount { get; set; }
    
    public DateTime? LastStartTime { get; set; }
    
    public string? HealthCheckUrl { get; set; }
    
    public string? Dependencies { get; set; } // JSON string
    
    public string? CustomMetrics { get; set; } // JSON string
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public virtual Server Server { get; set; } = null!;
    public virtual ServiceType? ServiceType { get; set; }
    public virtual ICollection<Deployment> Deployments { get; set; } = new List<Deployment>();
    public virtual ICollection<Metric> Metrics { get; set; } = new List<Metric>();
}

public enum ServiceStatus
{
    Running,
    Stopped,
    Starting,
    Stopping,
    Error,
    Unknown
}

public enum StartupType
{
    Automatic,
    Manual,
    Disabled
}