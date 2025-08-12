# Invalidus.Redis

**High-Performance Redis Provider for Invalidus** - The Universal Cache Invalidation Engine

## 🚀 What is Invalidus.Redis?

Invalidus.Redis is a powerful Redis provider that seamlessly integrates with the Invalidus ecosystem to provide:

- **🔥 High-Performance Caching**: Built on StackExchange.Redis with optimizations
- **🎯 Smart Invalidation**: Pattern-based, batch, and hierarchical cache invalidation
- **⚡ Advanced Operations**: Pipelines, transactions, and circuit breakers
- **📊 Rich Monitoring**: Comprehensive statistics and health checks
- **🛠️ Production Ready**: Failure handling, retry policies, and connection management

## 📦 Installation

```bash
dotnet add package Invalidus.Redis
```

## 🚀 Quick Start

### Basic Setup

```csharp
using Invalidus.Redis.Extensions;

// Program.cs or Startup.cs
builder.Services.AddInvalidusRedis("localhost:6379", options =>
{
    options.Database = 0;
    options.KeyPrefix = "myapp";
    options.DefaultExpiration = TimeSpan.FromMinutes(30);
});
```

### Environment-Specific Setup

```csharp
// Development
builder.Services.AddInvalidusRedisDevelopment("localhost:6379");

// Production  
builder.Services.AddInvalidusRedisProduction("production-redis:6379");

// High-Performance
builder.Services.AddInvalidusRedisHighPerformance("redis-cluster:6379");
```

### Configuration File Setup

```csharp
// appsettings.json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "Invalidus": {
    "Redis": {
      "Database": 0,
      "KeyPrefix": "myapp",
      "DefaultExpiration": "00:30:00",
      "ScanPageSize": 1000,
      "UseTransaction": true,
      "Logging": {
        "LogInvalidation": true,
        "LogCacheOperations": false
      }
    }
  }
}

// Program.cs
builder.Services.AddInvalidusRedisFromConfiguration(builder.Configuration);
```

## 💡 Features

### Cache Operations

```csharp
public class UserService
{
    private readonly ICacheProvider _cache;
    
    public UserService(ICacheProvider cache)
    {
        _cache = cache;
    }
    
    public async Task<User?> GetUserAsync(string userId)
    {
        // Get from cache
        var user = await _cache.GetAsync<User>($"user:{userId}");
        if (user != null) return user;
        
        // Load from database
        user = await _userRepository.GetByIdAsync(userId);
        
        // Cache with expiration
        await _cache.SetAsync($"user:{userId}", user, TimeSpan.FromHours(1));
        
        return user;
    }
    
    public async Task<User?> GetOrCreateUserAsync(string userId)
    {
        // GetOrSet pattern
        return await _cache.GetOrSetAsync($"user:{userId}", 
            async ct => await _userRepository.GetByIdAsync(userId),
            TimeSpan.FromHours(1));
    }
}
```

### Batch Operations

```csharp
// Get multiple users at once
var userIds = new[] { "user1", "user2", "user3" };
var users = await _cache.GetManyAsync<User>(userIds.Select(id => $"user:{id}"));

// Set multiple users at once
var userDict = new Dictionary<string, User>
{
    ["user:1"] = user1,
    ["user:2"] = user2,
    ["user:3"] = user3
};
await _cache.SetManyAsync(userDict, TimeSpan.FromHours(1));
```

### Invalidation Operations

```csharp
// Remove single key
await _cache.RemoveAsync("user:123");

// Remove multiple keys
await _cache.RemoveManyAsync(new[] { "user:1", "user:2", "user:3" });

// Pattern-based removal (powerful!)
await _cache.RemoveByPatternAsync("user:*");              // All users
await _cache.RemoveByPatternAsync("user:123:*");          // All data for user 123
await _cache.RemoveByPatternAsync("product:category:*");  // All products in category

// Scan keys before removal (safety check)
var keysToRemove = await _cache.ScanKeysAsync("user:temp:*");
Console.WriteLine($"Found {keysToRemove.Count()} temporary user keys");
await _cache.RemoveManyAsync(keysToRemove);
```

### Advanced Operations

```csharp
// Set TTL on existing key
await _cache.SetExpiryAsync("user:123", TimeSpan.FromMinutes(5));

// Get remaining TTL
var ttl = await _cache.GetTtlAsync("user:123");
Console.WriteLine($"Key expires in: {ttl}");

// Check if key exists
if (await _cache.ExistsAsync("user:123"))
{
    Console.WriteLine("User is cached");
}
```

### Statistics & Monitoring

```csharp
// Get comprehensive statistics
var stats = await _cache.GetStatisticsAsync();
Console.WriteLine($"Hit Ratio: {stats.HitRatio:P2}");
Console.WriteLine($"Total Keys: {stats.TotalKeys:N0}");
Console.WriteLine($"Memory Usage: {stats.DatabaseSize:N0} bytes");

// Health check
var healthCheck = await _cache.GetHealthCheckAsync();
if (healthCheck.IsHealthy)
{
    Console.WriteLine($"Redis is healthy (Response: {healthCheck.ResponseTime.TotalMilliseconds}ms)");
}
else
{
    Console.WriteLine($"Redis issues: {healthCheck.Description}");
}
```

## ⚙️ Configuration Options

### Basic Configuration

