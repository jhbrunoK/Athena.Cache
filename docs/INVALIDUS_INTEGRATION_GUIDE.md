# Invalidus Integration Guide

**The Universal Cache Invalidation Engine** - Complete Integration Guide

## 🌟 Overview

Invalidus is the unified cache invalidation ecosystem that brings together the best features from both Athena.Cache and Athena.Invalidation into a single, powerful, and intelligent solution.

### ✨ What Makes Invalidus Special?

- **🔄 Intelligent Fusion**: True integration of similar functionalities, not just code movement
- **🎯 Specialized Focus**: Dedicated to cache invalidation excellence
- **🌐 Universal Compatibility**: Works with Redis, Memory, FusionCache, and custom providers
- **📊 Complete Observability**: Integrated monitoring, metrics, and alerting
- **🎬 Event-Driven Architecture**: Full CQRS and Event Sourcing support

## 🚀 Quick Start

### Basic Setup

```csharp
using Invalidus.Core.Extensions;

// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Invalidus with intelligent defaults
builder.Services.AddInvalidus(invalidus =>
{
    invalidus.UseRedis("localhost:6379")      // Unified Redis provider
            .UseMemoryCache()                 // Additional caching layer
            .EnableMonitoring()               // Real-time observability
            .EnableCQRS()                     // Event-driven invalidation
            .EnableAlerts()                   // Smart alerting
            .EnableHealthChecks();            // Health monitoring
});

var app = builder.Build();

// Map health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

app.Run();
```

### Environment-Specific Setup

```csharp
// Development - Full features with detailed logging
builder.Services.AddInvalidusDevelopment(invalidus =>
{
    invalidus.UseRedis("localhost:6379")
            .UseMemoryCache();
});

// Production - Optimized for performance and reliability  
builder.Services.AddInvalidusProduction(invalidus =>
{
    invalidus.UseRedis(builder.Configuration.GetConnectionString("Redis"))
            .EnableMonitoring(monitoring =>
            {
                monitoring.MetricsCollectionInterval = TimeSpan.FromMinutes(1);
                monitoring.AlertEvaluationInterval = TimeSpan.FromMinutes(1);
            })
            .EnableCQRS(cqrs =>
            {
                cqrs.EventBatchSize = 1000;
                cqrs.CommandTimeout = TimeSpan.FromMinutes(5);
            });
});

// Configuration-based setup
builder.Services.AddInvalidusFromConfiguration(builder.Configuration);
```

## 🔧 Configuration

### appsettings.json

```json
{
  "Invalidus": {
    "DefaultTimeout": "00:00:30",
    "MaxRetries": 3,
    "BatchSize": 100,
    "EnableDetailedLogging": false,
    "EnablePerformanceMetrics": true,
    
    "Redis": {
      "ConnectionString": "localhost:6379",
      "Database": 0,
      "KeyPrefix": "invalidus:",
      "EnableCompression": true,
      "Pool": {
        "MinSize": 5,
        "MaxSize": 20
      }
    },
    
    "Monitoring": {
      "MetricsCollectionInterval": "00:01:00",
      "AlertEvaluationInterval": "00:01:00",
      "MaxEventHistory": 10000,
      "LogEvents": false
    },
    
    "CQRS": {
      "EnableEventSourcing": true,
      "CommandTimeout": "00:05:00",
      "EventBatchSize": 500,
      "MaxRetryAttempts": 5
    }
  }
}
```

## 💡 Core Usage Patterns

### 1. Basic Invalidation

```csharp
public class ProductService
{
    private readonly IInvalidationEngine _invalidationEngine;
    
    public ProductService(IInvalidationEngine invalidationEngine)
    {
        _invalidationEngine = invalidationEngine;
    }
    
    public async Task UpdateProductAsync(Product product)
    {
        // Update product in database
        await _repository.UpdateAsync(product);
        
        // Invalidate related caches
        await _invalidationEngine.InvalidateByTableAsync("Products");
        await _invalidationEngine.InvalidateByPatternAsync($"product:{product.Id}:*");
        await _invalidationEngine.InvalidateByPatternAsync($"category:{product.CategoryId}:*");
    }
    
    public async Task UpdateProductBatchAsync(IEnumerable<Product> products)
    {
        var productList = products.ToList();
        
        // Update products in database
        await _repository.UpdateBatchAsync(productList);
        
        // Batch invalidation for performance
        var patterns = productList.SelectMany(p => new[]
        {
            $"product:{p.Id}:*",
            $"category:{p.CategoryId}:*"
        }).ToList();
        
        await _invalidationEngine.InvalidateByPatternBatchAsync(patterns);
    }
}
```

### 2. Hierarchical Invalidation

