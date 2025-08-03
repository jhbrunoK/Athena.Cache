---
layout: default
title: Production Tuning
---

# Production Tuning

Optimize Athena.Cache for maximum performance in production environments. This guide covers configuration, monitoring, and optimization techniques for high-traffic applications.

## Performance Assessment

Before optimizing, establish baseline performance metrics.

### Baseline Measurement

```csharp
[HttpGet("performance/baseline")]
public async Task<IActionResult> GetPerformanceBaseline([FromServices] ICacheStatistics stats)
{
    return Ok(new
    {
        // Cache effectiveness
        HitRate = stats.HitRate,
        MissRate = stats.MissRate,
        TotalRequests = stats.TotalRequests,
        
        // Performance metrics
        AverageResponseTime = stats.AverageResponseTime,
        P95ResponseTime = stats.GetPercentileResponseTime(95),
        P99ResponseTime = stats.GetPercentileResponseTime(99),
        
        // Resource usage
        MemoryUsage = stats.MemoryUsage,
        CacheSize = stats.CacheSize,
        KeyCount = stats.KeyCount,
        
        // Throughput
        RequestsPerSecond = stats.RequestsPerSecond,
        CacheOperationsPerSecond = stats.CacheOperationsPerSecond
    });
}
```

### Load Testing Setup

```csharp
// Configure for load testing
public class LoadTestStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddAthenaCacheComplete(options =>
        {
            // Optimize for high throughput
            options.Performance.MaxConcurrentOperations = 1000;
            options.Performance.OperationTimeout = TimeSpan.FromSeconds(30);
            
            // Enable detailed metrics
            options.Monitoring.EnableDetailedMetrics = true;
            options.Monitoring.MetricsCollectionInterval = TimeSpan.FromSeconds(10);
        });
    }
}
```

## Memory Optimization

Optimize memory usage for production workloads.

### Memory Configuration

```csharp
builder.Services.AddAthenaCacheComplete(options =>
{
    // Memory pressure management
    options.MemoryPressure.EnableAutomaticCleanup = true;
    options.MemoryPressure.CleanupThresholdMB = 512;      // Start cleanup at 512MB
    options.MemoryPressure.AggressiveCleanupThresholdMB = 1024; // Aggressive at 1GB
    options.MemoryPressure.MonitoringIntervalSeconds = 15; // Check every 15 seconds
    
    // Memory optimization features
    options.MemoryOptimization.EnableStringPooling = true;
    options.MemoryOptimization.EnableCollectionPooling = true;
    options.MemoryOptimization.EnableValueTypeOptimizations = true;
    options.MemoryOptimization.EnableLazyInitialization = true;
    
    // String pool tuning
    options.StringPool.MaxPoolSize = 20000;              // Increase pool size
    options.StringPool.MaxStringLength = 2000;           // Allow longer strings
    options.StringPool.CleanupIntervalMinutes = 5;       // More frequent cleanup
    
    // Collection pool tuning
    options.CollectionPools.MaxDictionaryPoolSize = 200;
    options.CollectionPools.MaxListPoolSize = 500;
    options.CollectionPools.MaxArrayPoolSize = 100;
});
```

### Memory Monitoring

```csharp
[HttpGet("performance/memory")]
public IActionResult GetMemoryStats([FromServices] ICacheMemoryManager memory)
{
    return Ok(new
    {
        // Overall memory usage
        TotalMemoryUsage = GC.GetTotalMemory(false),
        WorkingSet = Environment.WorkingSet,
        
        // GC statistics
        Gen0Collections = GC.CollectionCount(0),
        Gen1Collections = GC.CollectionCount(1),
        Gen2Collections = GC.CollectionCount(2),
        TotalPauseDuration = GC.GetTotalPauseDuration(),
        
        // Pool statistics
        StringPoolStats = new
        {
            TotalStrings = memory.StringPool.TotalStrings,
            HitRate = memory.StringPool.HitRate,
            MemorySaved = memory.StringPool.EstimatedMemorySaved
        },
        
        CollectionPoolStats = new
        {
            ListPoolHitRate = memory.CollectionPools.ListPool.HitRate,
            DictionaryPoolHitRate = memory.CollectionPools.DictionaryPool.HitRate,
            TotalPooledObjects = memory.CollectionPools.TotalPooledObjects
        },
        
        // Memory pressure
        MemoryPressureLevel = memory.MemoryPressureLevel,
        AutoCleanupTriggered = memory.AutoCleanupTriggered,
        LastCleanupTime = memory.LastCleanupTime
    });
}
```

