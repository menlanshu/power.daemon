namespace PowerDaemon.Shared.DTOs;

public class AgentHeartbeat
{
    public Guid ServerId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public AgentHealthStatus Status { get; set; }
    public int ServiceCount { get; set; }
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageMb { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum AgentHealthStatus
{
    Healthy,
    Warning,
    Critical,
    Unknown
}