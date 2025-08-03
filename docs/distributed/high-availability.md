---
layout: default
title: High Availability
---

# High Availability

Ensure your distributed cache remains available even when individual components fail. This guide covers Redis clustering, failover strategies, and disaster recovery for mission-critical applications.

## Redis Sentinel for Automatic Failover

Redis Sentinel provides automatic failover when the master Redis instance fails.

### Sentinel Configuration

```bash
# sentinel.conf
port 26379
sentinel monitor mymaster 192.168.1.100 6379 2
sentinel down-after-milliseconds mymaster 5000
sentinel failover-timeout mymaster 60000
sentinel parallel-syncs mymaster 1

# Authentication
sentinel auth-pass mymaster your-redis-password

# Logging
logfile /var/log/redis/sentinel.log
```

### Application Configuration

```csharp
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions =>
    {
        athenaOptions.Namespace = "HA_Production";
        athenaOptions.DefaultExpirationMinutes = 60;
    },
    redisOptions =>
    {
        // Sentinel configuration
        redisOptions.ConnectionString = "sentinel1:26379,sentinel2:26379,sentinel3:26379";
        redisOptions.ServiceName = "mymaster";
        
        // High availability settings
        redisOptions.ConnectRetry = 10;
        redisOptions.ConnectTimeout = 30000;    // 30 seconds
        redisOptions.AbortOnConnectFail = false; // Don't abort on connection failure
        
        // Failover settings
        redisOptions.AllowAdmin = false;
        redisOptions.ResponseTimeout = 5000;    // 5 seconds
    });
```

### Sentinel Monitoring

```csharp
public class SentinelMonitoringService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SentinelMonitoringService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var endpoints = _redis.GetEndPoints();
                var sentinelInfo = new List<object>();

                foreach (var endpoint in endpoints)
                {
                    try
                    {
                        var server = _redis.GetServer(endpoint);
                        if (server.IsSlave || !server.IsConnected) continue;

                        var info = await server.InfoAsync("sentinel");
                        sentinelInfo.Add(new
                        {
                            Endpoint = endpoint.ToString(),
                            Info = info.ToDictionary(x => x.Key, x => x.Value)
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to get sentinel info from {Endpoint}: {Error}", 
                            endpoint, ex.Message);
                    }
                }

                _logger.LogInformation("Sentinel status: {@SentinelInfo}", sentinelInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring sentinel");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

## Redis Cluster for Horizontal Scaling

Redis Cluster provides data sharding and automatic failover across multiple nodes.

### Cluster Setup

```bash
# Create Redis cluster with 6 nodes (3 masters, 3 slaves)
redis-cli --cluster create \
  192.168.1.100:7000 192.168.1.101:7000 192.168.1.102:7000 \
  192.168.1.100:7001 192.168.1.101:7001 192.168.1.102:7001 \
  --cluster-replicas 1
```

### Cluster Configuration

```csharp
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions =>
    {
        athenaOptions.Namespace = "Cluster_Production";
        athenaOptions.DefaultExpirationMinutes = 45;
    },
    redisOptions =>
    {
        // Redis Cluster configuration
        redisOptions.ConnectionString = "node1:7000,node2:7000,node3:7000,node4:7001,node5:7001,node6:7001";
        
        // Cluster-specific settings
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectRetry = 5;
        redisOptions.ConnectTimeout = 15000;
        
        // Performance settings for cluster
        redisOptions.AsyncTimeout = 10000;
        redisOptions.SyncTimeout = 5000;
    });
```

### Cluster Health Monitoring

```csharp
[HttpGet("redis/cluster/health")]
public async Task<IActionResult> GetClusterHealth([FromServices] IConnectionMultiplexer redis)
{
    var clusterInfo = new List<object>();

    foreach (var endpoint in redis.GetEndPoints())
    {
        try
        {
            var server = redis.GetServer(endpoint);
            var info = await server.InfoAsync("replication");
            var clusterNodes = await server.ExecuteAsync("CLUSTER", "NODES");

            clusterInfo.Add(new
            {
                Endpoint = endpoint.ToString(),
                IsConnected = server.IsConnected,
                IsSlave = server.IsSlave,
                ServerType = server.ServerType,
                Version = server.Version,
                ReplicationInfo = info.ToDictionary(x => x.Key, x => x.Value),
                ClusterNodes = clusterNodes.ToString()
            });
        }
        catch (Exception ex)
        {
            clusterInfo.Add(new
            {
                Endpoint = endpoint.ToString(),
                Error = ex.Message,
                IsConnected = false
            });
        }
    }

    return Ok(new
    {
        ClusterStatus = clusterInfo,
        TotalNodes = clusterInfo.Count,
        ConnectedNodes = clusterInfo.Count(n => n.GetType().GetProperty("IsConnected")?.GetValue(n)?.Equals(true) == true)
    });
}
```

## Multi-Region Deployment

Deploy cache across multiple geographic regions for disaster recovery.

### Region Configuration

```csharp
public class MultiRegionCacheConfiguration
{
    public string PrimaryRegion { get; set; } = "us-east-1";
    public string SecondaryRegion { get; set; } = "us-west-2";
    public TimeSpan FailoverTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnableCrossRegionReplication { get; set; } = true;
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMultiRegionCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var config = configuration.GetSection("MultiRegionCache").Get<MultiRegionCacheConfiguration>();
        