## Redis Production Configuration

Optimize Redis settings for production workloads.

### Connection Optimization

```csharp
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions =>
    {
        athenaOptions.Namespace = "Production";
        athenaOptions.DefaultExpirationMinutes = 60;
    },
    redisOptions =>
    {
        redisOptions.ConnectionString = connectionString;
        
        // Connection pool optimization
        redisOptions.ConnectTimeout = 10000;    // 10 seconds
        redisOptions.SyncTimeout = 2000;        // 2 seconds  
        redisOptions.AsyncTimeout = 5000;       // 5 seconds
        redisOptions.ConnectRetry = 5;          // Retry 5 times
        redisOptions.AbortOnConnectFail = false; // Don't abort on failure
        
        // Performance settings
        redisOptions.AllowAdmin = false;         // Disable admin commands
        redisOptions.ChannelPrefix = "cache:";   // Shorter prefix
        redisOptions.DatabaseId = 1;             // Dedicated database
        
        // Connection pooling
        redisOptions.ClientName = $"AthenaCache-{Environment.MachineName}";
    });
```

### Redis Server Configuration

```bash
# redis.conf for production
# Memory settings
maxmemory 4gb
maxmemory-policy allkeys-lru

# Persistence settings (adjust based on needs)
save 900 1
save 300 10  
save 60 10000
appendonly yes
appendfsync everysec

# Network settings
tcp-keepalive 300
timeout 0

# Performance settings
tcp-backlog 511
databases 16

# Logging
loglevel notice
logfile "/var/log/redis/redis-server.log"

# Security
bind 127.0.0.1 ::1
protected-mode yes
requirepass your-strong-password
```

### Connection Health Monitoring

```csharp
[HttpGet("performance/redis")]
public async Task<IActionResult> GetRedisPerformance([FromServices] IConnectionMultiplexer redis)
{
    var database = redis.GetDatabase();
    var server = redis.GetServer(redis.GetEndPoints().First());
    
    // Connection statistics
    var counters = redis.GetCounters();
    
    // Server info
    var serverInfo = await server.InfoAsync("server");
    var memoryInfo = await server.InfoAsync("memory");
    var statsInfo = await server.InfoAsync("stats");
    
    return Ok(new
    {
        ConnectionInfo = new
        {
            IsConnected = redis.IsConnected,
            ConnectionCount = counters.Interactive.ConnectionCount,
            TotalOutstanding = counters.TotalOutstanding,
            ClientName = redis.ClientName
        },
        
        ServerInfo = new
        {
            RedisVersion = serverInfo.FirstOrDefault(x => x.Key == "redis_version")?.Value,
            UptimeInSeconds = serverInfo.FirstOrDefault(x => x.Key == "uptime_in_seconds")?.Value,
            ConnectedClients = statsInfo.FirstOrDefault(x => x.Key == "connected_clients")?.Value
        },
        
        MemoryInfo = new
        {
            UsedMemory = memoryInfo.FirstOrDefault(x => x.Key == "used_memory")?.Value,
            UsedMemoryRss = memoryInfo.FirstOrDefault(x => x.Key == "used_memory_rss")?.Value,
            MaxMemory = memoryInfo.FirstOrDefault(x => x.Key == "maxmemory")?.Value
        },
        
        PerformanceStats = new
        {
            TotalCommandsProcessed = statsInfo.FirstOrDefault(x => x.Key == "total_commands_processed")?.Value,
            InstantaneousOpsPerSec = statsInfo.FirstOrDefault(x => x.Key == "instantaneous_ops_per_sec")?.Value,
            KeyspaceHits = statsInfo.FirstOrDefault(x => x.Key == "keyspace_hits")?.Value,
            KeyspaceMisses = statsInfo.FirstOrDefault(x => x.Key == "keyspace_misses")?.Value
        }
    });
}
```

