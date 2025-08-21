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
    
    // Production scale optimization settings for 200+ servers
    public CacheProductionSettings ProductionScale { get; set; } = new();
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

public class CacheProductionSettings
{
    // Connection pooling and multiplexing for high concurrency
    public int MaxConnectionPoolSize { get; set; } = 100;
    public int MinConnectionPoolSize { get; set; } = 20;
    public int ConnectionMultiplexerPoolSize { get; set; } = 10;
    public bool EnableConnectionPooling { get; set; } = true;
    
    // Performance optimization for 200+ servers
    public int MaxConcurrentOperations { get; set; } = 500;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int OperationTimeoutSeconds { get; set; } = 10;
    public bool EnablePipelining { get; set; } = true;
    public int PipelineSize { get; set; } = 100;
    
    // Memory and storage optimization
    public int MaxMemoryUsageMB { get; set; } = 4096; // 4GB
    public string EvictionPolicy { get; set; } = "allkeys-lru";
    public bool EnableCompression { get; set; } = true;
    public int CompressionThreshold { get; set; } = 1024; // Compress values > 1KB
    
    // Clustering and high availability
    public List<string> ClusterEndpoints { get; set; } = new();
    public bool EnableClustering { get; set; } = false;
    public bool EnableReplication { get; set; } = true;
    public int ReplicationFactor { get; set; } = 2;
    
    // Sharding for horizontal scaling
    public bool EnableSharding { get; set; } = true;
    public int ShardCount { get; set; } = 8;
    public string ShardingStrategy { get; set; } = "consistent-hash";
    
    // Monitoring and metrics
    public bool EnableMetrics { get; set; } = true;
    public int MetricsIntervalSeconds { get; set; } = 30;
    public bool EnableSlowLogMonitoring { get; set; } = true;
    public int SlowLogThresholdMs { get; set; } = 100;
    
    // Backup and persistence optimization
    public bool EnablePersistence { get; set; } = true;
    public string PersistenceStrategy { get; set; } = "rdb"; // rdb, aof, or both
    public int BackupIntervalSeconds { get; set; } = 3600; // 1 hour
    
    // Rate limiting and throttling
    public int MaxRequestsPerSecond { get; set; } = 10000;
    public int MaxBatchSize { get; set; } = 1000;
    public bool EnableThrottling { get; set; } = true;
    
    // Cache warming and preloading
    public bool EnableCacheWarming { get; set; } = true;
    public List<string> PreloadKeys { get; set; } = new();
    public int WarmupTimeoutSeconds { get; set; } = 300;
}