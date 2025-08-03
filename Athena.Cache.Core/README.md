# 🏛️ Athena.Cache.Core

**Smart caching library for ASP.NET Core with automatic query parameter key generation and table-based cache invalidation**

Athena.Cache.Core is the foundational package for the Athena Cache ecosystem, providing intelligent caching capabilities with automatic key generation and table-based invalidation for ASP.NET Core applications.

## ✨ Key Features

- 🔑 **Automatic Cache Key Generation**: Query parameters → SHA256 hash keys automatically
- 🗂️ **Table-based Invalidation**: Automatic cache clearing when database tables change
- 🏗️ **Convention-based Inference**: Automatic table name inference from controller names
- 🎨 **Declarative Caching**: `[AthenaCache]` and `[CacheInvalidateOn]` attributes
- ⚡ **High Performance**: Optimized for high-traffic environments
- 🧠 **Zero Memory Allocation**: 90-98% memory allocation reduction through 5-phase optimization
- 🔄 **Automatic Memory Management**: Real-time GC monitoring and automatic cache cleanup
- 🏪 **Multiple Backends**: MemoryCache and Redis support
- 🧪 **Comprehensive Testing**: Full unit and integration test coverage

## 🚀 Quick Start

### Installation

```bash
# Install the core package
dotnet add package Athena.Cache.Core

# Optional: Add Source Generator for compile-time optimizations
dotnet add package Athena.Cache.SourceGenerator
```

### Basic Setup

```csharp
// Program.cs
using Athena.Cache.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Athena Cache with MemoryCache
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Namespace = "MyApp";
    options.DefaultExpirationMinutes = 30;
    options.Logging.LogCacheHitMiss = true;
});

var app = builder.Build();

// Add middleware (important: after routing, before controllers)
app.UseRouting();
app.UseAthenaCache();  // Add after routing
app.MapControllers();

app.Run();
```

### Controller Usage

```csharp
using Athena.Cache.Core.Attributes;
using Athena.Cache.Core.Enums;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    [CacheInvalidateOn("Users")]  // Auto-invalidated when Users table changes
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 10)
    {
        return Ok(await _userService.GetUsersAsync(search, page, size));
    }

    [HttpPost]
    [CacheInvalidateOn("Users")]  // Invalidates Users cache on creation
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserRequest request)
    {
        var user = await _userService.CreateUserAsync(request);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }

    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 60)]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        return user == null ? NotFound() : Ok(user);
    }

    // Disable caching for specific actions
    [HttpGet("no-cache")]
    [NoCache]  // This action will not be cached
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsersWithoutCache()
    {
        var users = await _userService.GetUsersAsync();
        return Ok(users);
    }
}
```

## 🔧 Configuration Options

### Cache Options

```csharp
builder.Services.AddAthenaCacheComplete(options =>
{
    // Basic settings
    options.Namespace = "MyApp_PROD";
    options.DefaultExpirationMinutes = 60;
    
    // Convention settings
    options.Convention.EnableConventionBasedInvalidation = true;
    options.Convention.ControllerSuffix = "Controller";
    options.Convention.TableSuffix = "";
    
    // Example: UsersController -> Users table (automatic inference)
    // Example: ProductsController -> Products table (automatic inference)
    
    // Error handling
    options.ErrorHandling.OnCacheError = CacheErrorAction.LogAndContinue;
    options.ErrorHandling.ThrowOnSerializationError = false;
    
    // Logging
    options.Logging.LogCacheHitMiss = true;
    options.Logging.LogCacheInvalidation = true;
    options.Logging.LogCacheOperations = false;
});
```

### Configuration from appsettings.json

```json
{
  "AthenaCache": {
    "Namespace": "MyApp",
    "DefaultExpirationMinutes": 30,
    "Convention": {
      "EnableConventionBasedInvalidation": true,
      "ControllerSuffix": "Controller",
      "TableSuffix": ""
    },
    "ErrorHandling": {
      "OnCacheError": "LogAndContinue",
      "ThrowOnSerializationError": false
    },
    "Logging": {
      "LogCacheHitMiss": true,
      "LogCacheInvalidation": true
    }
  }
}
```

```csharp
// Program.cs with configuration from appsettings.json
builder.Services.AddAthenaCacheComplete(
    builder.Configuration.GetSection("AthenaCache"));
```

### Memory Management

```csharp
// Configure automatic memory management
builder.Services.AddAthenaCacheComplete(options =>
{
    options.MemoryPressure.EnableAutomaticCleanup = true;
    options.MemoryPressure.CleanupThresholdMB = 100;
    options.MemoryPressure.MonitoringIntervalSeconds = 30;
});
```

## 📊 Cache Status Monitoring

### HTTP Headers

Athena.Cache provides cache status information through HTTP headers:

```bash
# First request (cache miss)
curl -v http://localhost:5000/api/users
# Response header: X-Athena-Cache: MISS

# Second request (cache hit)
curl -v http://localhost:5000/api/users  
# Response header: X-Athena-Cache: HIT
```

### Cache Statistics

