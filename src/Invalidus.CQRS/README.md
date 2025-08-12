# Invalidus.CQRS

**Advanced Event-Driven Cache Invalidation** - CQRS and Event Sourcing extensions for Invalidus

## 🎯 What is Invalidus.CQRS?

Invalidus.CQRS provides advanced CQRS (Command Query Responsibility Segregation) and event-driven capabilities for cache invalidation:

- **🎬 Command/Query Separation**: Separate command handling for cache invalidation operations
- **📡 Event-Driven Architecture**: Event sourcing and publish-subscribe patterns for reactive invalidation
- **📊 Read Model Management**: Automatic invalidation when domain models change
- **🔄 Projection Management**: Cache invalidation for CQRS projection updates
- **⚡ Asynchronous Processing**: Non-blocking, high-performance invalidation workflows
- **📝 Event Sourcing**: Complete audit trail of all invalidation operations

## 📦 Installation

```bash
dotnet add package Invalidus.CQRS
```

## 🚀 Quick Start

### Basic CQRS Setup

```csharp
using Invalidus.CQRS.Extensions;

// Program.cs or Startup.cs
services.AddInvalidusCQRS(options =>
{
    options.EnableEventSourcing = true;
    options.EnableProjectionManagement = true;
    options.CommandTimeout = TimeSpan.FromMinutes(1);
});
```

### Environment-Specific Setup

```csharp
// Development - Full featured with detailed logging
services.AddInvalidusCQRSDevelopment();

// Production - Optimized for performance and reliability
services.AddInvalidusCQRSProduction();

// High-Performance - Minimal overhead
services.AddInvalidusCQRSHighPerformance();
```

## 💡 Core Features

### 1. Command-Based Invalidation

```csharp
public class OrderService
{
    private readonly ICommandDispatcher _commandDispatcher;
    
    public OrderService(ICommandDispatcher commandDispatcher)
    {
        _commandDispatcher = commandDispatcher;
    }
    
    public async Task ProcessOrderAsync(Order order)
    {
        // Process order logic...
        
        // Send invalidation commands
        var commands = new List<IInvalidationCommand>
        {
            new InvalidateTableCommand 
            { 
                TableName = "Orders", 
                Metadata = { ["OrderId"] = order.Id }
            },
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
            // Handle partial failures
            LogPartialFailures(result.Errors);
        }
    }
}
```

### 2. Event-Driven Invalidation

```csharp
// Define domain events
public record ProductPriceChanged : InvalidationEvent
{
    public override string EventType => "ProductPriceChanged";
    public required string ProductId { get; init; }
    public required decimal OldPrice { get; init; }
    public required decimal NewPrice { get; init; }
    public List<string> AffectedCategories { get; init; } = new();
}

// Event handler for automatic cache invalidation
public class ProductPriceChangedHandler : IEventHandler<ProductPriceChanged>
{
    private readonly ICommandDispatcher _commandDispatcher;
    
    public async Task HandleAsync(ProductPriceChanged eventData, CancellationToken cancellationToken)
    {
        // Invalidate product-specific caches
        await _commandDispatcher.DispatchAsync(new InvalidatePatternCommand
        {
            Pattern = $"product:{eventData.ProductId}:*"
        }, cancellationToken);
        
        // Invalidate category caches if needed
        foreach (var category in eventData.AffectedCategories)
        {
            await _commandDispatcher.DispatchAsync(new InvalidatePatternCommand
            {
                Pattern = $"category:{category}:*"
            }, cancellationToken);
        }
    }
}

// Register the handler
services.AddEventHandler<ProductPriceChanged, ProductPriceChangedHandler>();

// Publish events
public class ProductService
{
    private readonly IEventPublisher _eventPublisher;
    
    public async Task UpdatePriceAsync(string productId, decimal newPrice)
    {
        var oldPrice = await GetCurrentPriceAsync(productId);
        
        // Update price in database...
        
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

### 3. Read Model Management

```csharp
// Configure read model dependencies
services.AddReadModelDependencies(dependencies =>
{
    dependencies
        .AddDependency("CustomerOrderSummary", "Order", 
            properties: new[] { "CustomerId", "Status", "Total" })
        .AddDependency("ProductCatalog", "Product", 
            strategy: InvalidationStrategy.Delayed, delay: TimeSpan.FromMinutes(5))
        .AddDependency("InventoryReport", "Product",
            properties: new[] { "Stock", "ReservedQuantity" },
            strategy: InvalidationStrategy.Batched);
});

// Use read model manager
public class CustomerService
{
    private readonly IReadModelManager _readModelManager;
    
    public async Task UpdateCustomerAsync(Customer customer, string[] changedProperties)
    {
        // Update customer in database...
        
        // Automatically invalidate related read models
        await _readModelManager.InvalidateRelatedReadModelsAsync(
            "Customer", customer.Id.ToString(), changedProperties);
    }
}
```

### 4. Projection Management

```csharp
// Register projections
services.AddInvalidusCQRSWithProjections();

