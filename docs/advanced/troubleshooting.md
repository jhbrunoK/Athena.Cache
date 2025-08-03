---
layout: default
title: Troubleshooting
---

# Troubleshooting

This guide helps you diagnose and resolve common issues with Athena.Cache. Most problems fall into specific categories with well-defined solutions.

## Quick Diagnostics

### Check Cache Status

```csharp
[HttpGet("cache/diagnostics")]
public IActionResult GetCacheDiagnostics([FromServices] ICacheHealthChecker healthChecker)
{
    return Ok(new
    {
        Health = healthChecker.GetCurrentHealth(),
        Statistics = healthChecker.GetCacheStatistics(),
        Configuration = healthChecker.GetConfiguration()
    });
}
```

### Enable Debug Logging

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Logging.LogCacheHitMiss = true;
    options.Logging.LogCacheOperations = true;
    options.Logging.LogCacheInvalidation = true;
    options.Logging.LogConfigurationChanges = true;
});
```

## Cache Not Working

### Problem: Cache attributes ignored

**Symptoms:**
- Methods with `[AthenaCache]` always execute
- No cache hits recorded
- Headers missing `X-Athena-Cache` information

**Solutions:**

1. **Verify middleware registration**:
```csharp
// Ensure correct order
app.UseRouting();
app.UseAthenaCache();  // Must be after UseRouting()
app.MapControllers();
```

2. **Check service registration**:
```csharp
// One of these is required
builder.Services.AddAthenaCacheComplete();  // Memory cache
// OR
builder.Services.AddAthenaCacheRedisComplete(...);  // Redis cache
```

3. **Verify controller inheritance**:
```csharp
// Controllers must inherit from ControllerBase
[ApiController]
public class UsersController : ControllerBase  // Required!
{
    [AthenaCache]
    public async Task<UserDto[]> GetUsers() { ... }
}
```

### Problem: Async methods not cached

**Symptoms:**
- Synchronous methods cache correctly
- Async methods always execute

**Solution:**
```csharp
// Incorrect - returns Task<T>
[AthenaCache]
public Task<UserDto[]> GetUsers() { ... }

// Correct - await the task
[AthenaCache] 
public async Task<UserDto[]> GetUsers() { ... }
```

### Problem: Null results cached

**Symptoms:**
- Null values being cached when they shouldn't be

**Solution:**
```csharp
// Configure null caching behavior
[AthenaCache(CacheNullResults = false)]
public async Task<UserDto> GetUser(int id) { ... }

// Or globally
builder.Services.AddAthenaCacheComplete(options =>
{
    options.CacheNullResults = false;
});
```

## Performance Issues

### Problem: High memory usage

**Symptoms:**
- Memory continuously growing
- OutOfMemoryException
- GC pressure warnings

**Solutions:**

1. **Enable memory pressure management**:
```csharp
builder.Services.AddAthenaCacheComplete(options =>
{
    options.MemoryPressure.EnableAutomaticCleanup = true;
    options.MemoryPressure.CleanupThresholdMB = 100;
    options.MemoryPressure.MonitoringIntervalSeconds = 30;
});
```

2. **Review cache expiration**:
```csharp
// Too long expiration
[AthenaCache(ExpirationMinutes = 1440)]  // 24 hours - consider reducing

// Better
[AthenaCache(ExpirationMinutes = 30)]
```

3. **Check for cache key explosion**:
```csharp
// Problematic - too many unique keys
[AthenaCache(KeyPattern = "user_{id}_{timestamp}")]  // timestamp changes constantly

// Better
[AthenaCache(KeyPattern = "user_{id}")]
```

### Problem: Slow cache operations

**Symptoms:**
- High response times
- Cache operations taking longer than direct database calls

**Solutions:**

1. **For Memory Cache**:
```csharp
// Ensure you're not using expensive serialization
options.Serialization.UseOptimizedSerialization = true;
```

2. **For Redis Cache**:
```csharp
// Optimize Redis connection
redisOptions.ConnectTimeout = 5000;   // 5 seconds
redisOptions.SyncTimeout = 1000;      // 1 second
redisOptions.AsyncTimeout = 3000;     // 3 seconds

// Use connection pooling
redisOptions.AbortOnConnectFail = false;
```

3. **Profile cache key generation**:
```csharp
// Simple keys are faster
[AthenaCache(KeyPattern = "user_{id}")]

// Complex keys are slower
[AthenaCache(KeyPattern = "complex_{param1}_{param2}_{param3}_{param4}")]
```

## Cache Invalidation Issues

### Problem: Cache not invalidating

**Symptoms:**
- Stale data returned after updates
- Manual invalidation doesn't work

**Solutions:**

1. **Check invalidation tags match**:
```csharp
[HttpGet]
[AthenaCache]
[CacheInvalidateOn("Users")]  // Tag: "Users"
public async Task<UserDto[]> GetUsers() { ... }