```csharp
services.AddInvalidusRedis("localhost:6379", options =>
{
    // Connection settings
    options.Database = 0;
    options.KeyPrefix = "myapp";
    options.ConnectTimeout = TimeSpan.FromSeconds(30);
    options.CommandTimeout = TimeSpan.FromSeconds(10);
    
    // Cache settings
    options.DefaultExpiration = TimeSpan.FromMinutes(30);
    
    // Performance settings
    options.ScanPageSize = 1000;
    options.MaxScanResults = 10000;
    options.BatchSize = 100;
    options.UseTransaction = true;
    options.UsePipeline = true;
});
```

### Logging Configuration

```csharp
options.Logging.LogCacheOperations = true;   // Log Get/Set operations
options.Logging.LogInvalidation = true;      // Log Remove operations
options.Logging.LogPerformanceMetrics = true; // Log performance data
options.Logging.LogConnectionEvents = true;   // Log Redis connection events
options.Logging.LogDebugInfo = false;        // Detailed debug logs
```

### Failure Handling

```csharp
options.FailureHandling.ThrowOnRedisError = false;      // Graceful degradation
options.FailureHandling.ThrowOnJsonError = false;       // Handle serialization errors
options.FailureHandling.MaxRetryAttempts = 3;           // Retry failed operations
options.FailureHandling.RetryDelay = TimeSpan.FromSeconds(1);
options.FailureHandling.EnableCircuitBreaker = true;    // Protect Redis from overload
options.FailureHandling.CircuitBreakerFailureThreshold = 5;
```

## 🎚️ Environment Presets

### Development
- **Debug Logging**: Enabled
- **Error Throwing**: Enabled for fast feedback
- **Small Batches**: For easier debugging
- **Short Expiration**: 5-minute default

### Production  
- **Minimal Logging**: Only errors and invalidation
- **Graceful Degradation**: No exceptions on Redis errors
- **Optimized Batching**: Larger batches for efficiency
- **Circuit Breaker**: Enabled for protection
- **Long Expiration**: 1-hour default

### High Performance
- **No Logging**: Maximum speed
- **No Transactions**: Faster individual operations
- **Large Batches**: Maximum throughput
- **No Circuit Breaker**: Minimal overhead
- **Fast Failure**: Single retry attempt

## 🔥 Performance Tips

### 1. Use Batch Operations
```csharp
// ❌ Inefficient - Multiple round trips
foreach (var key in keys)
{
    await cache.RemoveAsync(key);
}

// ✅ Efficient - Single round trip  
await cache.RemoveManyAsync(keys);
```

### 2. Leverage Pattern Operations
```csharp
// ❌ Inefficient - Get all keys then filter
var allKeys = await cache.ScanKeysAsync("*");
var userKeys = allKeys.Where(k => k.StartsWith("user:"));
await cache.RemoveManyAsync(userKeys);

// ✅ Efficient - Direct pattern match
await cache.RemoveByPatternAsync("user:*");
```

### 3. Use Appropriate Expiration
```csharp
// Different data, different expiration strategies
await cache.SetAsync("user:profile", user, TimeSpan.FromHours(1));      // Changes occasionally
await cache.SetAsync("user:session", session, TimeSpan.FromMinutes(20)); // Security sensitive
await cache.SetAsync("app:config", config, TimeSpan.FromDays(1));       // Rarely changes
```

### 4. Implement Cache-Aside Pattern
```csharp
public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiration)
{
    return await _cache.GetOrSetAsync(key, async ct => await factory(), expiration);
}
```

## 🚨 Best Practices

### 1. Key Naming Convention
```csharp
// Use hierarchical, predictable patterns
"user:{userId}"                    // ✅ Good
"user:{userId}:profile"           // ✅ Good  
"user:{userId}:sessions:{sessionId}" // ✅ Good
"userdata123"                     // ❌ Bad - not pattern-friendly
```

### 2. Expiration Strategy
```csharp
// Set expiration on all keys (avoid memory leaks)
await cache.SetAsync(key, value, TimeSpan.FromMinutes(30));

// Use sliding expiration for frequently accessed data
var slidingExpiration = TimeSpan.FromMinutes(20);
await cache.SetAsync(key, value, slidingExpiration);
```

### 3. Error Handling
```csharp
try
{
    var value = await cache.GetAsync<MyData>(key);
    if (value == null)
    {
        value = await LoadFromDatabase(key);
        await cache.SetAsync(key, value, TimeSpan.FromMinutes(30));
    }
    return value;
}
catch (Exception ex)
{
    // Log error but don't fail the operation
    _logger.LogWarning(ex, "Cache operation failed for key {Key}", key);
    return await LoadFromDatabase(key); // Fallback to source
}
```

### 4. Connection Management
```csharp
// ✅ Let Invalidus manage the connection (recommended)
services.AddInvalidusRedis("localhost:6379");

// ✅ Or reuse existing ConnectionMultiplexer
services.AddSingleton<IConnectionMultiplexer>(/* your existing connection */);
services.AddInvalidusRedis(serviceProvider.GetService<IConnectionMultiplexer>());
```

## 🔗 Integration with Invalidus Ecosystem

Invalidus.Redis works seamlessly with other Invalidus packages:

```csharp
// Complete Invalidus setup
services.AddInvalidus(builder =>
{
    builder.UseRedis("localhost:6379")           // This package
           .UseMemoryCache()                     // Invalidus.Memory
           .EnableCQRS()                        // Invalidus.CQRS  
           .EnableDistributed()                 // Invalidus.Distributed
           .EnableMonitoring();                 // Invalidus.Monitoring
});
```

---

**Invalidus.Redis** - *High-performance Redis caching and invalidation for the modern world* 🚀