public class SalesAnalyticsProjection : IProjectionProcessor
{
    public IEnumerable<Type> SupportedEventTypes => new[]
    {
        typeof(EntityChangedEvent),
        typeof(AggregateChangedEvent)
    };
    
    public async Task<ProjectionUpdateResult> ProcessEventAsync(IInvalidationEvent eventData, CancellationToken cancellationToken)
    {
        var invalidatedKeys = new List<string>();
        
        if (eventData is EntityChangedEvent entityChanged && entityChanged.EntityType == "Order")
        {
            // Update sales analytics projection
            await UpdateSalesDataAsync(entityChanged);
            
            // Invalidate related cache keys
            invalidatedKeys.Add($"sales:daily:{DateTime.Today:yyyy-MM-dd}");
            invalidatedKeys.Add($"sales:monthly:{DateTime.Today:yyyy-MM}");
        }
        
        return new ProjectionUpdateResult
        {
            Success = true,
            InvalidatedCacheKeys = invalidatedKeys,
            ProcessingTime = TimeSpan.FromMilliseconds(45)
        };
    }
}

// Register projection
await projectionManager.RegisterProjectionAsync(new ProjectionDefinition
{
    Name = "SalesAnalytics",
    Description = "Real-time sales analytics projection",
    EventTypes = new[] { "EntityChanged", "AggregateChanged" },
    Mode = ProjectionMode.Continuous,
    AutoInvalidateCache = true,
    CachePatterns = new[] { "sales:*", "analytics:sales:*" }
});
```

### 5. Event Sourcing

```csharp
public class OrderEventSourcingService
{
    private readonly IEventStore _eventStore;
    private readonly IEventPublisher _eventPublisher;
    
    public async Task ProcessOrderWorkflowAsync(string orderId)
    {
        // Get all events for this order
        var events = await _eventStore.GetEventsAsync(orderId);
        
        // Replay events to rebuild state
        var orderState = ReplayEvents(events);
        
        // Process next step based on current state
        var nextEvent = DetermineNextEvent(orderState);
        
        // Store and publish the event
        await _eventStore.SaveEventAsync(nextEvent);
        await _eventPublisher.PublishAsync(nextEvent);
    }
    
    // Query events by type for analytics
    public async Task<IEnumerable<EntityChangedEvent>> GetOrderEventsAsync(DateTime from)
    {
        return await _eventStore.GetEventsByTypeAsync<EntityChangedEvent>(from);
    }
}
```

## 🎬 Command Types

### Built-in Commands

```csharp
// Table-based invalidation
var tableCommand = new InvalidateTableCommand
{
    TableName = "Products",
    Schema = "catalog",
    Columns = new[] { "Name", "Price", "Description" },
    Delay = TimeSpan.FromSeconds(30)
};

// Pattern-based invalidation
var patternCommand = new InvalidatePatternCommand
{
    Pattern = "user:*:profile",
    ProviderName = "Redis",
    CascadeInvalidation = true
};

// Hierarchical invalidation
var hierarchyCommand = new InvalidateHierarchyCommand
{
    RootKey = "category:electronics",
    MaxDepth = 5,
    ExcludePatterns = new[] { "*:system:*" }
};

// Read model invalidation
var readModelCommand = new InvalidateReadModelCommand
{
    ReadModelType = "ProductCatalog",
    AggregateId = "category-123",
    RelatedEntities = new[] { "Product", "Category", "Brand" },
    InvalidateProjections = true
};

// Projection invalidation
var projectionCommand = new InvalidateProjectionCommand
{
    ProjectionName = "CustomerOrderHistory",
    PartitionKey = "customer-456",
    FilterCriteria = new Dictionary<string, object>
    {
        ["Status"] = "Active",
        ["Region"] = "North America"
    },
    RebuildProjection = false
};
```

### Custom Commands

```csharp
// Create custom invalidation command
public record ClearUserSessionCommand : InvalidationCommand
{
    public override string CommandType => "ClearUserSession";
    public required string UserId { get; init; }
    public bool ClearAllDevices { get; init; } = false;
}

// Create custom command handler
public class ClearUserSessionHandler : ICommandHandler<ClearUserSessionCommand>
{
    private readonly IInvalidationEngine _engine;
    
    public async Task<CommandResult> HandleAsync(ClearUserSessionCommand command, CancellationToken cancellationToken)
    {
        var invalidatedKeys = new List<string>();
        var context = new InvalidationContext();
        
        if (command.ClearAllDevices)
        {
            await _engine.InvalidateByPatternAsync($"session:{command.UserId}:*", context, cancellationToken);
            invalidatedKeys.Add($"session:{command.UserId}:*");
        }
        else
        {
            await _engine.InvalidateByTableAsync("UserSessions", context, cancellationToken);
            invalidatedKeys.Add("table:UserSessions");
        }
        
        return CommandResult.Successful(invalidatedKeys, context.ExecutionTime);
    }
}

