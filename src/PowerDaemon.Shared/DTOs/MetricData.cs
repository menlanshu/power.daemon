namespace PowerDaemon.Shared.DTOs;

public class MetricData
{
    public Guid ServerId { get; set; }
    public Guid? ServiceId { get; set; }
    public string MetricType { get; set; } = string.Empty;
    public string MetricName { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string>? Tags { get; set; }
}

public class MetricBatch
{
    public Guid ServerId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public List<MetricData> Metrics { get; set; } = new();
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}