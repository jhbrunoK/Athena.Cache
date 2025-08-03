---
layout: default
title: Cross-instance Sync
---

# Cross-instance Sync

Ensure cache consistency across multiple application instances with automatic synchronization and conflict resolution strategies.

## Why Cross-instance Sync Matters

In distributed applications, maintaining cache consistency prevents:
- **Stale data** being served from different instances
- **Race conditions** during concurrent updates
- **Data inconsistencies** across the application cluster
- **User experience issues** from inconsistent responses

## Automatic Invalidation Propagation

Cache invalidations automatically propagate across all instances when using Redis.

### Basic Sync Configuration

```csharp
// Program.cs
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions =>
    {
        athenaOptions.Namespace = "SyncedApp";
        athenaOptions.Sync.EnableCrossInstanceSync = true;
        athenaOptions.Sync.SyncChannel = "cache_sync";
        athenaOptions.Sync.ConflictResolution = ConflictResolutionStrategy.LastWriteWins;
    },
    redisOptions =>
    {
        redisOptions.ConnectionString = "localhost:6379";
        redisOptions.DatabaseId = 1;
    });
```

### Invalidation Sync Example

```csharp
// Instance A
[HttpPost]
[CacheInvalidateOn("Products")]
public async Task<ProductDto> CreateProduct([FromBody] CreateProductRequest request)
{
    var product = await _productService.CreateProductAsync(request);
    
    // Invalidation automatically propagates to Instance B, C, etc.
    return product;
}

// Instance B, C, etc. - their caches are automatically cleared
[HttpGet]
[AthenaCache(ExpirationMinutes = 30)]
[CacheInvalidateOn("Products")]
public async Task<ProductDto[]> GetProducts()
{
    return await _productService.GetProductsAsync();
}
```

## Manual Cross-instance Operations

Perform explicit cross-instance cache operations when needed.

### Distributed Cache Manager

