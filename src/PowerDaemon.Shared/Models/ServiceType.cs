using System.ComponentModel.DataAnnotations;

namespace PowerDaemon.Shared.Models;

public class ServiceType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty; // TypeA, TypeB, TypeC, TypeD
    
    public string? Description { get; set; }
    
    public string? DeploymentScript { get; set; }
    
    public string? HealthCheckTemplate { get; set; }
    
    [StringLength(20)]
    public string? DefaultPortRange { get; set; } // e.g., "8000-8099"
    
    public string? ConfigurationTemplate { get; set; } // JSON string
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public virtual ICollection<Service> Services { get; set; } = new List<Service>();
}