## Cache Key Optimization

Optimize cache key generation for better performance.

### Efficient Key Patterns

```csharp
// Good: Simple, predictable patterns
[AthenaCache(KeyPattern = "user_{id}")]
public async Task<UserDto> GetUser(int id) { ... }

// Good: Include essential parameters only
[AthenaCache(KeyPattern = "products_{categoryId}_{page}")]
public async Task<ProductDto[]> GetProducts(int categoryId, int page = 1) { ... }

// Avoid: Complex patterns with many parameters
// [AthenaCache(KeyPattern = "complex_{param1}_{param2}_{param3}_{param4}_{param5}")]
// public async Task<ComplexDto> GetComplexData(...) { ... }
```

### Key Length Optimization

```csharp
// Configure key optimization
builder.Services.AddAthenaCacheComplete(options =>
{
    options.KeyGeneration.MaxKeyLength = 100;           // Limit key length
    options.KeyGeneration.UseShortControllerNames = true; // "Usr" instead of "Users"
    options.KeyGeneration.UseShortMethodNames = true;     // "Get" instead of "GetAll"
    options.KeyGeneration.HashLongKeys = true;            // Hash keys > MaxKeyLength
});
```

### Key Compression

```csharp
public class OptimizedKeyGenerator : ICacheKeyGenerator
{
    public string GenerateKey(ControllerActionDescriptor actionDescriptor, object[] parameters)
    {
        var controller = GetShortControllerName(actionDescriptor.ControllerName);
        var action = GetShortActionName(actionDescriptor.ActionName);
        var paramHash = HashParameters(parameters);
        
        return $"{controller}:{action}:{paramHash}";
    }
    
    private string GetShortControllerName(string controllerName)
    {
        // Map common controller names to shorter versions
        return controllerName switch
        {
            "UsersController" => "Usr",
            "ProductsController" => "Prd",
            "OrdersController" => "Ord",
            _ => controllerName.Substring(0, Math.Min(3, controllerName.Length))
        };
    }
    
    private string HashParameters(object[] parameters)
    {
        if (parameters?.Length == 0) return "";
        
        // Use fast hash for parameter combinations
        var hash = System.HashCode.Combine(parameters);
        return hash.ToString("x8"); // 8-character hex
    }
}
```

## Serialization Performance

Optimize data serialization for faster cache operations.

### Serialization Configuration

```csharp
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Serialization.SerializerType = SerializerType.SystemTextJson; // Fastest
    options.Serialization.JsonOptions = new JsonSerializerOptions
    {
        // Performance optimizations
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,  // Smaller payload
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        
        // Advanced optimizations
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
    
    // Enable compression for large objects
    options.Serialization.EnableCompression = true;
    options.Serialization.CompressionThreshold = 1024; // Compress objects > 1KB
});
```

### Custom Serialization

```csharp
public class HighPerformanceSerializer : ICacheSerializer
{
    private readonly JsonSerializerOptions _options;
    private readonly ArrayPool<byte> _bytePool;

    public HighPerformanceSerializer()
    {
        _bytePool = ArrayPool<byte>.Shared;
        _options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
    }

    public byte[] Serialize<T>(T obj)
    {
        if (obj == null) return Array.Empty<byte>();

        var buffer = _bytePool.Rent(4096);
        try
        {
            using var stream = new MemoryStream(buffer);
            using var writer = new Utf8JsonWriter(stream);
            JsonSerializer.Serialize(writer, obj, _options);
            
            var result = new byte[stream.Position];
            Array.Copy(buffer, result, stream.Position);
            return result;
        }
        finally
        {
            _bytePool.Return(buffer);
        }
    }

    public T Deserialize<T>(byte[] data)
    {
        if (data?.Length == 0) return default(T);
        
        return JsonSerializer.Deserialize<T>(data, _options);
    }
}
```

## Concurrent Access Optimization

Handle high concurrency efficiently.

### Concurrency Configuration

