---
layout: default
title: Redis Setup
---

# Redis Setup

Athena.Cache supports Redis for distributed caching across multiple application instances. This guide covers Redis configuration, connection management, and best practices for production deployments.

## Why Redis?

Redis enables distributed caching for scenarios like:
- **Load-balanced applications** with multiple instances
- **Microservices** sharing cache data
- **Auto-scaling environments** where instances come and go
- **Cross-service cache sharing** between different applications

## Basic Redis Setup

### Install Package

```bash
dotnet add package Athena.Cache.Redis
```

### Simple Configuration

```csharp
// Program.cs
using Athena.Cache.Redis.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Replace MemoryCache setup with Redis
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions => {
        athenaOptions.Namespace = "MyApp";
        athenaOptions.DefaultExpirationMinutes = 30;
    },
    redisOptions => {
        redisOptions.ConnectionString = "localhost:6379";
        redisOptions.DatabaseId = 1;
        redisOptions.InstanceName = "MyApp";
    });

var app = builder.Build();

app.UseRouting();
app.UseAthenaCache();
app.MapControllers();

app.Run();
```

## Redis Connection Options

### Connection String Formats

```csharp
// Single server
redisOptions.ConnectionString = "localhost:6379";

// With authentication
redisOptions.ConnectionString = "localhost:6379,password=mypassword";

// Multiple servers (Redis Cluster)
redisOptions.ConnectionString = "server1:6379,server2:6379,server3:6379";

// With SSL
redisOptions.ConnectionString = "redis.example.com:6380,ssl=true,password=secret";

// Azure Redis Cache
redisOptions.ConnectionString = "cachename.redis.cache.windows.net:6380,password=key,ssl=true";
```

### Advanced Connection Options

```csharp
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions => { /* ... */ },
    redisOptions => {
        redisOptions.ConnectionString = "localhost:6379";
        redisOptions.DatabaseId = 2;
        redisOptions.InstanceName = "MyApp_Prod";
        
        // Timeout settings
        redisOptions.ConnectTimeout = 10000;  // 10 seconds
        redisOptions.SyncTimeout = 2000;      // 2 seconds
        redisOptions.AsyncTimeout = 5000;     // 5 seconds
        
        // Retry settings
        redisOptions.ConnectRetry = 5;
        redisOptions.AbortOnConnectFail = false;
        
        // Performance settings
        redisOptions.AllowAdmin = false;
        redisOptions.ChannelPrefix = "cache:";
    });
```

## Configuration from appsettings.json

### Configuration File

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "AthenaCache": {
    "Namespace": "MyApp_Prod",
    "DefaultExpirationMinutes": 60,
    "Logging": {
      "LogCacheHitMiss": true,
      "LogCacheInvalidation": true
    }
  },
  "RedisCacheOptions": {
    "ConnectionString": "localhost:6379",
    "DatabaseId": 1,
    "InstanceName": "MyApp",
    "ConnectTimeout": 5000,
    "SyncTimeout": 1000,
    "AsyncTimeout": 3000,
    "ConnectRetry": 3,
    "AbortOnConnectFail": false,
    "AllowAdmin": false,
    "ChannelPrefix": "cache:"
  }
}
```

### Using Configuration

```csharp
// Program.cs
builder.Services.AddAthenaCacheRedisComplete(
    builder.Configuration.GetSection("AthenaCache"),
    builder.Configuration.GetSection("RedisCacheOptions"));

// Or using connection string
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions => {
        athenaOptions.Namespace = "MyApp";
    },
    redisOptions => {
        redisOptions.ConnectionString = builder.Configuration.GetConnectionString("Redis");
        redisOptions.DatabaseId = 1;
    });
```

## High Availability Setup

### Redis Sentinel

For automatic failover with Redis Sentinel:

```csharp
redisOptions.ConnectionString = "sentinel1:26379,sentinel2:26379,sentinel3:26379";
redisOptions.ServiceName = "mymaster";
redisOptions.AbortOnConnectFail = false;
redisOptions.ConnectRetry = 10;
```

### Redis Cluster

For Redis Cluster deployment:

```csharp
redisOptions.ConnectionString = 
    "node1:7000,node2:7000,node3:7000,node4:7000,node5:7000,node6:7000";
