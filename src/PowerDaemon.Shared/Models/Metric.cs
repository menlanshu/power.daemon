using System.ComponentModel.DataAnnotations;

namespace PowerDaemon.Shared.Models;

public class Metric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    public Guid ServerId { get; set; }
    
    public Guid? ServiceId { get; set; }
    
    [Required]
    [StringLength(100)]
    public string MetricType { get; set; } = string.Empty; // CPU, Memory, Disk, Network, Custom
    
    [Required]
    [StringLength(100)]
    public string MetricName { get; set; } = string.Empty;
    
    [Required]
    public double Value { get; set; }
    
    [StringLength(20)]
    public string Unit { get; set; } = string.Empty; // %, MB, KB/s, etc.
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    public string? Tags { get; set; } // JSON string for additional metadata
    
    // Navigation properties
    public virtual Server Server { get; set; } = null!;
    public virtual Service? Service { get; set; }
}

public static class MetricTypes
{
    public const string Cpu = "CPU";
    public const string Memory = "Memory";
    public const string Disk = "Disk";
    public const string Network = "Network";
    public const string Custom = "Custom";
}

public static class MetricNames
{
    // CPU Metrics
    public const string CpuUsagePercent = "cpu_usage_percent";
    
    // Memory Metrics
    public const string MemoryUsageMb = "memory_usage_mb";
    public const string MemoryUsagePercent = "memory_usage_percent";
    
    // Disk Metrics
    public const string DiskReadKbps = "disk_read_kbps";
    public const string DiskWriteKbps = "disk_write_kbps";
    public const string DiskUsagePercent = "disk_usage_percent";
    
    // Network Metrics
    public const string NetworkInKbps = "network_in_kbps";
    public const string NetworkOutKbps = "network_out_kbps";
    
    // Process Metrics
    public const string ThreadCount = "thread_count";
    public const string HandleCount = "handle_count";
}