[HttpPost] 
[CacheInvalidateOn("Users")]  // Must match exactly
public async Task<UserDto> CreateUser(...) { ... }
```

2. **Verify method execution**:
```csharp
[HttpPost]
[CacheInvalidateOn("Users")]
public async Task<UserDto> CreateUser(CreateUserRequest request)
{
    // If this throws exception, invalidation won't happen
    var user = await _userService.CreateUserAsync(request);
    
    // Invalidation happens here (after successful execution)
    return user;
}
```

3. **Check distributed invalidation**:
```csharp
// For Redis, ensure IDistributedCacheInvalidator is registered
builder.Services.AddAthenaCacheRedisComplete(...);  // Registers automatically

// For custom scenarios, register manually
builder.Services.AddScoped<IDistributedCacheInvalidator, CustomDistributedInvalidator>();
```

### Problem: Pattern invalidation not working

**Symptoms:**
- Wildcard patterns don't clear expected caches

**Solution:**
```csharp
// Incorrect pattern
[CacheInvalidateOn("Users", InvalidationType.Pattern, "user*")]

// Correct pattern  
[CacheInvalidateOn("Users", InvalidationType.Pattern, "user_*")]

// For keys like "user_123_profile", use:
[CacheInvalidateOn("Users", InvalidationType.Pattern, "user_{id}_*")]
```

## Redis Issues

### Problem: Redis connection failures

**Symptoms:**
- "No connection available" errors
- Intermittent cache failures
- Connection timeout exceptions

**Solutions:**

1. **Configure retry logic**:
```csharp
redisOptions.ConnectRetry = 5;           // Retry 5 times
redisOptions.AbortOnConnectFail = false; // Don't abort on failure
redisOptions.ConnectTimeout = 30000;     // 30 second timeout
```

2. **Enable fallback to memory**:
```csharp
athenaOptions.Resilience.EnableFallbackToMemory = true;
athenaOptions.Resilience.FallbackToMemoryOnError = true;
```

3. **Check Redis server status**:
```bash
# Test Redis connectivity
redis-cli ping

# Check Redis info
redis-cli info server
redis-cli info memory
```

### Problem: Redis authentication failures

**Symptoms:**
- "NOAUTH Authentication required" errors
- "ERR invalid password" errors

**Solutions:**

1. **Check connection string format**:
```csharp
// Correct format with password
redisOptions.ConnectionString = "localhost:6379,password=mypassword";

// For Azure Redis Cache
redisOptions.ConnectionString = "cache.redis.cache.windows.net:6380,password=key==,ssl=true";
```

2. **Verify Redis AUTH configuration**:
```bash
# Test authentication
redis-cli -h localhost -p 6379 -a mypassword ping
```

### Problem: Redis memory issues

**Symptoms:**
- "OOM command not allowed" errors
- Redis running out of memory

**Solutions:**

1. **Configure Redis memory policy**:
```bash
# In redis.conf
maxmemory 2gb
maxmemory-policy allkeys-lru
```

2. **Monitor Redis memory usage**:
```bash
redis-cli info memory
```

3. **Set appropriate cache expiration**:
```csharp
// Avoid very long expiration times
[AthenaCache(ExpirationMinutes = 60)]  // Instead of 1440 (24 hours)
```

## Source Generator Issues

### Problem: Generated code not found

**Symptoms:**
- "Type 'AthenaCacheConfiguration' not found" errors
- Build succeeds but no generated files

**Solutions:**

1. **Verify package reference**:
```xml
<PackageReference Include="Athena.Cache.SourceGenerator" Version="1.0.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

2. **Clean and rebuild**:
```bash
dotnet clean
dotnet build
```

3. **Check generated files**:
```xml
<!-- Enable generated files output for debugging -->
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

### Problem: AOT compilation failures

**Symptoms:**
- Build fails with AOT enabled
- Reflection-related errors during publish

**Solutions:**

1. **Ensure Source Generator is installed**:
```bash
dotnet add package Athena.Cache.SourceGenerator
```

2. **Verify no reflection usage**:
```csharp
// Avoid runtime reflection in AOT scenarios
// The Source Generator eliminates reflection automatically
```

3. **Check AOT configuration**:
```xml
<PropertyGroup>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

## Serialization Issues

### Problem: Serialization failures

**Symptoms:**
- "Unable to serialize type" errors
- Complex objects not cached properly

**Solutions:**

1. **Ensure types are serializable**:
```csharp
// For JSON serialization, ensure properties are public
public class UserDto
{
    public int Id { get; set; }          // ✅ Good
    public string Name { get; set; }     // ✅ Good
    private string Secret { get; set; }  // ❌ Won't serialize
}
```