        services.AddAthenaCacheRedisComplete(
            athenaOptions =>
            {
                athenaOptions.Namespace = $"MultiRegion_{config.PrimaryRegion}";
                athenaOptions.Resilience.EnableFallbackToSecondary = true;
                athenaOptions.Resilience.SecondaryRegionTimeout = config.FailoverTimeout;
            },
            redisOptions =>
            {
                // Primary region connection
                redisOptions.ConnectionString = configuration.GetConnectionString($"Redis_{config.PrimaryRegion}");
                redisOptions.AbortOnConnectFail = false;
                redisOptions.ConnectRetry = 3;
            });

        // Register secondary region connection
        services.AddSingleton<ISecondaryRegionCache>(provider =>
            new SecondaryRegionCache(
                configuration.GetConnectionString($"Redis_{config.SecondaryRegion}"),
                config));

        return services;
    }
}
```

### Cross-Region Replication

```csharp
public class CrossRegionReplicationService : BackgroundService
{
    private readonly ICacheService _primaryCache;
    private readonly ISecondaryRegionCache _secondaryCache;
    private readonly MultiRegionCacheConfiguration _config;
    private readonly ILogger<CrossRegionReplicationService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.EnableCrossRegionReplication) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReplicateChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cross-region replication");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task ReplicateChanges()
    {
        var changesSinceLastSync = await _primaryCache.GetChangesSinceAsync(_lastSyncTime);
        
        foreach (var change in changesSinceLastSync)
        {
            try
            {
                switch (change.OperationType)
                {
                    case CacheOperationType.Set:
                        await _secondaryCache.SetAsync(change.Key, change.Value, change.Expiration);
                        break;
                    case CacheOperationType.Delete:
                        await _secondaryCache.RemoveAsync(change.Key);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to replicate change {ChangeId}: {Error}", 
                    change.Id, ex.Message);
            }
        }

        _lastSyncTime = DateTimeOffset.UtcNow;
    }
}
```

## Connection Resilience

Handle connection failures gracefully with automatic retry and circuit breaker patterns.

### Resilience Configuration

```csharp
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions =>
    {
        // Circuit breaker configuration
        athenaOptions.Resilience.EnableCircuitBreaker = true;
        athenaOptions.Resilience.FailureThreshold = 5;      // Open after 5 failures
        athenaOptions.Resilience.RecoveryTimeSeconds = 30;  // Try again after 30s
        athenaOptions.Resilience.TimeoutSeconds = 10;       // Timeout after 10s
        
        // Retry configuration
        athenaOptions.Resilience.EnableRetry = true;
        athenaOptions.Resilience.MaxRetryAttempts = 3;
        athenaOptions.Resilience.RetryDelayMs = 1000;       // 1 second between retries
        athenaOptions.Resilience.BackoffMultiplier = 2.0;   // Exponential backoff
        
        // Fallback configuration
        athenaOptions.Resilience.EnableFallbackToMemory = true;
        athenaOptions.Resilience.FallbackToMemoryOnError = true;
        athenaOptions.Resilience.MemoryFallbackMaxItems = 1000;
    },
    redisOptions => { /* ... */ });
```

### Custom Resilience Handler

```csharp
public class CacheResilienceHandler
{
    private readonly ICacheService _cache;
    private readonly IMemoryCache _fallbackCache;
    private readonly CircuitBreakerState _circuitBreaker;
    private readonly ILogger<CacheResilienceHandler> _logger;

    public async Task<T> ExecuteWithResilienceAsync<T>(
        string key,
        Func<Task<T>> operation,
        TimeSpan? expiration = null)
    {
        if (_circuitBreaker.State == CircuitBreakerState.Open)
        {
            _logger.LogInformation("Circuit breaker is open, using fallback cache for {Key}", key);
            return GetFromFallbackCache<T>(key);
        }

        try
        {
            var result = await ExecuteWithRetryAsync(operation);
            _circuitBreaker.RecordSuccess();
            
            // Cache in fallback for future use
            CacheInFallback(key, result, expiration);
            
            return result;
        }
        catch (Exception ex)
        {
            _circuitBreaker.RecordFailure();
            _logger.LogWarning(ex, "Cache operation failed for {Key}, using fallback", key);
            
            return GetFromFallbackCache<T>(key);
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation)
    {
        var retryCount = 0;
        var delay = TimeSpan.FromMilliseconds(1000);

        while (retryCount < 3)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (retryCount < 2)
            {
                retryCount++;
                _logger.LogWarning("Retry {RetryCount} for cache operation: {Error}", retryCount, ex.Message);
                
                await Task.Delay(delay);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2); // Exponential backoff
            }
        }