```csharp
public interface IDistributedCacheManager
{
    Task InvalidateGloballyAsync(string key);
    Task InvalidateGloballyByPatternAsync(string pattern);
    Task InvalidateGloballyByTableAsync(string table);
    Task SetGloballyAsync<T>(string key, T value, TimeSpan? expiration = null);
    Task<SyncStatus> GetSyncStatusAsync();
    Task ForceGlobalSyncAsync();
}

public class DistributedCacheManager : IDistributedCacheManager
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ICacheService _cache;
    private readonly CrossInstanceSyncOptions _options;
    private readonly ILogger<DistributedCacheManager> _logger;

    public DistributedCacheManager(
        IConnectionMultiplexer redis,
        ICacheService cache,
        IOptions<CrossInstanceSyncOptions> options,
        ILogger<DistributedCacheManager> logger)
    {
        _redis = redis;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvalidateGloballyAsync(string key)
    {
        // Remove from local cache
        await _cache.RemoveAsync(key);
        
        // Publish invalidation to all instances
        var database = _redis.GetDatabase();
        var syncMessage = new CacheSyncMessage
        {
            Type = SyncMessageType.InvalidateKey,
            Key = key,
            Timestamp = DateTimeOffset.UtcNow,
            InstanceId = Environment.MachineName
        };

        await database.PublishAsync(_options.SyncChannel, JsonSerializer.Serialize(syncMessage));
        
        _logger.LogDebug("Published global invalidation for key: {Key}", key);
    }

    public async Task InvalidateGloballyByPatternAsync(string pattern)
    {
        // Find matching keys locally
        var matchingKeys = await _cache.GetKeysByPatternAsync(pattern);
        
        // Remove local keys
        foreach (var key in matchingKeys)
        {
            await _cache.RemoveAsync(key);
        }

        // Publish pattern invalidation
        var database = _redis.GetDatabase();
        var syncMessage = new CacheSyncMessage
        {
            Type = SyncMessageType.InvalidatePattern,
            Pattern = pattern,
            Timestamp = DateTimeOffset.UtcNow,
            InstanceId = Environment.MachineName
        };

        await database.PublishAsync(_options.SyncChannel, JsonSerializer.Serialize(syncMessage));
        
        _logger.LogDebug("Published global pattern invalidation: {Pattern}", pattern);
    }

    public async Task InvalidateGloballyByTableAsync(string table)
    {
        // Remove local table caches
        await _cache.InvalidateByTableAsync(table);

        // Publish table invalidation
        var database = _redis.GetDatabase();
        var syncMessage = new CacheSyncMessage
        {
            Type = SyncMessageType.InvalidateTable,
            Table = table,
            Timestamp = DateTimeOffset.UtcNow,
            InstanceId = Environment.MachineName
        };

        await database.PublishAsync(_options.SyncChannel, JsonSerializer.Serialize(syncMessage));
        
        _logger.LogDebug("Published global table invalidation: {Table}", table);
    }

    public async Task SetGloballyAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        // Set in local cache
        await _cache.SetAsync(key, value, expiration);

        // Publish to all instances
        var database = _redis.GetDatabase();
        var syncMessage = new CacheSyncMessage
        {
            Type = SyncMessageType.SetValue,
            Key = key,
            Value = JsonSerializer.Serialize(value),
            Expiration = expiration,
            Timestamp = DateTimeOffset.UtcNow,
            InstanceId = Environment.MachineName
        };

        await database.PublishAsync(_options.SyncChannel, JsonSerializer.Serialize(syncMessage));
        
        _logger.LogDebug("Published global cache set for key: {Key}", key);
    }

    public async Task<SyncStatus> GetSyncStatusAsync()
    {
        var database = _redis.GetDatabase();
        
        // Get sync channel info
        var channelInfo = await database.ExecuteAsync("PUBSUB", "CHANNELS", _options.SyncChannel);
        var subscriberCount = await database.ExecuteAsync("PUBSUB", "NUMSUB", _options.SyncChannel);

        return new SyncStatus
        {
            IsEnabled = _options.EnableCrossInstanceSync,
            Channel = _options.SyncChannel,
            SubscriberCount = ((RedisResult[])subscriberCount)[1],
            LastSyncTime = await GetLastSyncTimeAsync(),
            ConflictResolutionStrategy = _options.ConflictResolution.ToString()
        };
    }

    public async Task ForceGlobalSyncAsync()
    {
        var database = _redis.GetDatabase();
        var syncMessage = new CacheSyncMessage
        {
            Type = SyncMessageType.ForceSync,
            Timestamp = DateTimeOffset.UtcNow,
            InstanceId = Environment.MachineName
        };

        await database.PublishAsync(_options.SyncChannel, JsonSerializer.Serialize(syncMessage));
        
        _logger.LogInformation("Forced global cache sync");
    }

    private async Task<DateTimeOffset?> GetLastSyncTimeAsync()
    {
        var database = _redis.GetDatabase();
        var lastSync = await database.StringGetAsync($"{_options.SyncChannel}:last_sync");
        
        return lastSync.HasValue ? DateTimeOffset.Parse(lastSync) : null;
    }
}
```

## Sync Message Handling

Handle incoming sync messages from other instances.

### Sync Message Processor

