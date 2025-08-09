# Usage Examples

This document provides practical examples of using Athena.Invalidation in various scenarios.

## Quick Start

### 1. Basic Setup with ASP.NET Core

```csharp
// Program.cs
using Athena.Invalidation.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Athena Invalidation with Memory Cache
builder.Services.AddInvalidationEngineComplete(options =>
{
    options.Logging.LogInvalidationEvents = true;
    options.Performance.UseMemoryPooling = true;
});

var app = builder.Build();
```

### 2. Redis Integration

```csharp
// Program.cs
using Athena.Invalidation.Redis.Extensions;

builder.Services.AddRedisInvalidation("localhost:6379", 
    providerOptions =>
    {
        options.Database = 0;
        options.ScanPageSize = 1000;
    },
    engineOptions =>
    {
        options.AllowClearAll = false;
        options.EnableKeyTracking = true;
    });
```

### 3. Multi-Provider Setup (Recommended for Production)

```csharp
// Program.cs
using Athena.Invalidation.MultiProvider.Extensions;

builder.Services.CreateMultiProviderBuilder()
    .AddMemoryCache()
    .AddRedis("redis:6379")
    .Build(options =>
    {
        options.DefaultFailurePolicy = FailureHandlingPolicy.AtLeastOneSucceeds;
        options.EnableParallelExecution = true;
        options.MaxConcurrency = Environment.ProcessorCount;
    });
```

## Usage Patterns

### 1. Basic Invalidation

```csharp
public class UserService
{
    private readonly IInvalidationEngine _invalidationEngine;

    public UserService(IInvalidationEngine invalidationEngine)
    {
        _invalidationEngine = invalidationEngine;
    }

    public async Task CreateUserAsync(CreateUserRequest request)
    {
        // Business logic
        var user = await CreateUserInDatabase(request);
        
        // Invalidate related caches
        await _invalidationEngine.InvalidateByTableAsync("Users");
    }

    public async Task UpdateUserAsync(int userId, UpdateUserRequest request)
    {
        // Business logic
        await UpdateUserInDatabase(userId, request);
        
        // Invalidate specific user cache
        await _invalidationEngine.InvalidateByKeyAsync($"user:{userId}");
        
        // Also invalidate user list caches
        await _invalidationEngine.InvalidateByPatternAsync("users:list:*");
    }
}
```

### 2. Cache Key Tracking

```csharp
public class ProductService
{
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly IMemoryCache _cache;

    public async Task<Product> GetProductAsync(int productId)
    {
        var cacheKey = $"product:{productId}";
        
        // Track this cache key with the Products table
        await _invalidationEngine.TrackCacheKeyAsync("Products", cacheKey);
        
        return await _cache.GetOrCreateAsync(cacheKey, async factory =>
        {
            factory.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return await LoadProductFromDatabase(productId);
        });
    }

    public async Task UpdateProductAsync(int productId, UpdateProductRequest request)
    {
        await UpdateProductInDatabase(productId, request);
        
        // This will invalidate all tracked keys for the Products table
        await _invalidationEngine.InvalidateByTableAsync("Products");
    }
}
```

### 3. Batch Invalidation

```csharp
public class OrderService
{
    public async Task ProcessOrderAsync(CreateOrderRequest request)
    {
        // Business logic
        var order = await CreateOrderInDatabase(request);
        
        // Invalidate multiple related tables at once
        await _invalidationEngine.InvalidateBatchAsync(new[] 
        { 
            "Orders", 
            "Products", 
            "Users", 
            "Inventory" 
        });
    }
}
```

### 4. Hierarchical Invalidation

```csharp
public class CatalogService
{
    public async Task UpdateCategoryAsync(int categoryId, UpdateCategoryRequest request)
    {
        await UpdateCategoryInDatabase(categoryId, request);
        
        // Invalidate category and all related subcategories and products
        await _invalidationEngine.InvalidateHierarchyAsync(
            "Categories", 
            relatedTables: new[] { "Subcategories", "Products", "ProductImages" },
            maxDepth: 3
        );
    }
}
```

### 5. CQRS Integration