redisOptions.AbortOnConnectFail = false;
```

### Cloud Redis Services

#### Azure Redis Cache

```json
{
  "RedisCacheOptions": {
    "ConnectionString": "yourcache.redis.cache.windows.net:6380,password=yourkey,ssl=true",
    "DatabaseId": 0,
    "ConnectTimeout": 30000,
    "SyncTimeout": 5000
  }
}
```

#### AWS ElastiCache

```json
{
  "RedisCacheOptions": {
    "ConnectionString": "your-cluster.cache.amazonaws.com:6379",
    "DatabaseId": 0,
    "ConnectTimeout": 15000
  }
}
```

## Distributed Cache Invalidation

One of the major benefits of Redis is automatic cache invalidation across all instances.

### Automatic Cross-instance Invalidation

```csharp
// Instance A
[HttpPost]
[CacheInvalidateOn("Products")]
public async Task<ProductDto> CreateProduct([FromBody] CreateProductRequest request)
{
    var product = await _productService.CreateProductAsync(request);
    // This automatically invalidates "Products" cache on Instance B, C, etc.
    return product;
}

// Instance B, C, etc. - their "Products" caches are automatically cleared
[HttpGet]
[AthenaCache(ExpirationMinutes = 30)]
[CacheInvalidateOn("Products")]
public async Task<ProductDto[]> GetProducts()
{
    return await _productService.GetProductsAsync();
}
```

### Manual Distributed Invalidation

```csharp
[ApiController]
public class CacheAdminController : ControllerBase
{
    private readonly IDistributedCacheInvalidator _distributedInvalidator;

    public CacheAdminController(IDistributedCacheInvalidator distributedInvalidator)
    {
        _distributedInvalidator = distributedInvalidator;
    }

    [HttpDelete("cache/global/table/{tableName}")]
    public async Task<IActionResult> ClearTableGlobally(string tableName)
    {
        await _distributedInvalidator.InvalidateByTableAsync(tableName);
        return Ok($"Cleared {tableName} cache across ALL instances");
    }

    [HttpDelete("cache/global/pattern/{pattern}")]
    public async Task<IActionResult> ClearPatternGlobally(string pattern)
    {
        await _distributedInvalidator.InvalidateByPatternAsync(pattern);
        return Ok($"Cleared pattern {pattern} across ALL instances");
    }
}
```

## Connection Management

### Connection Health Monitoring

```csharp
[ApiController]
public class RedisHealthController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthController(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    [HttpGet("redis/health")]
    public IActionResult GetRedisHealth()
    {
        return Ok(new
        {
            IsConnected = _redis.IsConnected,
            ConnectionCount = _redis.GetCounters().Interactive.ConnectionCount,
            ServerEndpoints = _redis.GetEndPoints().Select(ep => ep.ToString()),
            ClientName = _redis.ClientName,
            Configuration = _redis.Configuration
        });
    }

    [HttpGet("redis/info")]
    public async Task<IActionResult> GetRedisInfo()
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        
        return Ok(new
        {
            DatabaseSize = await db.ExecuteAsync("DBSIZE"),
            ServerInfo = await server.InfoAsync("server"),
            MemoryInfo = await server.InfoAsync("memory"),
            StatsInfo = await server.InfoAsync("stats")
        });
    }
}
```

### Connection Events

Monitor connection events for better observability:

```csharp
// Program.cs
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions => { /* ... */ },
    redisOptions => { /* ... */ },
    connectionSetup => {
        connectionSetup.ConnectionFailed += (sender, args) =>
        {
            Console.WriteLine($"Redis connection failed: {args.FailureType} - {args.Exception?.Message}");
        };
        
        connectionSetup.ConnectionRestored += (sender, args) =>
        {
            Console.WriteLine($"Redis connection restored: {args.ConnectionType}");
        };
        
        connectionSetup.ErrorMessage += (sender, args) =>
        {
            Console.WriteLine($"Redis error: {args.Message}");
        };
    });
```

## Performance Optimization

### Connection Pooling

```csharp
// Redis connections are automatically pooled, but you can tune pool settings
redisOptions.ConnectTimeout = 5000;   // Faster connection timeout
redisOptions.SyncTimeout = 1000;      // Faster sync operations
redisOptions.AsyncTimeout = 3000;     // Reasonable async timeout
```

### Pipeline Operations

Athena.Cache automatically uses Redis pipelining for bulk operations:

```csharp
// These operations are automatically pipelined for better performance
public async Task<Dictionary<string, UserDto>> GetMultipleUsers(int[] userIds)
{
    var tasks = userIds.Select(id => GetUserAsync(id));
    var users = await Task.WhenAll(tasks);
    
    return userIds.Zip(users, (id, user) => new { id, user })
                  .Where(x => x.user != null)
                  .ToDictionary(x => x.id.ToString(), x => x.user);
}
```

### Memory Optimization

```csharp
// Configure Redis-specific memory settings
redisOptions.ChannelPrefix = "cache:";  // Shorter prefix saves memory
redisOptions.DatabaseId = 1;            // Use specific database for cache data
```

## Error Handling and Resilience

### Fallback to Memory Cache

Configure fallback when Redis is unavailable:

```csharp
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions => {
        athenaOptions.Resilience.EnableFallbackToMemory = true;
        athenaOptions.Resilience.FallbackToMemoryOnError = true;
        athenaOptions.ErrorHandling.OnCacheError = CacheErrorAction.LogAndContinue;
    },
    redisOptions => { /* ... */ });
