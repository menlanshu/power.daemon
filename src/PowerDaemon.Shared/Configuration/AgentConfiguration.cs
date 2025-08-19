namespace PowerDaemon.Shared.Configuration;

public class AgentConfiguration
{
    public const string SectionName = "Agent";
    
    public Guid? ServerId { get; set; }
    public string Hostname { get; set; } = Environment.MachineName;
    public string CentralServiceUrl { get; set; } = "https://localhost:5001";
    
    // Metrics Configuration
    public int MetricsCollectionIntervalSeconds { get; set; } = 300; // 5 minutes
    public int MetricsRetentionDays { get; set; } = 30;
    public int MetricsBatchSize { get; set; } = 100;
    
    // Heartbeat Configuration
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int HeartbeatTimeoutSeconds { get; set; } = 90;
    
    // Service Discovery Configuration
    public bool EnableServiceDiscovery { get; set; } = true;
    public int ServiceDiscoveryIntervalSeconds { get; set; } = 600; // 10 minutes
    public List<string> ServiceDiscoveryFilters { get; set; } = new();
    
    // gRPC Configuration
    public string GrpcEndpoint { get; set; } = "https://localhost:5001";
    public bool UseTls { get; set; } = true;
    public string? ClientCertificatePath { get; set; }
    public string? ClientCertificatePassword { get; set; }
    
    // Logging Configuration
    public string LogLevel { get; set; } = "Information";
    public string LogPath { get; set; } = "logs";
    public int LogRetentionDays { get; set; } = 7;
}