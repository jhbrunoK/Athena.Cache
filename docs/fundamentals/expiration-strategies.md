---
layout: default
title: Expiration Strategies
---

# Expiration Strategies

Cache expiration determines when cached data becomes stale and should be refreshed. Athena.Cache provides multiple expiration strategies to balance performance with data freshness.

## Time-based Expiration

The most common strategy is time-based expiration, where cache entries expire after a specified duration.

### Fixed Expiration Times

```csharp
[HttpGet]
[AthenaCache(ExpirationMinutes = 30)]  // Expires after 30 minutes
public async Task<ProductDto[]> GetProducts()
{
    return await _productService.GetProductsAsync();
}

[HttpGet("{id}")]
[AthenaCache(ExpirationMinutes = 60)]  // Expires after 1 hour
public async Task<ProductDto> GetProduct(int id)
{
    return await _productService.GetProductAsync(id);
}
```

### Expiration by Data Type

Different types of data should have different expiration times based on how frequently they change:

```csharp
[ApiController]
public class CatalogController : ControllerBase
{
    // Static reference data - cache for a long time
    [HttpGet("categories")]
    [AthenaCache(ExpirationMinutes = 240)]  // 4 hours
    public async Task<CategoryDto[]> GetCategories() { ... }

    // Product data - moderate expiration
    [HttpGet("products")]
    [AthenaCache(ExpirationMinutes = 60)]   // 1 hour
    public async Task<ProductDto[]> GetProducts() { ... }

    // Price data - short expiration (changes frequently)
    [HttpGet("prices")]
    [AthenaCache(ExpirationMinutes = 15)]   // 15 minutes
    public async Task<PriceDto[]> GetPrices() { ... }

    // Real-time data - very short expiration
    [HttpGet("inventory")]
    [AthenaCache(ExpirationMinutes = 5)]    // 5 minutes
    public async Task<InventoryDto[]> GetInventory() { ... }
}
```

## Sliding Expiration

Sliding expiration resets the expiration timer each time the cache is accessed, keeping frequently used data in cache longer.

### Enable Sliding Expiration

```csharp
[HttpGet("{id}")]
[AthenaCache(ExpirationMinutes = 30, SlidingExpiration = true)]
public async Task<UserDto> GetUser(int id)
{
    // If accessed within 30 minutes, expiration resets to 30 minutes from now
    return await _userService.GetUserAsync(id);
}
```

### Use Cases for Sliding Expiration

```csharp
// User session data - keep active users cached longer
[HttpGet("profile")]
[AthenaCache(ExpirationMinutes = 60, SlidingExpiration = true)]
public async Task<UserProfileDto> GetUserProfile()
{
    return await _userService.GetUserProfileAsync(User.GetUserId());
}

// Frequently accessed settings
[HttpGet("settings")]
[AthenaCache(ExpirationMinutes = 120, SlidingExpiration = true)]
public async Task<SettingsDto> GetSettings()
{
    return await _settingsService.GetSettingsAsync();
}
```

## Absolute Expiration

Absolute expiration ensures cache expires at a specific time, regardless of access patterns.

### Fixed Time Expiration

```csharp
[HttpGet("daily-report")]
[AthenaCache(ExpirationMinutes = 1440, AbsoluteExpiration = true)]  // 24 hours absolute
public async Task<DailyReportDto> GetDailyReport(DateTime date)
{
    // Always expires at the same time each day
    return await _reportService.GetDailyReportAsync(date);
}
```

### Business Hours Expiration