```csharp
public class CrossInstanceSyncService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ICacheService _cache;
    private readonly CrossInstanceSyncOptions _options;
    private readonly ILogger<CrossInstanceSyncService> _logger;
    private ISubscriber _subscriber;

    public CrossInstanceSyncService(
        IConnectionMultiplexer redis,
        ICacheService cache,
        IOptions<CrossInstanceSyncOptions> options,
        ILogger<CrossInstanceSyncService> logger)
    {
        _redis = redis;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableCrossInstanceSync) return;

        _subscriber = _redis.GetSubscriber();
        
        await _subscriber.SubscribeAsync(_options.SyncChannel, HandleSyncMessage);
        
        _logger.LogInformation("Cross-instance sync service started, listening on channel: {Channel}", 
            _options.SyncChannel);

        // Keep the service running
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            
            // Periodic health check
            await UpdateLastHeartbeat();
        }
    }

    private async void HandleSyncMessage(RedisChannel channel, RedisValue message)
    {
        try
        {
            var syncMessage = JsonSerializer.Deserialize<CacheSyncMessage>(message);
            
            // Ignore messages from this instance
            if (syncMessage.InstanceId == Environment.MachineName)
                return;

            await ProcessSyncMessage(syncMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing sync message: {Message}", message);
        }
    }

    private async Task ProcessSyncMessage(CacheSyncMessage message)
    {
        _logger.LogDebug("Processing sync message: {Type} from {InstanceId}", 
            message.Type, message.InstanceId);

        switch (message.Type)
        {
            case SyncMessageType.InvalidateKey:
                await _cache.RemoveAsync(message.Key);
                break;

            case SyncMessageType.InvalidatePattern:
                var keys = await _cache.GetKeysByPatternAsync(message.Pattern);
                foreach (var key in keys)
                {
                    await _cache.RemoveAsync(key);
                }
                break;

            case SyncMessageType.InvalidateTable:
                await _cache.InvalidateByTableAsync(message.Table);
                break;

            case SyncMessageType.SetValue:
                if (ShouldApplyUpdate(message))
                {
                    var value = JsonSerializer.Deserialize<object>(message.Value);
                    await _cache.SetAsync(message.Key, value, message.Expiration);
                }
                break;

            case SyncMessageType.ForceSync:
                await PerformFullSync();
                break;
        }

        // Update sync timestamp
        await UpdateLastSyncTime(message.Timestamp);
    }

    private bool ShouldApplyUpdate(CacheSyncMessage message)
    {
        switch (_options.ConflictResolution)
        {
            case ConflictResolutionStrategy.LastWriteWins:
                return true; // Always apply updates

            case ConflictResolutionStrategy.FirstWriteWins:
                // Check if we already have a newer version
                var existingTimestamp = GetCacheItemTimestamp(message.Key);
                return existingTimestamp == null || message.Timestamp > existingTimestamp;

            case ConflictResolutionStrategy.VersionBased:
                // Implement version-based conflict resolution
                return CheckVersionCompatibility(message);

            default:
                return true;
        }
    }

    private async Task PerformFullSync()
    {
        _logger.LogInformation("Performing full cache sync");
        
        // This is a simplified version - in practice, you might want to:
        // 1. Get a list of all cache keys from Redis
        // 2. Compare with local cache
        // 3. Sync differences
        
        // For now, we'll just clear local cache to force refresh
        await _cache.ClearAsync();
    }

    private async Task UpdateLastSyncTime(DateTimeOffset timestamp)
    {
        var database = _redis.GetDatabase();
        await database.StringSetAsync($"{_options.SyncChannel}:last_sync", timestamp.ToString());
    }

    private async Task UpdateLastHeartbeat()
    {
        var database = _redis.GetDatabase();
        var heartbeatKey = $"{_options.SyncChannel}:heartbeat:{Environment.MachineName}";
        await database.StringSetAsync(heartbeatKey, DateTimeOffset.UtcNow.ToString(), TimeSpan.FromMinutes(5));
    }

    private DateTimeOffset? GetCacheItemTimestamp(string key)
    {
        // Implementation depends on your cache metadata storage
        // This could be stored as part of the cache value or in separate metadata
        return null; // Simplified for example
    }

    private bool CheckVersionCompatibility(CacheSyncMessage message)
    {
        // Implement version-based conflict resolution
        // This could involve comparing version numbers stored with cache items
        return true; // Simplified for example
    }
}

public class CacheSyncMessage
{
    public SyncMessageType Type { get; set; }
    public string Key { get; set; }
    public string Pattern { get; set; }
    public string Table { get; set; }
    public string Value { get; set; }
    public TimeSpan? Expiration { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string InstanceId { get; set; }
    public long Version { get; set; }
}

public enum SyncMessageType
{
    InvalidateKey,
    InvalidatePattern,
    InvalidateTable,
    SetValue,
    ForceSync
}

public enum ConflictResolutionStrategy
{
    LastWriteWins,
    FirstWriteWins,
    VersionBased,
    Manual
}
```

## Conflict Resolution

Handle conflicts when multiple instances update the same cache simultaneously.

### Last Write Wins Strategy

```csharp
public class LastWriteWinsResolver : IConflictResolver
{
    public async Task<ConflictResolutionResult> ResolveConflictAsync(ConflictContext context)
    {
        // Always accept the most recent update
        return new ConflictResolutionResult
        {
            Resolution = ConflictResolution.AcceptIncoming,
            ResolvedValue = context.IncomingValue,
            ResolvedTimestamp = context.IncomingTimestamp
        };
    }
}
```

### Version-based Resolution