```csharp
public class CategoryService
{
    private readonly IInvalidationEngine _invalidationEngine;
    
    public async Task UpdateCategoryAsync(Category category)
    {
        await _repository.UpdateAsync(category);
        
        // Invalidate category and related products hierarchically
        await _invalidationEngine.InvalidateHierarchyAsync(
            "Categories", 
            new[] { "Products", "ProductCategories" }, 
            maxDepth: 3);
    }
}
```

### 3. Event-Driven Invalidation (CQRS)

```csharp
// Define domain event
public record ProductPriceChanged : IInvalidationEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString();
    public string EventType => "ProductPriceChanged";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int Version { get; init; } = 1;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    public required string ProductId { get; init; }
    public required decimal OldPrice { get; init; }
    public required decimal NewPrice { get; init; }
    public List<string> AffectedCategories { get; init; } = new();
}

// Event handler for automatic invalidation
public class ProductPriceChangedHandler : IEventHandler<ProductPriceChanged>
{
    private readonly IInvalidationEngine _engine;
    
    public async Task HandleAsync(ProductPriceChanged eventData, CancellationToken cancellationToken)
    {
        // Invalidate product-specific caches
        await _engine.InvalidateByPatternAsync($"product:{eventData.ProductId}:*");
        
        // Invalidate category caches
        var categoryPatterns = eventData.AffectedCategories
            .Select(cat => $"category:{cat}:*")
            .ToList();
            
        await _engine.InvalidateByPatternBatchAsync(categoryPatterns);
        
        // Update read models
        await _engine.InvalidateReadModelAsync<ProductCatalogReadModel>();
    }
}

// Usage in service
public class ProductService
{
    private readonly IEventPublisher _eventPublisher;
    
    public async Task ChangePriceAsync(string productId, decimal newPrice)
    {
        var product = await _repository.GetAsync(productId);
        var oldPrice = product.Price;
        
        // Update price
        product.Price = newPrice;
        await _repository.UpdateAsync(product);
        
        // Publish event for reactive invalidation
        await _eventPublisher.PublishAsync(new ProductPriceChanged
        {
            ProductId = productId,
            OldPrice = oldPrice,
            NewPrice = newPrice,
            AffectedCategories = await GetProductCategoriesAsync(productId),
            CorrelationId = Activity.Current?.Id
        });
    }
}
```

### 4. Command-Based Invalidation

```csharp
public class OrderService
{
    private readonly ICommandDispatcher _commandDispatcher;
    
    public async Task ProcessOrderAsync(Order order)
    {
        // Process order logic
        await _repository.SaveAsync(order);
        
        // Send invalidation commands
        var commands = new List<IInvalidationCommand>
        {
            new InvalidateTableCommand { TableName = "Orders" },
            new InvalidateReadModelCommand 
            { 
                ReadModelType = "CustomerOrderSummary",
                AggregateId = order.CustomerId.ToString()
            },
            new InvalidateProjectionCommand 
            { 
                ProjectionName = "OrdersByRegion",
                PartitionKey = order.ShippingAddress.Region
            }
        };
        
        var result = await _commandDispatcher.DispatchBatchAsync(commands);
        
        if (!result.AllSuccessful)
        {
            _logger.LogWarning("Some invalidation commands failed: {Errors}", 
                string.Join(", ", result.Errors.Values));
        }
    }
}
```

### 5. Read Model Management

```csharp
// Configure read model dependencies
services.AddReadModelDependencies(dependencies =>
{
    dependencies
        .AddDependency("CustomerOrderSummary", "Order", 
            properties: new[] { "CustomerId", "Status", "Total" })
        .AddDependency("ProductCatalog", "Product", 
            strategy: InvalidationStrategy.Delayed, 
            delay: TimeSpan.FromMinutes(5))
        .AddDependency("InventoryReport", "Product",
            properties: new[] { "Stock", "ReservedQuantity" },
            strategy: InvalidationStrategy.Batched);
});

// Usage
public class CustomerService
{
    private readonly IReadModelManager _readModelManager;
    
    public async Task UpdateCustomerAsync(Customer customer, string[] changedProperties)
    {
        await _repository.UpdateAsync(customer);
        
        // Automatically invalidate related read models
        await _readModelManager.InvalidateRelatedReadModelsAsync(
            "Customer", customer.Id.ToString(), changedProperties);
    }
}
```

## 📊 Monitoring and Observability

### Real-time Dashboard