```csharp
[HttpGet("business-hours-data")]
[AthenaCache(CustomExpirationProvider = typeof(BusinessHoursExpirationProvider))]
public async Task<BusinessDataDto> GetBusinessData()
{
    // Custom logic to expire at end of business day
    return await _businessService.GetDataAsync();
}

public class BusinessHoursExpirationProvider : ICustomExpirationProvider
{
    public DateTimeOffset GetExpiration()
    {
        var now = DateTimeOffset.Now;
        var endOfBusinessDay = new DateTimeOffset(now.Date.AddHours(17)); // 5 PM
        
        // If after business hours, expire at next business day 5 PM
        if (now > endOfBusinessDay)
        {
            endOfBusinessDay = endOfBusinessDay.AddDays(1);
        }
        
        return endOfBusinessDay;
    }
}
```

## Conditional Expiration

Expire cache based on conditions rather than just time.

### User-based Expiration

```csharp
[HttpGet("personalized-data")]
[AthenaCache(ExpirationMinutes = 60, ConditionalExpiration = true)]
public async Task<PersonalizedDataDto> GetPersonalizedData()
{
    var userId = User.GetUserId();
    var userData = await _userService.GetUserDataAsync(userId);
    
    // Cache expiration varies based on user type
    if (userData.IsPremiumUser)
    {
        // Premium users get fresher data (shorter cache)
        Context.SetCacheExpiration(TimeSpan.FromMinutes(30));
    }
    else
    {
        // Regular users get longer cache
        Context.SetCacheExpiration(TimeSpan.FromMinutes(120));
    }
    
    return await _dataService.GetPersonalizedDataAsync(userId);
}
```

### Load-based Expiration

```csharp
[HttpGet("expensive-calculation")]
[AthenaCache(ExpirationMinutes = 60, ConditionalExpiration = true)]
public async Task<CalculationResultDto> GetExpensiveCalculation([FromQuery] CalculationRequest request)
{
    var complexity = CalculateComplexity(request);
    
    // More complex calculations get cached longer
    if (complexity == ComplexityLevel.High)
    {
        Context.SetCacheExpiration(TimeSpan.FromHours(4));
    }
    else if (complexity == ComplexityLevel.Medium)
    {
        Context.SetCacheExpiration(TimeSpan.FromHours(2));
    }
    else
    {
        Context.SetCacheExpiration(TimeSpan.FromMinutes(30));
    }
    
    return await _calculationService.PerformCalculationAsync(request);
}
```

## Global Expiration Configuration

Set default expiration policies for your entire application.

### Application-wide Defaults

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options =>
{
    // Default expiration for all cached methods
    options.DefaultExpirationMinutes = 60;
    
    // Configure expiration by controller
    options.ExpirationPolicies.Add("ProductsController", TimeSpan.FromMinutes(30));
    options.ExpirationPolicies.Add("UsersController", TimeSpan.FromMinutes(45));
    options.ExpirationPolicies.Add("ReportsController", TimeSpan.FromHours(2));
    
    // Configure expiration by method pattern
    options.ExpirationPolicies.Add("*GetAll*", TimeSpan.FromMinutes(15));
    options.ExpirationPolicies.Add("*GetById*", TimeSpan.FromMinutes(60));
});
```

### Environment-based Configuration

```csharp
// Different expiration times per environment
var expirationMinutes = builder.Environment.IsDevelopment() ? 5 : 60;

