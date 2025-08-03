---
layout: default
title: Quick Start
---

# Quick Start

Get Athena.Cache running in your ASP.NET Core application in under 5 minutes.

## Step 1: Install Package

```bash
# For memory caching (single instance)
dotnet add package Athena.Cache.Core

# OR for distributed caching (multiple instances)
dotnet add package Athena.Cache.Redis
```

## Step 2: Configure Services

Add Athena.Cache to your application:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Option A: Memory Cache (single instance)
builder.Services.AddAthenaCacheComplete();

// Option B: Redis Cache (distributed)
// builder.Services.AddAthenaCacheRedisComplete(
//     athenaOptions => {
//         athenaOptions.Namespace = "MyApp";
//     },
//     redisOptions => {
//         redisOptions.ConnectionString = "localhost:6379";
//     });

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.UseAthenaCache();  // Add this middleware
app.MapControllers();

app.Run();
```

## Step 3: Add Cache to Your Controller

```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    // Cache for 30 minutes
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    [CacheInvalidateOn("Products")]
    public async Task<ProductDto[]> GetProducts()
    {
        return await _productService.GetProductsAsync();
    }

    // Get specific product (cache for 1 hour)
    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 60, KeyPattern = "product_{id}")]
    [CacheInvalidateOn("Products")]
    public async Task<ProductDto> GetProduct(int id)
    {
        return await _productService.GetProductAsync(id);
    }

    // Creating a product invalidates the cache
    [HttpPost]
    [CacheInvalidateOn("Products")]
    public async Task<ProductDto> CreateProduct([FromBody] CreateProductRequest request)
    {
        return await _productService.CreateProductAsync(request);
    }

    // Updating a product invalidates related caches
    [HttpPut("{id}")]
    [CacheInvalidateOn("Products")]
    public async Task<ProductDto> UpdateProduct(int id, [FromBody] UpdateProductRequest request)
    {
        return await _productService.UpdateProductAsync(id, request);
    }

    // Deleting a product invalidates the cache
    [HttpDelete("{id}")]
    [CacheInvalidateOn("Products")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        await _productService.DeleteProductAsync(id);
        return NoContent();
    }
}
```

## Step 4: Test Your Cache

1. **Start your application**:
   ```bash
   dotnet run
   ```

2. **Make a request**:
   ```bash
   curl http://localhost:5000/api/products
   ```

3. **Check the response headers** for cache information:
   ```
   X-Athena-Cache: MISS
   X-Athena-Cache-Key: ProductsController.GetProducts
   ```

4. **Make the same request again** - it should be faster:
   ```
   X-Athena-Cache: HIT
   X-Athena-Cache-Key: ProductsController.GetProducts
   ```

## What Just Happened?

- **First request**: Cache MISS → Database query executed → Result cached
- **Second request**: Cache HIT → Data served from cache (much faster!)
- **Cache headers**: Show whether request was served from cache
- **Automatic invalidation**: Creating/updating products clears related cache

## Configuration Options

### Basic Configuration

```csharp
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Namespace = "MyApp";
    options.DefaultExpirationMinutes = 60;
    options.CacheNullResults = false;
});
```

### Enable Logging

```csharp
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Logging.LogCacheHitMiss = true;
    options.Logging.LogCacheInvalidation = true;
});
```

### Redis Configuration

```csharp
builder.Services.AddAthenaCacheRedisComplete(
    athenaOptions => {
        athenaOptions.Namespace = "MyApp_Production";
        athenaOptions.DefaultExpirationMinutes = 45;
    },
    redisOptions => {
        redisOptions.ConnectionString = "localhost:6379";
        redisOptions.DatabaseId = 1;
    });
```

## Common Patterns

### Pattern 1: Simple Caching

```csharp
// Cache for 30 minutes
[AthenaCache(ExpirationMinutes = 30)]
public async Task<UserDto[]> GetUsers()
{
    return await _userService.GetUsersAsync();
}
```

### Pattern 2: Custom Cache Keys

```csharp
// Include user ID in cache key
[AthenaCache(KeyPattern = "user_{id}_profile")]
public async Task<UserProfileDto> GetUserProfile(int id)
{
    return await _userService.GetUserProfileAsync(id);
}
```

### Pattern 3: Conditional Caching

```csharp
// Only cache for anonymous users
[AthenaCache(ExpirationMinutes = 60, CacheCondition = "User.Identity.IsAuthenticated == false")]
public async Task<PublicDataDto> GetPublicData()
{
    return await _dataService.GetPublicDataAsync();
}
```

### Pattern 4: Multiple Cache Dependencies

```csharp
// This cache depends on both Users and Profiles tables
[AthenaCache(ExpirationMinutes = 45)]
[CacheInvalidateOn("Users")]
[CacheInvalidateOn("Profiles")]
public async Task<UserWithProfileDto[]> GetUsersWithProfiles()
{
    return await _userService.GetUsersWithProfilesAsync();
}
```

## Performance Optimization

### Enable Source Generator (Recommended)

For better performance and AOT support:

```bash
dotnet add package Athena.Cache.SourceGenerator
```

```csharp
// Program.cs - No changes needed, Source Generator works automatically
builder.Services.AddAthenaCacheComplete();