```csharp
public class VersionBasedResolver : IConflictResolver
{
    public async Task<ConflictResolutionResult> ResolveConflictAsync(ConflictContext context)
    {
        var existingVersion = GetVersion(context.ExistingValue);
        var incomingVersion = GetVersion(context.IncomingValue);

        if (incomingVersion > existingVersion)
        {
            return new ConflictResolutionResult
            {
                Resolution = ConflictResolution.AcceptIncoming,
                ResolvedValue = context.IncomingValue,
                ResolvedTimestamp = context.IncomingTimestamp
            };
        }
        else if (incomingVersion < existingVersion)
        {
            return new ConflictResolutionResult
            {
                Resolution = ConflictResolution.KeepExisting,
                ResolvedValue = context.ExistingValue,
                ResolvedTimestamp = context.ExistingTimestamp
            };
        }
        else
        {
            // Same version - fallback to timestamp
            return context.IncomingTimestamp > context.ExistingTimestamp
                ? new ConflictResolutionResult
                {
                    Resolution = ConflictResolution.AcceptIncoming,
                    ResolvedValue = context.IncomingValue,
                    ResolvedTimestamp = context.IncomingTimestamp
                }
                : new ConflictResolutionResult
                {
                    Resolution = ConflictResolution.KeepExisting,
                    ResolvedValue = context.ExistingValue,
                    ResolvedTimestamp = context.ExistingTimestamp
                };
        }
    }

    private long GetVersion(object value)
    {
        // Extract version from the cached object
        // This assumes your cached objects implement IVersioned or have version metadata
        return value switch
        {
            IVersioned versioned => versioned.Version,
            { } obj when obj.GetType().GetProperty("Version") is PropertyInfo prop => (long)prop.GetValue(obj),
            _ => 0
        };
    }
}
```

### Custom Business Logic Resolution

```csharp
public class BusinessRuleResolver : IConflictResolver
{
    private readonly ILogger<BusinessRuleResolver> _logger;

    public async Task<ConflictResolutionResult> ResolveConflictAsync(ConflictContext context)
    {
        // Apply business-specific conflict resolution rules
        switch (context.CacheKey)
        {
            case string key when key.StartsWith("user_"):
                return await ResolveUserDataConflict(context);
                
            case string key when key.StartsWith("product_"):
                return await ResolveProductDataConflict(context);
                
            case string key when key.StartsWith("inventory_"):
                return await ResolveInventoryConflict(context);
                
            default:
                // Default to last write wins
                return new ConflictResolutionResult
                {
                    Resolution = ConflictResolution.AcceptIncoming,
                    ResolvedValue = context.IncomingValue,
                    ResolvedTimestamp = context.IncomingTimestamp
                };
        }
    }

    private async Task<ConflictResolutionResult> ResolveUserDataConflict(ConflictContext context)
    {
        // User data: prefer the most complete data
        var existing = JsonSerializer.Deserialize<UserData>(context.ExistingValue.ToString());
        var incoming = JsonSerializer.Deserialize<UserData>(context.IncomingValue.ToString());

        var resolved = new UserData
        {
            Id = existing.Id,
            Name = string.IsNullOrEmpty(existing.Name) ? incoming.Name : existing.Name,
            Email = string.IsNullOrEmpty(existing.Email) ? incoming.Email : existing.Email,
            LastUpdated = DateTimeOffset.Max(existing.LastUpdated, incoming.LastUpdated)
        };

        return new ConflictResolutionResult
        {
            Resolution = ConflictResolution.Merge,
            ResolvedValue = resolved,
            ResolvedTimestamp = DateTimeOffset.UtcNow
        };
    }

    private async Task<ConflictResolutionResult> ResolveInventoryConflict(ConflictContext context)
    {
        // Inventory: prefer the lower quantity for safety
        var existing = JsonSerializer.Deserialize<InventoryData>(context.ExistingValue.ToString());
        var incoming = JsonSerializer.Deserialize<InventoryData>(context.IncomingValue.ToString());

        var resolved = new InventoryData
        {
            ProductId = existing.ProductId,
            Quantity = Math.Min(existing.Quantity, incoming.Quantity), // Conservative approach
            LastUpdated = DateTimeOffset.UtcNow
        };

        _logger.LogWarning("Inventory conflict resolved for product {ProductId}: chose quantity {Quantity} from options {ExistingQuantity} and {IncomingQuantity}",
            existing.ProductId, resolved.Quantity, existing.Quantity, incoming.Quantity);

        return new ConflictResolutionResult
        {
            Resolution = ConflictResolution.Merge,
            ResolvedValue = resolved,
            ResolvedTimestamp = DateTimeOffset.UtcNow
        };
    }
}
```