        throw new InvalidOperationException("Cache operation failed after all retries");
    }

    private T GetFromFallbackCache<T>(string key)
    {
        return _fallbackCache.TryGetValue(key, out T value) ? value : default(T);
    }

    private void CacheInFallback<T>(string key, T value, TimeSpan? expiration)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromMinutes(30),
            Priority = CacheItemPriority.Normal
        };

        _fallbackCache.Set(key, value, options);
    }
}
```

## Health Monitoring and Alerting

Comprehensive health monitoring for high availability systems.

### Health Check Implementation

```csharp
public class DistributedCacheHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ICacheService _cache;
    private readonly ILogger<DistributedCacheHealthCheck> _logger;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var checks = new List<(string Name, bool Success, string Details)>();

            // Check Redis connection
            var isConnected = _redis.IsConnected;
            checks.Add(("Redis Connection", isConnected, $"Connected: {isConnected}"));

            if (isConnected)
            {
                // Check Redis ping
                var database = _redis.GetDatabase();
                var pingTime = await database.PingAsync();
                var pingSuccess = pingTime.TotalMilliseconds < 100;
                checks.Add(("Redis Ping", pingSuccess, $"Ping time: {pingTime.TotalMilliseconds:F2}ms"));

                // Check cache operations
                var testKey = $"health_check_{Guid.NewGuid()}";
                var testValue = "health_check_value";
                
                await _cache.SetAsync(testKey, testValue, TimeSpan.FromMinutes(1));
                var retrievedValue = await _cache.GetAsync<string>(testKey);
                var cacheOpSuccess = retrievedValue == testValue;
                checks.Add(("Cache Operations", cacheOpSuccess, $"Set/Get test: {(cacheOpSuccess ? "Success" : "Failed")}"));

                await _cache.RemoveAsync(testKey);
            }

            var allSuccess = checks.All(c => c.Success);
            var details = checks.ToDictionary(c => c.Name, c => c.Details);

            return allSuccess 
                ? HealthCheckResult.Healthy("All cache health checks passed", details)
                : HealthCheckResult.Degraded("Some cache health checks failed", details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache health check failed");
            return HealthCheckResult.Unhealthy("Cache health check failed", ex);
        }
    }
}

// Register health checks
builder.Services.AddHealthChecks()
    .AddCheck<DistributedCacheHealthCheck>("distributed_cache")
    .AddCheck("redis_connectivity", () =>
    {
        // Additional specific checks
        return HealthCheckResult.Healthy();
    });