```csharp
[ApiController]
public class CacheStatusController : ControllerBase
{
    private readonly IAthenaCache _cache;

    public CacheStatusController(IAthenaCache cache)
    {
        _cache = cache;
    }

    [HttpGet("cache/stats")]
    public async Task<IActionResult> GetCacheStats()
    {
        var stats = await _cache.GetStatisticsAsync();
        return Ok(new
        {
            HitRate = stats.HitRate,
            MissRate = stats.MissRate,
            TotalRequests = stats.TotalRequests,
            CacheSize = stats.CacheSize
        });
    }
}
```

## 🎯 Advanced Features

### Custom Cache Key Generation

```csharp
public class CustomCacheKeyGenerator : ICacheKeyGenerator
{
    public string GenerateKey(string baseKey, object parameters)
    {
        // Custom key generation logic
        return $"custom_{baseKey}_{HashHelper.ComputeHash(parameters)}";
    }
}

// Register custom key generator
builder.Services.AddSingleton<ICacheKeyGenerator, CustomCacheKeyGenerator>();
```

### Programmatic Cache Management

```csharp
[ApiController]
public class CacheController : ControllerBase
{
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAthenaCache _cache;

    public CacheController(ICacheInvalidator cacheInvalidator, IAthenaCache cache)
    {
        _cacheInvalidator = cacheInvalidator;
        _cache = cache;
    }

    [HttpDelete("cache/table/{tableName}")]
    public async Task<IActionResult> InvalidateTable(string tableName)
    {
        await _cacheInvalidator.InvalidateByTableAsync(tableName);
        return Ok($"Cache invalidated for table: {tableName}");
    }

    [HttpGet("cache/stats")]
    public async Task<IActionResult> GetCacheStats()
    {
        var stats = await _cache.GetStatisticsAsync();
        return Ok(stats);
    }
}
```

### Performance Monitoring

```csharp
// Enable performance monitoring
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Observability.EnableMetrics = true;
    options.Observability.EnableTracing = true;
});

// Access performance metrics
[ApiController]
public class MetricsController : ControllerBase
{
    private readonly ICacheMetrics _metrics;

    public MetricsController(ICacheMetrics metrics)
    {
        _metrics = metrics;
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics()
    {
        var cacheStats = await _metrics.GetCacheStatisticsAsync();
        return Ok(new
        {
            HitRate = cacheStats.HitRate,
            MissRate = cacheStats.MissRate,
            TotalRequests = cacheStats.TotalRequests,
            MemoryUsage = cacheStats.MemoryUsage
        });
    }
}
```

## 🧠 Zero Memory Optimization

Athena.Cache.Core implements advanced memory optimization techniques:

### Phase 1: String Pooling
```csharp
// High-performance string pooling for cache keys
var pooledKey = HighPerformanceStringPool.Get(keyTemplate, parameters);
```

### Phase 2: Value Type Optimizations
```csharp
// Optimized value type handling
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public ref struct CacheKeyBuilder
{
    // Stack-allocated key building
}
```

### Phase 3: Collection Pooling
```csharp
// Reusable collections to minimize allocations
using var pooledList = CollectionPools.RentList<string>();
```

### Phase 4: Lazy Initialization
```csharp
// Lazy cache initialization for better startup performance
public class LazyCache<T> where T : class
{
    private readonly Lazy<T> _lazy;
    // Implementation details...
}
```

### Phase 5: Memory Pressure Management
```csharp
// Automatic cache cleanup based on memory pressure
public class MemoryPressureManager
{
    public void MonitorAndCleanup()
    {
        if (GC.GetTotalMemory(false) > _threshold)
        {
            TriggerCacheCleanup();
        }
    }
}
```

## 🔌 Integration with Other Packages

Athena.Cache.Core seamlessly integrates with:

- **[Athena.Cache.Redis](https://www.nuget.org/packages/Athena.Cache.Redis/)**: Redis distributed caching
- **[Athena.Cache.Monitoring](https://www.nuget.org/packages/Athena.Cache.Monitoring/)**: Real-time monitoring and alerting
- **[Athena.Cache.Analytics](https://www.nuget.org/packages/Athena.Cache.Analytics/)**: Advanced analytics and insights
- **[Athena.Cache.SourceGenerator](https://www.nuget.org/packages/Athena.Cache.SourceGenerator/)**: Compile-time optimization

## 🧪 Testing

```csharp
// Example unit test
[Test]
public async Task CacheAttribute_ShouldCacheResponse()
{
    // Arrange
    var controller = new TestController(_service);
    var context = CreateHttpContext();
    
    // Act
    var result1 = await controller.GetData();
    var result2 = await controller.GetData();
    
    // Assert
    Assert.AreEqual(result1, result2);
    _mockService.Verify(x => x.GetData(), Times.Once);
}
```

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/jhbrunoK/Athena.Cache/blob/main/LICENSE.txt) file for details.

## 🐛 Issues & Support

- **Repository**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)
- **Issues**: [https://github.com/jhbrunoK/Athena.Cache/issues](https://github.com/jhbrunoK/Athena.Cache/issues)
- **Documentation**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)