// Register custom command handler
services.AddCommandHandler<ClearUserSessionCommand, ClearUserSessionHandler>();
```

## 📡 Event Types

### Domain Events

```csharp
// Entity lifecycle events
var entityEvent = new EntityChangedEvent
{
    EntityType = "Product",
    EntityId = "prod-123",
    ChangeType = "Updated",
    ChangedProperties = new[] { "Price", "Stock" },
    PreviousValues = new Dictionary<string, object> { ["Price"] = 99.99m },
    NewValues = new Dictionary<string, object> { ["Price"] = 89.99m }
};

// Aggregate events for DDD
var aggregateEvent = new AggregateChangedEvent
{
    AggregateType = "Order",
    AggregateId = "order-456",
    ChangeType = "StatusChanged",
    AggregateVersion = 3,
    AffectedReadModels = new[] { "OrderSummary", "CustomerOrderHistory" },
    AffectedProjections = new[] { "SalesAnalytics", "OrdersByRegion" }
};

// Read model lifecycle
var readModelEvent = new ReadModelUpdatedEvent
{
    ReadModelType = "ProductCatalog",
    ReadModelId = "catalog-789",
    UpdateReason = "Product price changed",
    UpdatedFields = new[] { "Price", "LastModified" },
    RequiresCacheInvalidation = true
};
```

### System Events

```csharp
// Invalidation completion tracking
var completionEvent = new InvalidationCompletedEvent
{
    InvalidationId = command.CommandId,
    InvalidationType = "PatternInvalidation",
    InvalidatedKeys = new[] { "product:123", "product:124", "product:125" },
    TotalKeysInvalidated = 3,
    Duration = TimeSpan.FromMilliseconds(150),
    Success = true
};

// Batch operation tracking
var batchEvent = new BatchInvalidationEvent
{
    Commands = commandList,
    TotalCommands = 10,
    SuccessfulCommands = 8,
    FailedCommands = 2,
    BatchDuration = TimeSpan.FromSeconds(2),
    Errors = new Dictionary<string, string>
    {
        ["cmd-001"] = "Timeout waiting for Redis",
        ["cmd-005"] = "Invalid table name"
    }
};
```

## ⚙️ Configuration

### appsettings.json

```json
{
  "Invalidus": {
    "CQRS": {
      "EnableEventSourcing": true,
      "EnableProjectionManagement": true,
      "CommandTimeout": "00:01:00",
      "EventBatchSize": 500,
      "MaxRetryAttempts": 3,
      "EnableDetailedLogging": false,
      "EnableEventStoreCleanup": true,
      "EventStoreCleanupInterval": "1.00:00:00",
      "EventRetentionPeriod": "30.00:00:00"
    }
  }
}
```

### Advanced Configuration

```csharp
services.AddInvalidusCQRS(options =>
{
    options.EnableEventSourcing = true;
    options.EnableProjectionManagement = true;
    options.CommandTimeout = TimeSpan.FromMinutes(5);
    options.EventBatchSize = 1000;
    options.MaxRetryAttempts = 5;
    options.EnableDetailedLogging = true;
    options.EnableEventStoreCleanup = true;
    options.EventStoreCleanupInterval = TimeSpan.FromHours(6);
    options.EventRetentionPeriod = TimeSpan.FromDays(90);
});
```

## 🔧 Integration Examples

### ASP.NET Core Web API

```csharp
[ApiController]
[Route("api/[controller]")]
public class InvalidationController : ControllerBase
{
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IEventPublisher _eventPublisher;
    private readonly IEventStore _eventStore;

    [HttpPost("commands/invalidate-table")]
    public async Task<IActionResult> InvalidateTable([FromBody] InvalidateTableCommand command)
    {
        var result = await _commandDispatcher.DispatchAsync(command);
        
        return result.Success 
            ? Ok(new { result.InvalidatedKeys, result.ExecutionTime })
            : BadRequest(new { result.ErrorMessage });
    }