```csharp
// Command Handler
public class UpdateUserCommandHandler
{
    private readonly IInvalidationEngine _invalidationEngine;
    
    public async Task Handle(UpdateUserCommand command)
    {
        // Business logic
        await UpdateUser(command);
        
        // CQRS-based invalidation
        await _invalidationEngine.InvalidateOnCommandAsync(command);
    }
}

// Event Handler
public class UserUpdatedEventHandler
{
    private readonly IInvalidationEngine _invalidationEngine;
    
    public async Task Handle(UserUpdatedEvent domainEvent)
    {
        // Event-based invalidation
        await _invalidationEngine.InvalidateOnEventAsync(domainEvent);
        
        // Or invalidate read models specifically
        await _invalidationEngine.InvalidateReadModelAsync<UserProfileReadModel>(
            domainEvent.UserId.ToString()
        );
    }
}
```

### 6. Distributed Environment

```csharp
// Node A - API Server
public class ApiController : ControllerBase
{
    private readonly IInvalidationEngine _invalidationEngine;
    
    [HttpPut("users/{id}")]
    public async Task UpdateUser(int id, UpdateUserRequest request)
    {
        await UpdateUserInDatabase(id, request);
        
        // This invalidation will be distributed to all nodes
        await _invalidationEngine.InvalidateByTableAsync("Users");
    }
}

// Node B - Background Service (automatically receives invalidation)
public class CacheWarmupService : BackgroundService
{
    // This service will automatically receive distributed invalidation events
    // and can react accordingly (e.g., warming up cache)
}
```

### 7. Advanced Tracking with Tags

```csharp
public class ArticleService
{
    public async Task<Article> GetArticleAsync(int articleId)
    {
        var cacheKey = $"article:{articleId}";
        
        // Track with multiple tables and tags
        await _invalidationEngine.TrackCacheKeyAsync(
            new[] { "Articles", "Authors", "Categories" }, 
            cacheKey
        );
        
        return await _cache.GetOrCreateAsync(cacheKey, async factory =>
        {
            var article = await LoadArticleFromDatabase(articleId);
            
            // Add tags for more granular invalidation
            // This would require advanced tracking features
            return article;
        });
    }
}
```

## Configuration Examples

### 1. Production Configuration

```csharp
// appsettings.Production.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Athena.Invalidation": "Warning"
    }
  },
  "CacheInvalidation": {
    "Redis": {
      "ConnectionString": "redis-cluster:6379",
      "Database": 0,
      "AllowClearAll": false
    },
    "Performance": {
      "MaxConcurrency": 8,
      "EnableParallelExecution": true
    },
    "Monitoring": {
      "EnablePrometheusMetrics": true,
      "EnableOpenTelemetry": true
    }
  }
}
```

### 2. Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddInvalidationEngineHealthChecks()
    .AddRedisInvalidationHealthChecks()
    .AddMemoryCacheInvalidationHealthChecks();

app.MapHealthChecks("/health");
```

### 3. Monitoring Setup

```csharp
// Add monitoring
builder.Services.AddInvalidationMonitoring(options =>
{
    options.EnablePrometheusMetrics = true;
    options.EnableOpenTelemetry = true;
    options.MetricsCollectionInterval = TimeSpan.FromMinutes(1);
});

// Prometheus endpoint
app.MapMetrics("/metrics");
```

## Best Practices

### 1. Key Naming Conventions
```csharp
// Good: Consistent, hierarchical naming
"user:profile:123"
"product:details:456" 
"category:tree:789"

// Bad: Inconsistent naming
"userProfile123"
"product_456_details"
"cat789tree"
```

### 2. Error Handling
```csharp
try
{
    await _invalidationEngine.InvalidateByTableAsync("Users");
}
catch (InvalidationException ex)
{
    _logger.LogWarning(ex, "Cache invalidation failed, continuing with degraded performance");
    // Don't fail the entire operation due to cache issues
}
```

### 3. Performance Considerations
```csharp
// Use batch operations when possible
await _invalidationEngine.InvalidateBatchAsync(new[] 
{ 
    "Users", "Orders", "Products" 
});

// Instead of individual calls
await _invalidationEngine.InvalidateByTableAsync("Users");
await _invalidationEngine.InvalidateByTableAsync("Orders");
await _invalidationEngine.InvalidateByTableAsync("Products");
```

This covers the main usage patterns for Athena.Invalidation. For more advanced scenarios, refer to the production deployment guide.