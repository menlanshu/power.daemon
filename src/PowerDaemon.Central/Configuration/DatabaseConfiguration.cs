namespace PowerDaemon.Central.Configuration;

public class DatabaseConfiguration
{
    public const string SectionName = "Database";
    
    public string Provider { get; set; } = "PostgreSQL"; // PostgreSQL or Oracle
    public string ConnectionString { get; set; } = string.Empty;
    public bool EnableSensitiveDataLogging { get; set; } = false;
    public bool EnableDetailedErrors { get; set; } = false;
    
    // Connection pooling settings
    public int MaxPoolSize { get; set; } = 100;
    public int MinPoolSize { get; set; } = 5;
    public int ConnectionTimeout { get; set; } = 30;
    public int CommandTimeout { get; set; } = 300; // 5 minutes for long operations
    
    // Migration settings
    public bool AutoMigrateOnStartup { get; set; } = false;
    public string MigrationsAssembly { get; set; } = "PowerDaemon.Central";
}