    [HttpPost("commands/batch")]
    public async Task<IActionResult> InvalidateBatch([FromBody] List<IInvalidationCommand> commands)
    {
        var result = await _commandDispatcher.DispatchBatchAsync(commands);
        
        return Ok(new 
        { 
            result.TotalCommands,
            result.SuccessfulCommands,
            result.FailedCommands,
            result.SuccessRate,
            result.TotalExecutionTime,
            result.Errors
        });
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetEvents([FromQuery] DateTime? from = null, [FromQuery] int maxEvents = 100)
    {
        var events = await _eventStore.GetEventsByTypeAsync<IInvalidationEvent>(from, maxEvents);
        return Ok(events);
    }
}
```

### Background Processing

```csharp
public class OrderProcessingService : BackgroundService
{
    private readonly IEventSubscriber _eventSubscriber;
    private readonly ICommandDispatcher _commandDispatcher;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe to order-related events
        await _eventSubscriber.SubscribeAsync<EntityChangedEvent>(async (evt, ct) =>
        {
            if (evt.EntityType == "Order" && evt.ChangeType == "StatusChanged")
            {
                await ProcessOrderStatusChange(evt, ct);
            }
        }, stoppingToken);
    }
    
    private async Task ProcessOrderStatusChange(EntityChangedEvent orderEvent, CancellationToken cancellationToken)
    {
        var invalidationCommands = new List<IInvalidationCommand>();
        
        // Invalidate customer's order cache
        invalidationCommands.Add(new InvalidatePatternCommand
        {
            Pattern = $"customer:{orderEvent.NewValues?["CustomerId"]}:orders:*"
        });
        
        // Update order analytics projection
        invalidationCommands.Add(new InvalidateProjectionCommand
        {
            ProjectionName = "OrderAnalytics",
            PartitionKey = orderEvent.EntityId
        });
        
        await _commandDispatcher.DispatchBatchAsync(invalidationCommands, cancellationToken);
    }
}
```

## 🎯 Best Practices

### 1. Command Design

```csharp
// ✅ Good - Specific, focused commands
public record InvalidateProductCatalogCommand : InvalidationCommand
{
    public override string CommandType => "InvalidateProductCatalog";
    public required string CategoryId { get; init; }
    public bool IncludeSubcategories { get; init; } = true;
    public List<string>? ExcludeBrands { get; init; }
}

// ❌ Avoid - Generic, unclear commands
public record GenericInvalidateCommand : InvalidationCommand
{
    public override string CommandType => "Generic";
    public Dictionary<string, object> Data { get; init; } = new();
}
```

### 2. Event Design

```csharp
// ✅ Good - Rich, meaningful events
public record ProductInventoryChanged : InvalidationEvent
{
    public override string EventType => "ProductInventoryChanged";
    public required string ProductId { get; init; }
    public required int PreviousStock { get; init; }
    public required int NewStock { get; init; }
    public required string Reason { get; init; } // "Sale", "Restock", "Adjustment"
    public string? WarehouseId { get; init; }
    public bool IsLowStock => NewStock < 10;
    public bool WasCritical => PreviousStock <= 0 && NewStock > 0;
}

// ❌ Avoid - Vague, data-poor events
public record SomethingChangedEvent : InvalidationEvent
{
    public override string EventType => "Changed";
    public string? What { get; init; }
}
```

### 3. Handler Design

```csharp
// ✅ Good - Focused, error-handling handlers
public class ProductInventoryChangedHandler : IEventHandler<ProductInventoryChanged>
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly ILogger<ProductInventoryChangedHandler> _logger;
    
    public async Task HandleAsync(ProductInventoryChanged evt, CancellationToken cancellationToken)
    {
        try
        {
            var commands = new List<IInvalidationCommand>();
            
            // Always invalidate product cache
            commands.Add(new InvalidatePatternCommand { Pattern = $"product:{evt.ProductId}:*" });
            
            // Conditionally invalidate other caches
            if (evt.IsLowStock)
            {
                commands.Add(new InvalidatePatternCommand { Pattern = "inventory:low-stock:*" });
            }
            
            if (evt.WasCritical)
            {
                commands.Add(new InvalidatePatternCommand { Pattern = "alerts:critical-inventory:*" });
            }
            
            if (!string.IsNullOrEmpty(evt.WarehouseId))
            {
                commands.Add(new InvalidatePatternCommand { Pattern = $"warehouse:{evt.WarehouseId}:*" });
            }
            
            await _dispatcher.DispatchBatchAsync(commands, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle inventory change for product {ProductId}", evt.ProductId);
            throw; // Re-throw to trigger retry mechanisms
        }
    }
}
```

## 🔗 Ecosystem Integration

Invalidus.CQRS works seamlessly with the entire Invalidus ecosystem:

```csharp
services.AddInvalidus(builder =>
{
    builder.UseRedis("localhost:6379")           // Cache provider
           .UseMemoryCache()                     // Additional provider  
           .EnableCQRS(cqrs =>                   // This package
           {
               cqrs.EnableEventSourcing = true;
               cqrs.EnableProjectionManagement = true;
               cqrs.CommandTimeout = TimeSpan.FromMinutes(2);
           })
           .EnableMonitoring()                   // Monitoring integration
           .EnableAlerts();                      // Smart alerting
});
```

---

**Invalidus.CQRS** - *Advanced event-driven cache invalidation for modern applications* 🎬