// Generated code eliminates reflection overhead
services.ConfigureAthenaCache(registry =>
{
    MyApp.Generated.AthenaCacheConfiguration.RegisterCacheConfigurations(registry);
});
```

### Memory Optimization

```csharp
builder.Services.AddAthenaCacheComplete(options =>
{
    options.MemoryOptimization.EnableStringPooling = true;
    options.MemoryOptimization.EnableCollectionPooling = true;
    options.MemoryPressure.EnableAutomaticCleanup = true;
});
```

## Monitoring Your Cache

### Add Cache Statistics Endpoint

```csharp
[HttpGet("cache/stats")]
public IActionResult GetCacheStats([FromServices] ICacheStatistics stats)
{
    return Ok(new
    {
        HitRate = stats.HitRate,
        TotalRequests = stats.TotalRequests,
        CacheSize = stats.CacheSize,
        MemoryUsage = stats.MemoryUsage
    });
}
```

### Check Cache Health

```csharp
[HttpGet("health/cache")]
public IActionResult CheckCacheHealth([FromServices] ICacheHealthChecker health)
{
    var status = health.CheckHealth();
    return status.IsHealthy ? Ok(status) : StatusCode(503, status);
}
```

## Common Scenarios

### E-commerce Product Catalog

```csharp
[ApiController]
[Route("api/[controller]")]
public class CatalogController : ControllerBase
{
    [HttpGet("categories")]
    [AthenaCache(ExpirationMinutes = 120)]  // Categories change rarely
    [CacheInvalidateOn("Categories")]
    public async Task<CategoryDto[]> GetCategories() { ... }

    [HttpGet("products")]
    [AthenaCache(ExpirationMinutes = 30)]   // Products change more often
    [CacheInvalidateOn("Products")]
    public async Task<ProductDto[]> GetProducts([FromQuery] ProductFilter filter) { ... }

    [HttpGet("products/{id}")]
    [AthenaCache(ExpirationMinutes = 60, KeyPattern = "product_{id}")]
    [CacheInvalidateOn("Products")]
    public async Task<ProductDto> GetProduct(int id) { ... }
}
```

### User Management

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 15)]
    [CacheInvalidateOn("Users")]
    public async Task<UserDto[]> GetUsers() { ... }

    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 30, KeyPattern = "user_{id}")]
    [CacheInvalidateOn("Users")]
    public async Task<UserDto> GetUser(int id) { ... }

    [HttpPut("{id}")]
    [CacheInvalidateOn("Users")]
    public async Task<UserDto> UpdateUser(int id, [FromBody] UpdateUserRequest request) { ... }
}
```

## Next Steps

Now that you have basic caching working:

1. **[Learn the fundamentals]({{ site.baseurl }}/getting-started/basics)** - Understand cache key generation and invalidation
2. **[Set up distributed caching]({{ site.baseurl }}/distributed/redis-setup)** - Scale across multiple instances
3. **[Optimize performance]({{ site.baseurl }}/performance/zero-memory-optimization)** - Get maximum performance
4. **[Add monitoring]({{ site.baseurl }}/monitoring/real-time-dashboards)** - Track cache performance

## Troubleshooting

### Cache Not Working?

1. **Check middleware order**:
   ```csharp
   app.UseRouting();
   app.UseAthenaCache();  // Must be after UseRouting()
   app.MapControllers();
   ```

2. **Verify service registration**:
   ```csharp
   builder.Services.AddAthenaCacheComplete();  // Required!
   ```

3. **Check controller inheritance**:
   ```csharp
   public class MyController : ControllerBase  // Must inherit from ControllerBase
   ```

### Need Help?

- **[Installation Guide]({{ site.baseurl }}/getting-started/installation)** - Detailed setup instructions
- **[Troubleshooting]({{ site.baseurl }}/advanced/troubleshooting)** - Common issues and solutions
- **[GitHub Issues](https://github.com/athena-cache/athena-cache/issues)** - Community support