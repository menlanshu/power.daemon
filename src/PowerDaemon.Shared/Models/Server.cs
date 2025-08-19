using System.ComponentModel.DataAnnotations;

namespace PowerDaemon.Shared.Models;

public class Server
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [StringLength(255)]
    public string Hostname { get; set; } = string.Empty;
    
    [Required]
    public string IpAddress { get; set; } = string.Empty;
    
    [Required]
    [StringLength(20)]
    public OsType OsType { get; set; }
    
    [StringLength(100)]
    public string? OsVersion { get; set; }
    
    [StringLength(50)]
    public string? AgentVersion { get; set; }
    
    public AgentStatus AgentStatus { get; set; } = AgentStatus.Unknown;
    
    public DateTime? LastHeartbeat { get; set; }
    
    public int? CpuCores { get; set; }
    
    public int? TotalMemoryMb { get; set; }
    
    [StringLength(255)]
    public string? Location { get; set; }
    
    [StringLength(50)]
    public string Environment { get; set; } = "Production";
    
    public string? ConnectionString { get; set; }
    
    public string? Tags { get; set; } // JSON string
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public virtual ICollection<Service> Services { get; set; } = new List<Service>();
}

public enum OsType
{
    Windows,
    Linux
}

public enum AgentStatus
{
    Connected,
    Disconnected,
    Unknown,
    Error
}