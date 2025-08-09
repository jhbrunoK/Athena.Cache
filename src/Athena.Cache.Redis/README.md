# 🔴 Athena.Cache.Redis

**Redis distributed caching provider for Athena Cache with advanced connection management and high availability support**

Athena.Cache.Redis extends the Athena Cache ecosystem with Redis/Valkey distributed caching capabilities, providing scalable caching solutions for multi-instance applications and microservices architectures.

## ✨ Key Features

- 🔴 **Redis/Valkey Support**: Full compatibility with Redis and Valkey
- 🌐 **Distributed Caching**: Share cache across multiple application instances
- 🔄 **Connection Management**: Advanced connection pooling and failover
- 📡 **Pub/Sub Invalidation**: Real-time cache invalidation across instances
- ⚡ **High Performance**: Optimized Redis operations with pipelining
- 🛡️ **Resilience**: Circuit breaker pattern and automatic retry logic
- 🔧 **Easy Configuration**: Simple setup with extensive customization options
- 📊 **Built-in Monitoring**: Connection health and performance metrics
- 🏗️ **Seamless Integration**: Drop-in replacement for MemoryCache

## 🚀 Quick Start

### Installation

```bash
# Install Redis package (includes Core dependency)
dotnet add package Athena.Cache.Redis

# Optional: Add Source Generator for compile-time optimizations
dotnet add package Athena.Cache.SourceGenerator
```

### Basic Setup

```csharp
// Program.cs
using Athena.Cache.Core.Extensions;
using Athena.Cache.Redis.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Athena Cache with Redis
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions =>
    {
        athenaOptions.Namespace = "MyApp_PROD";
        athenaOptions.DefaultExpirationMinutes = 60;
        athenaOptions.Logging.LogCacheHitMiss = true;
    },
    redisOptions =>
    {
        redisOptions.ConnectionString = "localhost:6379";
        redisOptions.DatabaseId = 1;
        redisOptions.InstanceName = "MyApp";
    });

var app = builder.Build();

// Add middleware
app.UseRouting();
app.UseAthenaCache();
app.MapControllers();

app.Run();
```

### Configuration from appsettings.json

```json
{
  "AthenaCache": {
    "Namespace": "MyApp_PROD",
    "DefaultExpirationMinutes": 60,
    "Logging": {
      "LogCacheHitMiss": true,
      "LogCacheInvalidation": true
    }
  },
  "RedisCacheOptions": {
    "ConnectionString": "localhost:6379,localhost:6380",
    "DatabaseId": 1,
    "InstanceName": "MyApp",
    "ConnectTimeout": 5000,
    "SyncTimeout": 1000,
    "AsyncTimeout": 1000,
    "ConnectRetry": 3,
    "AbortOnConnectFail": false
  }
}
```

```csharp
// Program.cs with configuration
builder.Services.AddAthenaCacheRedisComplete(
    builder.Configuration.GetSection("AthenaCache"),
    builder.Configuration.GetSection("RedisCacheOptions"));
```

## 🔧 Advanced Configuration

### Redis Connection Options

```csharp
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions => { /* Athena options */ },
    redisOptions =>
    {
        // Connection settings
        redisOptions.ConnectionString = "redis-cluster.example.com:6379";
        redisOptions.DatabaseId = 2;
        redisOptions.InstanceName = "MyApp_Instance1";
        
        // Timeout settings
        redisOptions.ConnectTimeout = 10000;  // 10 seconds
        redisOptions.SyncTimeout = 2000;      // 2 seconds
        redisOptions.AsyncTimeout = 5000;     // 5 seconds
        
        // Retry and resilience
        redisOptions.ConnectRetry = 5;
        redisOptions.AbortOnConnectFail = false;
        
        // Performance tuning
        redisOptions.AllowAdmin = false;
        redisOptions.ChannelPrefix = "cache:";
    });
```

### High Availability Setup

```csharp
// Redis Sentinel configuration
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions => { /* Athena options */ },
    redisOptions =>
    {
        redisOptions.ConnectionString = "sentinel1:26379,sentinel2:26379,sentinel3:26379";
        redisOptions.ServiceName = "mymaster";
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectRetry = 10;
    });

// Redis Cluster configuration
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions => { /* Athena options */ },
    redisOptions =>
    {
        redisOptions.ConnectionString = "node1:7000,node2:7000,node3:7000,node4:7000,node5:7000,node6:7000";
        redisOptions.AbortOnConnectFail = false;
    });
```

## 🌐 Distributed Cache Invalidation

### Automatic Cross-Instance Invalidation

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    [CacheInvalidateOn("Products")]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts()
    {
        // This cache will be automatically invalidated across ALL instances
        // when any instance updates the Products table
        return Ok(await _productService.GetProductsAsync());
    }

    [HttpPost]
    [CacheInvalidateOn("Products")]  // Invalidates cache across all instances
    public async Task<ActionResult<ProductDto>> CreateProduct([FromBody] CreateProductRequest request)
    {
        var product = await _productService.CreateProductAsync(request);
        // Cache invalidation message sent via Redis Pub/Sub to all instances
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
    }
}
```

### Manual Distributed Invalidation

```csharp
[ApiController]
public class CacheManagementController : ControllerBase
{
    private readonly IDistributedCacheInvalidator _invalidator;

    public CacheManagementController(IDistributedCacheInvalidator invalidator)
    {
        _invalidator = invalidator;
    }