```csharp
builder.Services.AddAthenaCacheComplete(options =>
{
    // Concurrency settings
    options.Concurrency.MaxConcurrentOperations = 500;
    options.Concurrency.OperationTimeout = TimeSpan.FromSeconds(30);
    options.Concurrency.EnableConcurrentRefresh = true;
    
    // Lock optimization
    options.Concurrency.LockTimeout = TimeSpan.FromMilliseconds(100);
    options.Concurrency.UseFairLocking = true;
    options.Concurrency.MaxLockWaitTime = TimeSpan.FromSeconds(5);
});
```

### Async Operation Optimization

```csharp
[HttpGet("batch-data")]
public async Task<BatchDataDto> GetBatchData([FromQuery] int[] ids)
{
    // Parallel cache operations
    var tasks = ids.Select(async id =>
    {
        var cacheKey = $"data_{id}";
        var cached = await _cache.GetAsync<DataDto>(cacheKey);
        
        if (cached != null) return cached;
        
        var data = await _dataService.GetDataAsync(id);
        await _cache.SetAsync(cacheKey, data, TimeSpan.FromMinutes(30));
        
        return data;
    });
    
    var results = await Task.WhenAll(tasks);
    
    return new BatchDataDto { Items = results };
}
```

## Environment-Specific Optimizations

### Production Environment

```csharp
// Production optimizations
if (builder.Environment.IsProduction())
{
    builder.Services.AddAthenaCacheComplete(options =>
    {
        // Aggressive optimization for production
        options.MemoryOptimization.EnableStringPooling = true;
        options.MemoryOptimization.EnableCollectionPooling = true;
        options.MemoryOptimization.EnableValueTypeOptimizations = true;
        
        // Higher memory thresholds for production
        options.MemoryPressure.CleanupThresholdMB = 1024;
        options.MemoryPressure.MonitoringIntervalSeconds = 30;
        
        // Performance monitoring
        options.Monitoring.EnableDetailedMetrics = true;
        options.Logging.LogSlowOperations = true;
        options.Logging.SlowOperationThresholdMs = 100;
    });
}
```

### Development Environment

```csharp
// Development optimizations
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAthenaCacheComplete(options =>
    {
        // Shorter cache times for development
        options.DefaultExpirationMinutes = 5;
        
        // More aggressive cleanup for testing
        options.MemoryPressure.CleanupThresholdMB = 50;
        options.MemoryPressure.MonitoringIntervalSeconds = 10;
        
        // Detailed logging for development
        options.Logging.LogCacheHitMiss = true;
        options.Logging.LogCacheOperations = true;
        options.Logging.LogCacheInvalidation = true;
    });
}
```

## Performance Monitoring and Alerting

### Real-time Performance Dashboard

```csharp
[HttpGet("performance/realtime")]
public async Task<IActionResult> GetRealtimePerformance(
    [FromServices] ICacheStatistics stats,
    [FromServices] ICacheHealthChecker health)
{
    var currentStats = await stats.GetCurrentStatsAsync();
    var healthStatus = await health.CheckHealthAsync();
    
    return Ok(new
    {
        Timestamp = DateTimeOffset.UtcNow,
        
        Performance = new
        {
            HitRate = currentStats.HitRate,
            RequestsPerSecond = currentStats.RequestsPerSecond,
            AverageResponseTime = currentStats.AverageResponseTime,
            P95ResponseTime = currentStats.P95ResponseTime,
            ErrorRate = currentStats.ErrorRate
        },
        
        Resources = new
        {
            MemoryUsage = currentStats.MemoryUsage,
            CpuUsage = currentStats.CpuUsage,
            DiskUsage = currentStats.DiskUsage,
            NetworkLatency = currentStats.NetworkLatency
        },
        
        Health = new
        {
            Status = healthStatus.Status,
            ChecksPerformed = healthStatus.ChecksPerformed,
            Uptime = healthStatus.Uptime,
            Issues = healthStatus.Issues
        }
    });
}
```

### Performance Alerts