```

### Real-time Monitoring Dashboard

```csharp
[HttpGet("ha/status")]
public async Task<IActionResult> GetHighAvailabilityStatus(
    [FromServices] IConnectionMultiplexer redis,
    [FromServices] ICacheStatistics stats,
    [FromServices] HealthCheckService healthCheck)
{
    var healthReport = await healthCheck.CheckHealthAsync();
    var currentStats = await stats.GetCurrentStatsAsync();

    return Ok(new
    {
        Timestamp = DateTimeOffset.UtcNow,
        
        OverallHealth = new
        {
            Status = healthReport.Status.ToString(),
            TotalDuration = healthReport.TotalDuration,
            Entries = healthReport.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    Status = kvp.Value.Status.ToString(),
                    Duration = kvp.Value.Duration,
                    Description = kvp.Value.Description,
                    Data = kvp.Value.Data
                })
        },

        RedisCluster = new
        {
            TotalNodes = redis.GetEndPoints().Length,
            ConnectedNodes = redis.GetEndPoints().Count(ep => 
            {
                try
                {
                    var server = redis.GetServer(ep);
                    return server.IsConnected;
                }
                catch
                {
                    return false;
                }
            }),
            MasterNodes = redis.GetEndPoints().Count(ep =>
            {
                try
                {
                    var server = redis.GetServer(ep);
                    return server.IsConnected && !server.IsSlave;
                }
                catch
                {
                    return false;
                }
            })
        },

        Performance = new
        {
            HitRate = currentStats.HitRate,
            RequestsPerSecond = currentStats.RequestsPerSecond,
            AverageResponseTime = currentStats.AverageResponseTime,
            ErrorRate = currentStats.ErrorRate,
            MemoryUsage = currentStats.MemoryUsage
        },

        Failover = new
        {
            FailoverCount = GetFailoverCount(),
            LastFailoverTime = GetLastFailoverTime(),
            RecoveryTime = GetLastRecoveryTime(),
            CircuitBreakerState = GetCircuitBreakerState()
        }
    });
}
```

## Disaster Recovery

Implement comprehensive disaster recovery procedures.

### Backup and Recovery

```csharp
public class CacheBackupService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CacheBackupService> _logger;

    public async Task CreateBackupAsync(string backupPath)
    {
        try
        {
            foreach (var endpoint in _redis.GetEndPoints())
            {
                var server = _redis.GetServer(endpoint);
                if (server.IsSlave || !server.IsConnected) continue;

                _logger.LogInformation("Creating backup for Redis server: {Endpoint}", endpoint);
                
                // Trigger Redis BGSAVE
                await server.BackgroundSaveAsync();
                
                // Wait for backup completion
                while (server.LastSaveTime < DateTimeOffset.UtcNow.AddMinutes(-1))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }

                _logger.LogInformation("Backup completed for Redis server: {Endpoint}", endpoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create cache backup");
            throw;
        }
    }

    public async Task RestoreFromBackupAsync(string backupPath)
    {
        _logger.LogInformation("Starting cache restore from backup: {BackupPath}", backupPath);
        
        try
        {
            // Implementation depends on your backup strategy
            // This might involve:
            // 1. Stopping Redis instances
            // 2. Copying backup files
            // 3. Restarting Redis instances
            // 4. Verifying data integrity

            _logger.LogInformation("Cache restore completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore cache from backup");
            throw;
        }
    }
}
```

### Automated Recovery Procedures

```csharp
public class DisasterRecoveryService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ICacheService _cache;
    private readonly ILogger<DisasterRecoveryService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorAndRecover();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in disaster recovery monitoring");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task MonitorAndRecover()
    {
        var healthStatus = await CheckSystemHealth();
        
        if (healthStatus.RequiresRecovery)
        {
            _logger.LogWarning("System health degraded, initiating recovery procedures");
            
            await ExecuteRecoveryProcedure(healthStatus);
        }
    }

    private async Task ExecuteRecoveryProcedure(SystemHealthStatus healthStatus)
    {
        if (healthStatus.RedisConnectionLost)
        {
            await AttemptRedisReconnection();
        }

        if (healthStatus.DataIntegrityIssues)
        {
            await ValidateAndRepairData();
        }

        if (healthStatus.PerformanceDegraded)
        {
            await OptimizePerformance();
        }
    }
}
```

## Best Practices for High Availability

### 1. Use Multiple Availability Zones

```csharp
// Deploy Redis nodes across different availability zones
var redisConfig = new RedisConfiguration
{
    Nodes = new[]
    {
        new RedisNode { Host = "redis-az1.example.com", Port = 6379, AvailabilityZone = "us-east-1a" },
        new RedisNode { Host = "redis-az2.example.com", Port = 6379, AvailabilityZone = "us-east-1b" },
        new RedisNode { Host = "redis-az3.example.com", Port = 6379, AvailabilityZone = "us-east-1c" }
    }
};
```

### 2. Implement Graceful Degradation

```csharp
[AthenaCache(FallbackStrategy = CacheFallbackStrategy.GracefulDegradation)]
public async Task<ProductDto[]> GetProducts()
{
    // If cache fails, still return data (but slower)
    return await _productService.GetProductsAsync();
}
```

### 3. Regular Testing

```csharp
public class HighAvailabilityTests
{
    [Test]
    public async Task Should_Handle_Redis_Node_Failure()
    {
        // Simulate node failure and verify failover
        await SimulateNodeFailure("redis-node-1");
        
        var result = await _cacheService.GetAsync<string>("test-key");
        
        Assert.IsNotNull(result); // Should still work via other nodes
    }

    [Test]
    public async Task Should_Recover_After_Network_Partition()
    {
        // Test network partition scenarios
        await SimulateNetworkPartition();
        await RestoreNetworkConnectivity();
        
        // Verify cache consistency after recovery
        await VerifyCacheConsistency();
    }
}
```

For related documentation:
- **[Redis Setup]({{ site.baseurl }}/distributed/redis-setup)** - Basic Redis configuration
- **[Cross-instance Sync]({{ site.baseurl }}/distributed/cross-instance-sync)** - Data synchronization
- **[Monitoring]({{ site.baseurl }}/monitoring/real-time-dashboards)** - Health monitoring
- **[Troubleshooting]({{ site.baseurl }}/advanced/troubleshooting)** - Common issues and solutions