builder.Services.AddAthenaCacheComplete(options =>
{
    options.DefaultExpirationMinutes = expirationMinutes;
    
    // Shorter cache in development for faster testing
    if (builder.Environment.IsDevelopment())
    {
        options.ExpirationPolicies.Add("*", TimeSpan.FromMinutes(5));
    }
});
```

## Data-driven Expiration

Determine expiration based on the data being cached.

### Content-based Expiration

```csharp
[HttpGet("content/{id}")]
[AthenaCache(ConditionalExpiration = true)]
public async Task<ContentDto> GetContent(int id)
{
    var content = await _contentService.GetContentAsync(id);
    
    // Set expiration based on content type
    switch (content.Type)
    {
        case ContentType.News:
            Context.SetCacheExpiration(TimeSpan.FromMinutes(30));
            break;
        case ContentType.Reference:
            Context.SetCacheExpiration(TimeSpan.FromHours(6));
            break;
        case ContentType.Static:
            Context.SetCacheExpiration(TimeSpan.FromDays(1));
            break;
    }
    
    return content;
}
```

### Size-based Expiration

```csharp
[HttpGet("dataset/{id}")]
[AthenaCache(ConditionalExpiration = true)]
public async Task<DatasetDto> GetDataset(int id)
{
    var dataset = await _dataService.GetDatasetAsync(id);
    
    // Smaller datasets can be cached longer
    var sizeInMB = dataset.EstimatedSizeInBytes / (1024 * 1024);
    
    if (sizeInMB < 1)
    {
        Context.SetCacheExpiration(TimeSpan.FromHours(2));
    }
    else if (sizeInMB < 10)
    {
        Context.SetCacheExpiration(TimeSpan.FromHours(1));
    }
    else
    {
        Context.SetCacheExpiration(TimeSpan.FromMinutes(30));
    }
    
    return dataset;
}
```

## Cache Refresh Strategies

Proactively refresh cache before it expires to avoid cache misses.

### Background Refresh

```csharp
// Configure background refresh
builder.Services.AddAthenaCacheComplete(options =>
{
    options.BackgroundRefresh.EnableBackgroundRefresh = true;
    options.BackgroundRefresh.RefreshThresholdPercent = 80; // Refresh when 80% of TTL elapsed
    options.BackgroundRefresh.MaxConcurrentRefreshes = 5;
});

[HttpGet("critical-data")]
[AthenaCache(ExpirationMinutes = 60, BackgroundRefresh = true)]
public async Task<CriticalDataDto> GetCriticalData()
{
    // Data will be refreshed in background when 80% of 60 minutes has passed
    return await _dataService.GetCriticalDataAsync();
}
```

### Predictive Refresh

```csharp
[HttpGet("predictive-data")]
[AthenaCache(ExpirationMinutes = 30, PredictiveRefresh = true)]
public async Task<PredictiveDataDto> GetPredictiveData()
{
    // System analyzes access patterns and refreshes before expiration
    return await _dataService.GetPredictiveDataAsync();
}
```

## Manual Expiration Control

Sometimes you need explicit control over when cache expires.

### Programmatic Expiration

```csharp
[ApiController]
public class DataController : ControllerBase
{
    private readonly ICacheExpirationManager _expirationManager;

    [HttpPost("refresh-cache")]
    public async Task<IActionResult> RefreshCache([FromServices] ICacheExpirationManager expirationManager)
    {
        // Manually expire specific cache entries
        await expirationManager.ExpireAsync("DataController.GetData");
        await expirationManager.ExpireByPatternAsync("user_*_data");
        
        return Ok("Cache refreshed");
    }

    [HttpPost("extend-cache")]
    public async Task<IActionResult> ExtendCache(string cacheKey, int additionalMinutes)
    {
        // Extend expiration of existing cache entry
        await _expirationManager.ExtendExpirationAsync(cacheKey, TimeSpan.FromMinutes(additionalMinutes));
        
        return Ok($"Cache extended by {additionalMinutes} minutes");
    }
}
```

### Event-driven Expiration

```csharp
public class DataUpdateHandler
{
    private readonly ICacheExpirationManager _expirationManager;

    public async Task HandleDataUpdated(DataUpdatedEvent @event)
    {
        // Expire related cache when data changes
        switch (@event.DataType)
        {
            case "Products":
                await _expirationManager.ExpireByTableAsync("Products");
                break;
            case "Users":
                await _expirationManager.ExpireByPatternAsync($"user_{@event.EntityId}_*");
                break;
        }
    }
}
```

## Performance Considerations

### Expiration and Memory Usage

```csharp
// Configure memory-conscious expiration
builder.Services.AddAthenaCacheComplete(options =>
{
    options.MemoryPressure.EnableAutomaticExpiration = true;
    options.MemoryPressure.ExpirationPressureThresholdMB = 100;
    
    // Expire oldest entries first when under memory pressure
    options.MemoryPressure.ExpirationStrategy = ExpirationStrategy.LeastRecentlyUsed;
});
```

### Distributed Expiration Synchronization

```csharp
// For Redis distributed caching
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions => {
        athenaOptions.DistributedExpiration.SynchronizeExpiration = true;
        athenaOptions.DistributedExpiration.ExpirationToleranceSeconds = 30;
    },
    redisOptions => { ... });