```csharp
[ApiController]
[Route("api/[controller]")]
public class InvalidusController : ControllerBase
{
    private readonly IInvalidationMonitor _monitor;
    
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardData>> GetDashboardAsync()
    {
        var metrics = await _monitor.CollectMetricsAsync();
        var systemHealth = await _monitor.CheckSystemHealthAsync();
        var activeAlerts = await _monitor.GetActiveAlertsAsync();
        
        return Ok(new DashboardData
        {
            // System Health
            IsSystemHealthy = systemHealth.IsHealthy,
            TotalProviders = systemHealth.TotalProviders,
            HealthyProviders = systemHealth.HealthyProviders,
            
            // Performance Metrics
            CacheHitRatio = metrics.CacheHitRatio,
            InvalidationSuccessRate = metrics.InvalidationSuccessRate,
            TotalKeys = metrics.TotalKeys,
            TotalMemoryUsage = metrics.TotalMemoryUsage,
            
            // Active Issues
            ActiveAlerts = activeAlerts.Count(),
            CriticalAlerts = activeAlerts.Count(a => a.Severity == AlertSeverity.Critical)
        });
    }
    
    [HttpGet("performance")]
    public async Task<ActionResult<PerformanceReport>> GetPerformanceAsync(
        [FromQuery] int hours = 24)
    {
        var stats = await _monitor.GetPerformanceStatsAsync(TimeSpan.FromHours(hours));
        var hotKeys = await _monitor.GetHotKeysAnalysisAsync(TimeSpan.FromHours(hours), 20);
        var patterns = await _monitor.AnalyzeInvalidationPatternsAsync(TimeSpan.FromHours(hours));
        
        return Ok(new PerformanceReport
        {
            TotalInvalidations = stats.TotalInvalidations,
            AverageLatency = stats.AverageLatency,
            ErrorRate = stats.ErrorRate,
            TopHotKeys = hotKeys.Take(10),
            Recommendations = patterns.Recommendations
        });
    }
}

public class DashboardData
{
    public bool IsSystemHealthy { get; init; }
    public int TotalProviders { get; init; }
    public int HealthyProviders { get; init; }
    public double CacheHitRatio { get; init; }
    public double InvalidationSuccessRate { get; init; }
    public long TotalKeys { get; init; }
    public long TotalMemoryUsage { get; init; }
    public int ActiveAlerts { get; init; }
    public int CriticalAlerts { get; init; }
}
```

### Alert Configuration

```csharp
services.AddInvalidusMonitoringWithAlerts(
    configureAlerts: alertConfig =>
    {
        // Cache performance alerts
        alertConfig.Thresholds["CacheHitRatio"] = new AlertThreshold
        {
            MetricName = "CacheHitRatio",
            WarningThreshold = 0.80,
            CriticalThreshold = 0.70,
            ComparisonType = AlertComparisonType.LessThan,
            EvaluationWindow = TimeSpan.FromMinutes(5)
        };
        
        // Memory usage alerts
        alertConfig.Thresholds["MemoryUsage"] = new AlertThreshold
        {
            MetricName = "TotalMemoryUsage",
            WarningThreshold = 1_000_000_000,  // 1GB
            CriticalThreshold = 2_000_000_000, // 2GB
            ComparisonType = AlertComparisonType.GreaterThan
        };
        
        // Invalidation failure alerts
        alertConfig.Thresholds["InvalidationFailures"] = new AlertThreshold
        {
            MetricName = "FailedInvalidations",
            WarningThreshold = 10,
            CriticalThreshold = 50,
            ComparisonType = AlertComparisonType.GreaterThan,
            EvaluationWindow = TimeSpan.FromMinutes(5)
        };
    });
```

## 🔗 Integration Examples

### ASP.NET Core Web API

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Invalidus
builder.Services.AddInvalidus(invalidus =>
{
    invalidus.UseRedis(builder.Configuration.GetConnectionString("Redis")!)
            .UseMemoryCache()
            .EnableMonitoring()
            .EnableCQRS()
            .EnableAlerts()
            .EnableHealthChecks();
});

// Add controllers
builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseRouting();
app.MapControllers();

// Health checks
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

// Invalidus dashboard (if needed)
app.MapGet("/invalidus/status", async (IInvalidationMonitor monitor) =>
{
    var health = await monitor.CheckSystemHealthAsync();
    var metrics = await monitor.CollectMetricsAsync();
    
    return Results.Ok(new { health, metrics });
});

app.Run();
```

### Background Services

```csharp
public class OrderProcessingService : BackgroundService
{
    private readonly IEventSubscriber _eventSubscriber;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly ILogger<OrderProcessingService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe to order events
        await _eventSubscriber.SubscribeAsync<OrderStatusChangedEvent>(
            HandleOrderStatusChanged, stoppingToken);
            
        // Subscribe to inventory events  
        await _eventSubscriber.SubscribeAsync<InventoryUpdatedEvent>(
            HandleInventoryUpdated, stoppingToken);
        
        // Keep service running
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
    