```csharp
public class PerformanceAlertService : BackgroundService
{
    private readonly ICacheStatistics _stats;
    private readonly ILogger<PerformanceAlertService> _logger;
    private readonly PerformanceThresholds _thresholds;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var stats = await _stats.GetCurrentStatsAsync();
            
            // Check performance thresholds
            if (stats.HitRate < _thresholds.MinHitRate)
            {
                _logger.LogWarning("Cache hit rate below threshold: {HitRate}%", stats.HitRate);
            }
            
            if (stats.AverageResponseTime > _thresholds.MaxResponseTime)
            {
                _logger.LogWarning("Response time above threshold: {ResponseTime}ms", stats.AverageResponseTime);
            }
            
            if (stats.MemoryUsage > _thresholds.MaxMemoryUsage)
            {
                _logger.LogWarning("Memory usage above threshold: {MemoryUsage}MB", stats.MemoryUsage);
            }
            
            if (stats.ErrorRate > _thresholds.MaxErrorRate)
            {
                _logger.LogError("Error rate above threshold: {ErrorRate}%", stats.ErrorRate);
            }
            
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}

public class PerformanceThresholds
{
    public double MinHitRate { get; set; } = 70.0;           // 70%
    public TimeSpan MaxResponseTime { get; set; } = TimeSpan.FromMilliseconds(100);
    public long MaxMemoryUsage { get; set; } = 512 * 1024 * 1024; // 512MB
    public double MaxErrorRate { get; set; } = 1.0;         // 1%
}
```

## Benchmarking and Load Testing

### Performance Benchmarks

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class CachePerformanceBenchmarks
{
    private readonly ICacheService _cache;
    private readonly TestData[] _testData;

    [Params(100, 1000, 10000)]
    public int DataSetSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Initialize test data
        _testData = GenerateTestData(DataSetSize);
    }

    [Benchmark]
    public async Task CacheWrite()
    {
        foreach (var data in _testData)
        {
            await _cache.SetAsync($"test_{data.Id}", data, TimeSpan.FromMinutes(30));
        }
    }

    [Benchmark]
    public async Task CacheRead()
    {
        foreach (var data in _testData)
        {
            await _cache.GetAsync<TestData>($"test_{data.Id}");
        }
    }

    [Benchmark]
    public async Task CacheMixed()
    {
        var tasks = _testData.Select(async (data, index) =>
        {
            if (index % 3 == 0) // 33% writes
            {
                await _cache.SetAsync($"test_{data.Id}", data, TimeSpan.FromMinutes(30));
            }
            else // 67% reads
            {
                await _cache.GetAsync<TestData>($"test_{data.Id}");
            }
        });

        await Task.WhenAll(tasks);
    }
}
```

### Load Testing Configuration

```csharp
// NBomber load test example
var scenario = Scenario.Create("cache_load_test", async context =>
{
    var userId = Random.Shared.Next(1, 1000);
    var response = await httpClient.GetAsync($"/api/users/{userId}");
    
    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithLoadSimulations(
    Simulation.InjectPerSec(rate: 100, during: TimeSpan.FromMinutes(5)),
    Simulation.KeepConstant(copies: 50, during: TimeSpan.FromMinutes(10))
);

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();
```

## Best Practices Summary

### 1. Memory Management
- Enable all memory optimizations in production
- Set appropriate memory pressure thresholds
- Monitor GC pressure and optimize accordingly

### 2. Redis Configuration
- Use dedicated Redis database for cache
- Configure appropriate timeouts and retry policies
- Monitor Redis performance metrics

### 3. Cache Design
- Use efficient key patterns
- Optimize serialization settings
- Implement proper expiration strategies

### 4. Monitoring
- Set up performance alerts
- Monitor hit rates and response times
- Track memory usage and trends

### 5. Testing
- Load test with production-like data
- Benchmark critical cache operations
- Validate performance under stress

For related topics:
- **[Zero Memory Optimization]({{ site.baseurl }}/performance/zero-memory-optimization)** - Memory optimization techniques
- **[Source Generator]({{ site.baseurl }}/performance/source-generator)** - Compile-time optimizations  
- **[Redis Setup]({{ site.baseurl }}/distributed/redis-setup)** - Redis configuration
- **[Monitoring]({{ site.baseurl }}/monitoring/performance-metrics)** - Performance tracking