```

## Best Practices

### 1. Choose Appropriate Expiration Times

```csharp
// Good: Different expiration times based on data characteristics
[AthenaCache(ExpirationMinutes = 1440)]  // Static reference data: 24 hours
public async Task<CountryDto[]> GetCountries() { ... }

[AthenaCache(ExpirationMinutes = 60)]    // Semi-static data: 1 hour  
public async Task<ProductDto[]> GetProducts() { ... }

[AthenaCache(ExpirationMinutes = 15)]    // Dynamic data: 15 minutes
public async Task<PriceDto[]> GetPrices() { ... }

[AthenaCache(ExpirationMinutes = 2)]     // Real-time data: 2 minutes
public async Task<InventoryDto[]> GetInventory() { ... }
```

### 2. Use Sliding Expiration for User Data

```csharp
// Good: Keep active user data cached
[AthenaCache(ExpirationMinutes = 60, SlidingExpiration = true)]
public async Task<UserProfileDto> GetUserProfile(int userId) { ... }
```

### 3. Consider Business Hours

```csharp
// Good: Longer cache during off-hours
[AthenaCache(ConditionalExpiration = true)]
public async Task<BusinessDataDto> GetBusinessData()
{
    var now = DateTime.Now;
    var isBusinessHours = now.Hour >= 9 && now.Hour <= 17;
    
    Context.SetCacheExpiration(
        isBusinessHours ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(2)
    );
    
    return await _businessService.GetDataAsync();
}
```

### 4. Monitor Expiration Effectiveness

```csharp
[HttpGet("cache/expiration-stats")]
public IActionResult GetExpirationStats([FromServices] ICacheStatistics stats)
{
    return Ok(new
    {
        ExpiredEntries = stats.ExpiredEntries,
        AverageTimeToExpiration = stats.AverageTimeToExpiration,
        ExpirationHitRate = stats.ExpirationHitRate,
        PrematureExpirations = stats.PrematureExpirations
    });
}
```

## Troubleshooting Expiration Issues

### Cache Expiring Too Quickly

```csharp
// Check if memory pressure is causing early expiration
[HttpGet("cache/memory-pressure")]
public IActionResult CheckMemoryPressure([FromServices] ICacheMemoryManager memory)
{
    return Ok(new
    {
        CurrentMemoryUsage = memory.CurrentMemoryUsage,
        MemoryPressureLevel = memory.MemoryPressureLevel,
        EarlyExpirationCount = memory.EarlyExpirationCount
    });
}
```

### Cache Not Expiring

```csharp
// Verify expiration configuration
[HttpGet("cache/expiration-config")]
public IActionResult GetExpirationConfig([FromServices] ICacheConfiguration config)
{
    return Ok(new
    {
        DefaultExpiration = config.DefaultExpirationMinutes,
        ExpirationPolicies = config.ExpirationPolicies,
        SlidingExpirationEnabled = config.SlidingExpirationEnabled
    });
}
```

For more on cache management:
- **[Cache Invalidation]({{ site.baseurl }}/fundamentals/cache-invalidation)** - Manual cache clearing strategies
- **[Performance Tuning]({{ site.baseurl }}/performance/production-tuning)** - Optimize cache performance
- **[Monitoring]({{ site.baseurl }}/monitoring/performance-metrics)** - Track expiration metrics