namespace PowerDaemon.Cache.Services;

public interface ICacheService
{
    // Basic cache operations
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class;
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
    Task<long> DeleteByPatternAsync(string pattern, CancellationToken cancellationToken = default);
    
    // Batch operations
    Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class;
    Task<bool> SetManyAsync<T>(Dictionary<string, T> keyValuePairs, TimeSpan? expiry = null, CancellationToken cancellationToken = default) where T : class;
    
    // Hash operations for complex objects
    Task<T?> HashGetAsync<T>(string hashKey, string field, CancellationToken cancellationToken = default) where T : class;
    Task<bool> HashSetAsync<T>(string hashKey, string field, T value, CancellationToken cancellationToken = default) where T : class;
    Task<Dictionary<string, T?>> HashGetAllAsync<T>(string hashKey, CancellationToken cancellationToken = default) where T : class;
    Task<bool> HashDeleteAsync(string hashKey, string field, CancellationToken cancellationToken = default);
    
    // List operations for queues
    Task<long> ListPushAsync<T>(string listKey, T value, bool leftSide = true, CancellationToken cancellationToken = default) where T : class;
    Task<T?> ListPopAsync<T>(string listKey, bool leftSide = true, CancellationToken cancellationToken = default) where T : class;
    Task<long> ListLengthAsync(string listKey, CancellationToken cancellationToken = default);
    
    // Set operations for unique collections
    Task<bool> SetAddAsync<T>(string setKey, T value, CancellationToken cancellationToken = default) where T : class;
    Task<bool> SetRemoveAsync<T>(string setKey, T value, CancellationToken cancellationToken = default) where T : class;
    Task<bool> SetContainsAsync<T>(string setKey, T value, CancellationToken cancellationToken = default) where T : class;
    Task<T[]> SetMembersAsync<T>(string setKey, CancellationToken cancellationToken = default) where T : class;
    
    // Expiration management
    Task<bool> ExpireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default);
    Task<TimeSpan?> TimeToLiveAsync(string key, CancellationToken cancellationToken = default);
    
    // Distributed locking
    Task<IDistributedLock?> AcquireLockAsync(string lockKey, TimeSpan expiry, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    
    // Cache invalidation
    Task InvalidateTagAsync(string tag, CancellationToken cancellationToken = default);
    Task InvalidatePatternAsync(string pattern, CancellationToken cancellationToken = default);
    
    // Health and stats
    Task<CacheHealthInfo> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

public interface IDistributedLock : IDisposable
{
    string LockKey { get; }
    DateTime AcquiredAt { get; }
    TimeSpan Expiry { get; }
    Task<bool> ExtendAsync(TimeSpan additionalTime, CancellationToken cancellationToken = default);
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}

public class CacheHealthInfo
{
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
    public TimeSpan ResponseTime { get; set; }
    public long UsedMemory { get; set; }
    public long MaxMemory { get; set; }
    public int ConnectedClients { get; set; }
    public Dictionary<string, object> Details { get; set; } = new();
}

public class CacheStatistics
{
    public long TotalConnections { get; set; }
    public long CommandsProcessed { get; set; }
    public double HitRate { get; set; }
    public long KeyspaceHits { get; set; }
    public long KeyspaceMisses { get; set; }
    public long EvictedKeys { get; set; }
    public long ExpiredKeys { get; set; }
    public Dictionary<string, long> KeysByDatabase { get; set; } = new();
}