## Monitoring Cross-instance Sync

Track sync health and performance across instances.

### Sync Monitoring Dashboard

```csharp
[ApiController]
[Route("api/cache/sync")]
public class SyncMonitoringController : ControllerBase
{
    private readonly IDistributedCacheManager _distributedCache;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SyncMonitoringController> _logger;

    [HttpGet("status")]
    public async Task<IActionResult> GetSyncStatus()
    {
        var status = await _distributedCache.GetSyncStatusAsync();
        var instances = await GetConnectedInstancesAsync();
        var syncMetrics = await GetSyncMetricsAsync();

        return Ok(new
        {
            SyncStatus = status,
            ConnectedInstances = instances,
            Metrics = syncMetrics,
            Health = await EvaluateSyncHealthAsync(status, instances, syncMetrics)
        });
    }

    [HttpGet("instances")]
    public async Task<IActionResult> GetConnectedInstances()
    {
        var instances = await GetConnectedInstancesAsync();
        return Ok(instances);
    }

    [HttpPost("force-sync")]
    public async Task<IActionResult> ForceSync()
    {
        await _distributedCache.ForceGlobalSyncAsync();
        return Ok(new { Message = "Global sync initiated" });
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetSyncMetrics()
    {
        var metrics = await GetSyncMetricsAsync();
        return Ok(metrics);
    }

    private async Task<List<InstanceInfo>> GetConnectedInstancesAsync()
    {
        var database = _redis.GetDatabase();
        var instances = new List<InstanceInfo>();

        try
        {
            // Get all heartbeat keys
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var heartbeatKeys = server.Keys(pattern: "cache_sync:heartbeat:*");

            foreach (var key in heartbeatKeys)
            {
                var lastHeartbeat = await database.StringGetAsync(key);
                if (lastHeartbeat.HasValue)
                {
                    var instanceName = key.ToString().Split(':').Last();
                    var heartbeatTime = DateTimeOffset.Parse(lastHeartbeat);
                    var isOnline = DateTimeOffset.UtcNow - heartbeatTime < TimeSpan.FromMinutes(2);

                    instances.Add(new InstanceInfo
                    {
                        Name = instanceName,
                        LastHeartbeat = heartbeatTime,
                        IsOnline = isOnline,
                        Status = isOnline ? "Online" : "Offline"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving connected instances");
        }

        return instances;
    }

    private async Task<SyncMetrics> GetSyncMetricsAsync()
    {
        var database = _redis.GetDatabase();
        
        // Get sync statistics
        var totalMessages = await database.StringGetAsync("sync:metrics:total_messages") ?? 0;
        var conflictCount = await database.StringGetAsync("sync:metrics:conflicts") ?? 0;
        var lastSyncTime = await database.StringGetAsync("cache_sync:last_sync");

        return new SyncMetrics
        {
            TotalSyncMessages = totalMessages,
            ConflictCount = conflictCount,
            ConflictRate = totalMessages > 0 ? (double)conflictCount / totalMessages * 100 : 0,
            LastSyncTime = lastSyncTime.HasValue ? DateTimeOffset.Parse(lastSyncTime) : null,
            AverageSyncLatency = await CalculateAverageSyncLatency(),
            SyncThroughput = await CalculateSyncThroughput()
        };
    }

    private async Task<SyncHealth> EvaluateSyncHealthAsync(SyncStatus status, List<InstanceInfo> instances, SyncMetrics metrics)
    {
        var onlineInstances = instances.Count(i => i.IsOnline);
        var totalInstances = instances.Count;
        
        var health = new SyncHealth
        {
            OverallStatus = "Healthy",
            Issues = new List<string>()
        };

        // Check instance connectivity
        if (onlineInstances < totalInstances)
        {
            health.Issues.Add($"{totalInstances - onlineInstances} instance(s) offline");
            health.OverallStatus = "Degraded";
        }

        // Check sync latency
        if (metrics.AverageSyncLatency > TimeSpan.FromSeconds(5))
        {
            health.Issues.Add("High sync latency detected");
            health.OverallStatus = "Degraded";
        }

        // Check conflict rate
        if (metrics.ConflictRate > 10.0)
        {
            health.Issues.Add("High conflict rate detected");
            health.OverallStatus = "Warning";
        }

        // Check last sync time
        if (metrics.LastSyncTime.HasValue && DateTimeOffset.UtcNow - metrics.LastSyncTime.Value > TimeSpan.FromMinutes(10))
        {
            health.Issues.Add("No recent sync activity");
            health.OverallStatus = "Warning";
        }

        if (health.Issues.Count == 0)
        {
            health.OverallStatus = "Healthy";
        }

        return health;
    }

    private async Task<TimeSpan> CalculateAverageSyncLatency()
    {
        // Implementation would track sync message timestamps and calculate latency
        return TimeSpan.FromMilliseconds(50); // Placeholder
    }

    private async Task<double> CalculateSyncThroughput()
    {
        // Implementation would calculate messages per second
        return 10.5; // Placeholder
    }
}

public class InstanceInfo
{
    public string Name { get; set; }
    public DateTimeOffset LastHeartbeat { get; set; }
    public bool IsOnline { get; set; }
    public string Status { get; set; }
}

public class SyncMetrics
{
    public long TotalSyncMessages { get; set; }
    public long ConflictCount { get; set; }
    public double ConflictRate { get; set; }
    public DateTimeOffset? LastSyncTime { get; set; }
    public TimeSpan AverageSyncLatency { get; set; }
    public double SyncThroughput { get; set; }
}

public class SyncHealth
{
    public string OverallStatus { get; set; }
    public List<string> Issues { get; set; }
}
```

