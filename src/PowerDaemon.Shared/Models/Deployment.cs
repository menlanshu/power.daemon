using System.ComponentModel.DataAnnotations;

namespace PowerDaemon.Shared.Models;

public class Deployment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    public Guid ServiceId { get; set; }
    
    [Required]
    [StringLength(50)]
    public string Version { get; set; } = string.Empty;
    
    [Required]
    public string PackagePath { get; set; } = string.Empty;
    
    public decimal? PackageSizeMb { get; set; }
    
    [StringLength(64)]
    public string? PackageChecksum { get; set; } // SHA256
    
    public DeploymentStatus Status { get; set; } = DeploymentStatus.Pending;
    
    public DeploymentStrategy DeploymentStrategy { get; set; } = DeploymentStrategy.Immediate;
    
    [StringLength(255)]
    public string? DeployedBy { get; set; } // AD username
    
    public string? DeploymentNotes { get; set; }
    
    public DateTime? StartedAt { get; set; }
    
    public DateTime? CompletedAt { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    [StringLength(50)]
    public string? PreviousVersion { get; set; }
    
    public Guid? RollbackDeploymentId { get; set; }
    
    public string? ConfigurationChanges { get; set; } // JSON string
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public virtual Service Service { get; set; } = null!;
    public virtual Deployment? RollbackDeployment { get; set; }
}

public enum DeploymentStatus
{
    Pending,
    InProgress,
    Success,
    Failed,
    RolledBack
}

public enum DeploymentStrategy
{
    Immediate,
    Rolling,
    BlueGreen,
    Canary
}