    private async Task HandleOrderStatusChanged(OrderStatusChangedEvent evt, CancellationToken ct)
    {
        var commands = new List<IInvalidationCommand>
        {
            new InvalidatePatternCommand 
            { 
                Pattern = $"customer:{evt.CustomerId}:orders:*" 
            },
            new InvalidateProjectionCommand 
            { 
                ProjectionName = "OrderAnalytics",
                PartitionKey = evt.OrderId 
            }
        };
        
        await _commandDispatcher.DispatchBatchAsync(commands, ct);
    }
}
```

### Microservices Integration

```csharp
// Service A - Order Service
public class OrderService
{
    public async Task CreateOrderAsync(CreateOrderRequest request)
    {
        // Create order
        var order = new Order(request);
        await _repository.SaveAsync(order);
        
        // Publish event for other services
        await _eventPublisher.PublishAsync(new OrderCreatedEvent
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            ProductIds = order.Items.Select(i => i.ProductId).ToList(),
            TotalAmount = order.TotalAmount
        });
    }
}

// Service B - Inventory Service  
public class InventoryEventHandler : IEventHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent evt, CancellationToken ct)
    {
        // Update inventory
        foreach (var productId in evt.ProductIds)
        {
            await UpdateProductInventoryAsync(productId);
        }
        
        // Invalidate inventory caches
        var patterns = evt.ProductIds.Select(id => $"inventory:{id}:*").ToList();
        await _invalidationEngine.InvalidateByPatternBatchAsync(patterns);
    }
}
```

## 🎯 Best Practices

### 1. Invalidation Strategy Selection

```csharp
// ✅ Good - Specific, targeted invalidation
await _engine.InvalidateByPatternAsync($"product:{productId}:*");

// ✅ Good - Batch operations for performance
await _engine.InvalidateByPatternBatchAsync(patterns);

// ❌ Avoid - Overly broad invalidation
await _engine.ClearAllAsync(); // Only use when absolutely necessary
```

### 2. Event Design

```csharp
// ✅ Good - Rich, meaningful events
public record ProductUpdatedEvent : InvalidationEvent
{
    public override string EventType => "ProductUpdated";
    public required string ProductId { get; init; }
    public required string[] ChangedProperties { get; init; }
    public required Dictionary<string, object> PreviousValues { get; init; }
    public required Dictionary<string, object> NewValues { get; init; }
    public bool IsPriceChange => ChangedProperties.Contains("Price");
    public bool IsInventoryChange => ChangedProperties.Contains("Stock");
}

// ❌ Avoid - Vague, minimal events  
public record SomethingChanged
{
    public string? What { get; init; }
}
```

### 3. Error Handling

```csharp
public class OrderService
{
    public async Task ProcessOrderAsync(Order order)
    {
        try
        {
            // Main business logic
            await _repository.SaveAsync(order);
            
            // Cache invalidation - don't fail the main operation
            try
            {
                await _invalidationEngine.InvalidateByTableAsync("Orders");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache invalidation failed for order {OrderId}", order.Id);
                // Continue - main operation succeeded
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process order {OrderId}", order.Id);
            throw;
        }
    }
}
```

### 4. Performance Optimization

```csharp
// ✅ Good - Use batch operations
var patterns = products.Select(p => $"product:{p.Id}:*").ToList();
await _engine.InvalidateByPatternBatchAsync(patterns);

// ✅ Good - Use background processing for non-critical invalidation
_ = Task.Run(async () =>
{
    try 
    {
        await _engine.InvalidateByPatternAsync("analytics:*");
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Background invalidation failed");
    }
});

// ✅ Good - Use appropriate timeouts
await _engine.InvalidateByTableAsync("Products", cancellationToken);
```

## 🚀 Migration Guide

### From Athena.Cache

```csharp
// Before (Athena.Cache)
services.AddAthenaCache(options =>
{
    options.UseRedis("localhost:6379");
});

// After (Invalidus)
services.AddInvalidus(invalidus =>
{
    invalidus.UseRedis("localhost:6379")
            .EnableMonitoring()
            .EnableHealthChecks();
});
```

### From Athena.Invalidation

```csharp
// Before (Athena.Invalidation)  
services.AddAthenaCacheInvalidation(options =>
{
    options.UseRedisInvalidation("localhost:6379");
});

// After (Invalidus) - Same Redis connection, more features
services.AddInvalidus(invalidus =>
{
    invalidus.UseRedis("localhost:6379")  // Unified provider
            .EnableCQRS()                 // Enhanced event support
            .EnableMonitoring()           // Built-in observability
            .EnableAlerts();              // Smart alerting
});
```

---

**Invalidus** - *The Universal Cache Invalidation Engine* 🌟

*Bringing intelligent fusion, specialized focus, and complete observability to cache invalidation.*