2. **Configure custom serialization**:
```csharp
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Serialization.SerializerType = SerializerType.SystemTextJson;
    options.Serialization.JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
});
```

3. **Handle circular references**:
```csharp
// Configure JSON options to handle circular references
options.Serialization.JsonOptions = new JsonSerializerOptions
{
    ReferenceHandler = ReferenceHandler.Preserve
};
```

## Configuration Issues

### Problem: Settings not applied

**Symptoms:**
- Configuration changes don't take effect
- Default values used instead of configured values

**Solutions:**

1. **Verify configuration section binding**:
```csharp
// Ensure section name matches
builder.Services.AddAthenaCacheComplete(
    builder.Configuration.GetSection("AthenaCache"));  // Must match appsettings.json
```

2. **Check appsettings.json structure**:
```json
{
  "AthenaCache": {
    "Namespace": "MyApp",
    "DefaultExpirationMinutes": 30,
    "Logging": {
      "LogCacheHitMiss": true
    }
  }
}
```

3. **Validate configuration at startup**:
```csharp
builder.Services.AddAthenaCacheComplete(options =>
{
    // Log configuration for debugging
    Console.WriteLine($"Cache namespace: {options.Namespace}");
    Console.WriteLine($"Default expiration: {options.DefaultExpirationMinutes}");
});
```

## Performance Profiling

### Identify Bottlenecks

```csharp
[HttpGet("cache/performance")]
public IActionResult GetCachePerformance([FromServices] ICacheStatistics stats)
{
    return Ok(new
    {
        HitRate = stats.HitRate,
        AverageResponseTime = stats.AverageResponseTime,
        TotalOperations = stats.TotalOperations,
        CacheSize = stats.CacheSize,
        MemoryUsage = stats.MemoryUsage,
        TopSlowKeys = stats.GetTopSlowKeys(10),
        TopFrequentKeys = stats.GetTopFrequentKeys(10)
    });
}
```

### Memory Analysis

```csharp
[HttpGet("memory/analysis")]
public IActionResult GetMemoryAnalysis()
{
    return Ok(new
    {
        TotalMemory = GC.GetTotalMemory(false),
        WorkingSet = Environment.WorkingSet,
        Gen0Collections = GC.CollectionCount(0),
        Gen1Collections = GC.CollectionCount(1),
        Gen2Collections = GC.CollectionCount(2),
        StringPoolStats = HighPerformanceStringPool.GetStatistics(),
        CollectionPoolStats = CollectionPools.GetStatistics()
    });
}
```

## Debugging Tools

### Enable Detailed Logging

```csharp
// Program.cs
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

// appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Athena.Cache": "Debug",
      "Microsoft.Extensions.Caching": "Debug"
    }
  }
}
```

### Request Tracking

```csharp
[HttpGet("cache/request-tracking")]
public IActionResult GetRequestTracking([FromServices] ICacheRequestTracker tracker)
{
    return Ok(new
    {
        RecentRequests = tracker.GetRecentRequests(50),
        SlowRequests = tracker.GetSlowRequests(10),
        FailedRequests = tracker.GetFailedRequests(10)
    });
}
```

### Health Checks

```csharp
// Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<AthenaCacheHealthCheck>("athena-cache")
    .AddCheck<RedisHealthCheck>("redis");

app.MapHealthChecks("/health");
```

## Common Error Codes

| Error Code | Description | Solution |
|------------|-------------|----------|
| ATHENA001 | Cache service not registered | Add `AddAthenaCacheComplete()` to services |
| ATHENA002 | Middleware not configured | Add `UseAthenaCache()` after `UseRouting()` |
| ATHENA003 | Redis connection failed | Check Redis server and connection string |
| ATHENA004 | Serialization failed | Ensure types are serializable |
| ATHENA005 | Invalid cache key pattern | Check key pattern syntax |
| ATHENA006 | Memory pressure detected | Enable automatic cleanup or increase memory |
| ATHENA007 | Invalidation target not found | Verify invalidation table/pattern names |

## Getting Help

1. **Enable verbose logging** and check logs for specific error messages
2. **Use diagnostic endpoints** to gather cache health information
3. **Check GitHub issues** for similar problems
4. **Profile memory usage** if experiencing performance issues
5. **Verify Redis connectivity** for distributed cache scenarios

## Related Documentation

- **[Installation]({{ site.baseurl }}/getting-started/installation)** - Setup verification
- **[Performance]({{ site.baseurl }}/performance/zero-memory-optimization)** - Performance optimization
- **[Redis Setup]({{ site.baseurl }}/distributed/redis-setup)** - Redis configuration
- **[Monitoring]({{ site.baseurl }}/monitoring/real-time-dashboards)** - Monitoring and alerts