## Best Practices

### 1. Design for Eventually Consistent Systems

```csharp
// Good: Accept that data might be temporarily inconsistent
[HttpGet("{id}")]
[AthenaCache(ExpirationMinutes = 30)]
public async Task<ProductDto> GetProduct(int id)
{
    // Accept that different instances might have slightly different data for a short time
    return await _productService.GetProductAsync(id);
}
```

### 2. Use Appropriate Sync Granularity

```csharp
// Good: Invalidate specific related data
[HttpPut("{id}")]
[CacheInvalidateOn("Products", InvalidationType.Pattern, "product_{id}_*")]
public async Task UpdateProduct(int id, [FromBody] UpdateProductRequest request)
{
    // Only invalidates caches related to this specific product
    return await _productService.UpdateProductAsync(id, request);
}

// Avoid: Over-broad invalidation
// [CacheInvalidateOn("Products")] // This would clear ALL product caches
```

### 3. Monitor Sync Performance

```csharp
// Track sync metrics
public class SyncMetricsCollector
{
    private readonly IMetricsCollector _metrics;

    public void RecordSyncOperation(string operation, TimeSpan duration, bool success)
    {
        _metrics.RecordGauge("sync_operation_duration_ms", duration.TotalMilliseconds, new Dictionary<string, string>
        {
            ["operation"] = operation,
            ["success"] = success.ToString()
        });

        _metrics.IncrementCounter("sync_operations_total", new Dictionary<string, string>
        {
            ["operation"] = operation,
            ["result"] = success ? "success" : "failure"
        });
    }
}
```

### 4. Handle Network Partitions Gracefully

```csharp
// Implement partition tolerance
public class PartitionTolerantSync
{
    public async Task HandlePartition()
    {
        // During network partition:
        // 1. Continue serving from local cache
        // 2. Queue sync operations for later
        // 3. Resume sync when partition heals
        
        if (await IsNetworkPartitioned())
        {
            await SwitchToLocalOnlyMode();
        }
        else
        {
            await ResumeCrossInstanceSync();
        }
    }
}
```

For related topics:
- **[Redis Setup]({{ site.baseurl }}/distributed/redis-setup)** - Basic distributed configuration
- **[High Availability]({{ site.baseurl }}/distributed/high-availability)** - Resilience and failover
- **[Performance Metrics]({{ site.baseurl }}/monitoring/performance-metrics)** - Monitoring sync performance
- **[Error Handling]({{ site.baseurl }}/fundamentals/error-handling)** - Handle sync failures