namespace PowerDaemon.Cache.Configuration;

public class RedisConfiguration
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string InstanceName { get; set; } = "PowerDaemon";
    public int Database { get; set; } = 0;
    public bool EnableSsl { get; set; } = false;
    public string? Password { get; set; }
    public int ConnectTimeout { get; set; } = 5000;
    public int SyncTimeout { get; set; } = 5000;
    public int ConnectRetry { get; set; } = 3;
    public bool AbortOnConnectFail { get; set; } = false;
    
    // Cache TTL settings (in seconds)
    public CacheTtlSettings Ttl { get; set; } = new();
    
    // Key prefixes for different data types
    public CacheKeyPrefixes KeyPrefixes { get; set; } = new();
}

public class CacheTtlSettings
{
    public int ServerStatus { get; set; } = 5;        // 5 seconds
    public int ServiceMetadata { get; set; } = 30;    // 30 seconds  
    public int Metrics { get; set; } = 10;            // 10 seconds
    public int DeploymentStatus { get; set; } = 15;   // 15 seconds
    public int UserSession { get; set; } = 3600;      // 1 hour
    public int AuthToken { get; set; } = 1800;        // 30 minutes
    public int SystemHealth { get; set; } = 60;       // 1 minute
    public int DefaultExpiry { get; set; } = 300;     // 5 minutes
}

public class CacheKeyPrefixes
{
    public string Server { get; set; } = "server";
    public string Service { get; set; } = "service";
    public string Metrics { get; set; } = "metrics";
    public string Deployment { get; set; } = "deployment";
    public string User { get; set; } = "user";
    public string Session { get; set; } = "session";
    public string Lock { get; set; } = "lock";
    public string Health { get; set; } = "health";
}