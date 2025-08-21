namespace PowerDaemon.Messaging.Configuration;

public class RabbitMQConfiguration
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public bool EnableSsl { get; set; } = false;
    public int RequestedHeartbeat { get; set; } = 60;
    public int NetworkRecoveryInterval { get; set; } = 10;
    public bool AutomaticRecoveryEnabled { get; set; } = true;
    public string ExchangeName { get; set; } = "powerdaemon";
    public string DeploymentQueue { get; set; } = "powerdaemon.deployments";
    public string CommandQueue { get; set; } = "powerdaemon.commands";
    public string StatusQueue { get; set; } = "powerdaemon.status";
    public string DeadLetterExchange { get; set; } = "powerdaemon.dlx";
    public int MessageTtlSeconds { get; set; } = 3600; // 1 hour
    public int MaxRetryAttempts { get; set; } = 3;
    
    // Production scale optimization settings for 200+ servers
    public ProductionScaleSettings ProductionScale { get; set; } = new();
}

public class ProductionScaleSettings
{
    // Connection pooling for high concurrency
    public int MaxConnectionPoolSize { get; set; } = 50;
    public int MinConnectionPoolSize { get; set; } = 10;
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int ChannelPoolSize { get; set; } = 100;
    
    // Message processing optimization
    public int PrefetchCount { get; set; } = 50; // Number of unacknowledged messages per consumer
    public int BatchSize { get; set; } = 100; // Batch size for bulk operations
    public int ConsumerThreadCount { get; set; } = 10; // Parallel consumers per queue
    
    // Queue optimization for high throughput
    public bool EnableMessageBatching { get; set; } = true;
    public int BatchTimeoutMs { get; set; } = 100;
    public int MaxMessageSize { get; set; } = 1048576; // 1MB
    
    // Load balancing and failover
    public List<string> ClusterHosts { get; set; } = new();
    public bool EnableLoadBalancing { get; set; } = true;
    public int FailoverTimeoutSeconds { get; set; } = 30;
    
    // Performance monitoring
    public bool EnableMetrics { get; set; } = true;
    public int MetricsIntervalSeconds { get; set; } = 30;
    
    // Memory and resource optimization
    public int MaxMemoryMB { get; set; } = 2048;
    public bool EnableCompression { get; set; } = true;
    public bool EnablePersistence { get; set; } = true;
    
    // Rate limiting and throttling
    public int MaxMessagesPerSecond { get; set; } = 1000;
    public int MaxConcurrentOperations { get; set; } = 200;
}