    [HttpDelete("cache/distributed/table/{tableName}")]
    public async Task<IActionResult> InvalidateTableAcrossInstances(string tableName)
    {
        // Invalidates cache for the table across ALL connected instances
        await _invalidator.InvalidateByTableAsync(tableName);
        return Ok($"Cache invalidated across all instances for table: {tableName}");
    }

    [HttpDelete("cache/distributed/pattern/{pattern}")]
    public async Task<IActionResult> InvalidateByPatternAcrossInstances(string pattern)
    {
        // Invalidates cache by pattern across ALL connected instances
        await _invalidator.InvalidateByPatternAsync(pattern);
        return Ok($"Cache invalidated across all instances for pattern: {pattern}");
    }
}
```

## 📊 Performance Optimization

### Redis Pipelining

```csharp
// Automatic pipelining for bulk operations
public class BulkCacheOperations
{
    private readonly IAthenaCache _cache;

    public async Task<Dictionary<string, T>> GetMultipleAsync<T>(IEnumerable<string> keys)
    {
        // Athena.Cache.Redis automatically uses pipelining for multiple operations
        var tasks = keys.Select(key => _cache.GetAsync<T>(key));
        var results = await Task.WhenAll(tasks);
        
        return keys.Zip(results, (key, value) => new { key, value })
                  .Where(x => x.value != null)
                  .ToDictionary(x => x.key, x => x.value);
    }
}
```

### Connection Monitoring

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
            DatabaseSize = _redis.GetDatabase().Execute("DBSIZE"),
            Memory = _redis.GetDatabase().Execute("INFO", "memory")
        });
    }

    [HttpGet("redis/stats")]
    public async Task<IActionResult> GetRedisStats()
    {
        var db = _redis.GetDatabase();
        var info = await db.ExecuteAsync("INFO", "stats");
        
        return Ok(new
        {
            Keyspace = await db.ExecuteAsync("INFO", "keyspace"),
            Stats = info.ToString(),
            ClientList = await db.ExecuteAsync("CLIENT", "LIST")
        });
    }
}
```

## 🛡️ Error Handling and Resilience

### Circuit Breaker Configuration

```csharp
// Built-in circuit breaker for Redis operations
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions =>
    {
        athenaOptions.ErrorHandling.OnCacheError = CacheErrorAction.LogAndContinue;
        athenaOptions.ErrorHandling.ThrowOnSerializationError = false;
        athenaOptions.Resilience.EnableCircuitBreaker = true;
        athenaOptions.Resilience.FailureThreshold = 5;
        athenaOptions.Resilience.RecoveryTimeSeconds = 30;
    },
    redisOptions => { /* Redis options */ });
```

### Fallback to Memory Cache

```csharp
// Automatic fallback when Redis is unavailable
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions =>
    {
        athenaOptions.Resilience.EnableFallbackToMemory = true;
        athenaOptions.Resilience.FallbackToMemoryOnError = true;
    },
    redisOptions => { /* Redis options */ });
```

## 🔌 Integration Examples

### With ASP.NET Core Health Checks

```csharp
// Add Redis health checks
builder.Services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis")
    .AddCheck("athena-cache", () =>
    {
        // Custom health check for Athena Cache
        return HealthCheckResult.Healthy("Athena Cache is running");
    });

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### With Background Services

```csharp
public class CacheWarmupService : BackgroundService
{
    private readonly IAthenaCache _cache;
    private readonly ILogger<CacheWarmupService> _logger;

    public CacheWarmupService(IAthenaCache cache, ILogger<CacheWarmupService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Warm up critical cache entries
                await WarmupCriticalCaches();
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cache warmup");
            }
        }
    }

    private async Task WarmupCriticalCaches()
    {
        // Pre-load frequently accessed data
        var criticalData = await GetCriticalDataAsync();
        await _cache.SetAsync("critical_data", criticalData, TimeSpan.FromHours(24));
    }
}
```

## 🔧 Troubleshooting

### Common Issues

1. **Connection Timeouts**
   ```csharp
   redisOptions.ConnectTimeout = 30000;  // Increase timeout
   redisOptions.SyncTimeout = 5000;
   ```

2. **Memory Issues**
   ```csharp
   // Monitor Redis memory usage
   var memoryInfo = await redis.GetDatabase().ExecuteAsync("INFO", "memory");
   ```

3. **Network Partitions**
   ```csharp
   redisOptions.AbortOnConnectFail = false;  // Continue with fallback
   athenaOptions.Resilience.EnableFallbackToMemory = true;
   ```

## 🔌 Integration with Other Packages

Works seamlessly with:
- **[Athena.Cache.Core](https://www.nuget.org/packages/Athena.Cache.Core/)**: Core caching functionality
- **[Athena.Cache.Monitoring](https://www.nuget.org/packages/Athena.Cache.Monitoring/)**: Redis-specific monitoring
- **[Athena.Cache.Analytics](https://www.nuget.org/packages/Athena.Cache.Analytics/)**: Distributed analytics
- **[Athena.Cache.SourceGenerator](https://www.nuget.org/packages/Athena.Cache.SourceGenerator/)**: Compile-time optimization

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/jhbrunoK/Athena.Cache/blob/main/LICENSE.txt) file for details.

## 🐛 Issues & Support

- **Repository**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)
- **Issues**: [https://github.com/jhbrunoK/Athena.Cache/issues](https://github.com/jhbrunoK/Athena.Cache/issues)
- **Documentation**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)