```

### Circuit Breaker Pattern

```csharp
athenaOptions.Resilience.EnableCircuitBreaker = true;
athenaOptions.Resilience.FailureThreshold = 5;      // Open circuit after 5 failures
athenaOptions.Resilience.RecoveryTimeSeconds = 30;  // Try reconnecting after 30s
```

### Retry Configuration

```csharp
redisOptions.ConnectRetry = 5;           // Retry connection 5 times
redisOptions.AbortOnConnectFail = false; // Don't abort, keep retrying
```

## Multi-tenant Setup

### Database Separation

```csharp
// Configure different databases per tenant
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions => {
        athenaOptions.Namespace = $"Tenant_{tenantId}";
    },
    redisOptions => {
        redisOptions.ConnectionString = "localhost:6379";
        redisOptions.DatabaseId = tenantId;  // Each tenant gets its own database
    });
```

### Key Prefix Separation

```csharp
// Use prefixes to separate tenant data within same database
athenaOptions.Namespace = $"Tenant_{tenantId}";

// Or use custom key patterns
[AthenaCache(KeyPattern = "tenant_{tenantId}_user_{id}")]
public async Task<UserDto> GetUser(string tenantId, int id) { ... }
```

## Docker Setup

### Docker Compose Example

```yaml
version: '3.8'
services:
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    command: redis-server --appendonly yes
    volumes:
      - redis_data:/data
      
  app:
    build: .
    ports:
      - "5000:80"
    depends_on:
      - redis
    environment:
      - ConnectionStrings__Redis=redis:6379
      - AthenaCache__Namespace=MyApp_Docker

volumes:
  redis_data:
```

### Redis Configuration

```bash
# redis.conf for production
maxmemory 2gb
maxmemory-policy allkeys-lru
save 900 1
save 300 10
save 60 10000
appendonly yes
appendfsync everysec
```

## Monitoring and Troubleshooting

### Enable Redis Logging

```csharp
athenaOptions.Logging.LogCacheOperations = true;  // Log Redis operations
athenaOptions.Logging.LogConnectionEvents = true; // Log connection events
```

### Common Issues

#### Connection Timeouts

```csharp
// Increase timeouts for slow networks
redisOptions.ConnectTimeout = 30000;  // 30 seconds
redisOptions.SyncTimeout = 5000;      // 5 seconds
```

#### Memory Issues

```bash
# Monitor Redis memory usage
redis-cli INFO memory

# Set appropriate maxmemory in redis.conf
maxmemory 1gb
maxmemory-policy allkeys-lru
```

#### Network Partitions

```csharp
// Configure for network resilience
redisOptions.AbortOnConnectFail = false;
redisOptions.ConnectRetry = 10;
athenaOptions.Resilience.EnableFallbackToMemory = true;
```

### Performance Monitoring

```csharp
[HttpGet("redis/performance")]
public async Task<IActionResult> GetRedisPerformance()
{
    var server = _redis.GetServer(_redis.GetEndPoints().First());
    var info = await server.InfoAsync("stats");
    
    return Ok(new
    {
        TotalConnectionsReceived = info.FirstOrDefault(x => x.Key == "total_connections_received").Value,
        TotalCommandsProcessed = info.FirstOrDefault(x => x.Key == "total_commands_processed").Value,
        InstantaneousOpsPerSec = info.FirstOrDefault(x => x.Key == "instantaneous_ops_per_sec").Value,
        KeyspaceHits = info.FirstOrDefault(x => x.Key == "keyspace_hits").Value,
        KeyspaceMisses = info.FirstOrDefault(x => x.Key == "keyspace_misses").Value
    });
}
```

## Next Steps

- **[High Availability]({{ site.baseurl }}/distributed/high-availability)** - Advanced Redis deployment patterns
- **[Cross-instance Sync]({{ site.baseurl }}/distributed/cross-instance-sync)** - Synchronization strategies
- **[Monitoring]({{ site.baseurl }}/monitoring/real-time-dashboards)** - Monitor Redis performance
- **[Performance Tuning]({{ site.baseurl }}/performance/production-tuning)** - Optimize Redis performance