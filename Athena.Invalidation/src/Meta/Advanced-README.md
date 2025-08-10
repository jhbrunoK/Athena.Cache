# Athena.Invalidation.Advanced

Enterprise-grade advanced features for Athena.Invalidation. Perfect for complex applications that need sophisticated cache invalidation patterns, CQRS support, and comprehensive monitoring.

## 🚀 Quick Start

### Installation

```bash
dotnet add package Athena.Invalidation.Advanced
```

### CQRS Pattern Integration

```csharp
// Program.cs
services.AddInvalidation(config => 
{
    config.EnableCQRS()
          .WithEventDrivenInvalidation()
          .WithCommandInvalidation();
});

// Command Handler
public async Task Handle(CreateProductCommand command)
{
    await _repository.CreateAsync(command.Product);
    
    // Automatic invalidation based on command type
    await _invalidation.InvalidateOnCommandAsync(command);
}

// Event Handler  
public async Task Handle(ProductCreatedEvent domainEvent)
{
    // Automatic invalidation based on domain event
    await _invalidation.InvalidateOnEventAsync(domainEvent);
}
```

### Hierarchical Invalidation

```csharp
services.AddInvalidation(config => 
{
    config.EnableHierarchicalInvalidation(hierarchy => {
        hierarchy.AddDependency("Products", "Categories", "Suppliers");
        hierarchy.AddDependency("Orders", "Products", "Customers");
    });
});

// This will automatically invalidate Products, Categories, and Suppliers
await _invalidation.InvalidateHierarchyAsync("Categories", maxDepth: 3);
```

### Distributed Cluster Synchronization

```csharp
services.AddInvalidation(config => 
{
    config.EnableDistributedInvalidation(distributed => {
        distributed.UseRedisEventBus("localhost:6379")
                  .UseRabbitMQEventBus("localhost:5672")
                  .WithClusterSynchronization();
    });
});
```

### Comprehensive Monitoring

```csharp
services.AddInvalidation(config => 
{
    config.EnableMonitoring(monitoring => {
        monitoring.UseOpenTelemetry()
                 .UsePrometheusMetrics()
                 .WithHealthChecks()
                 .WithPerformanceCounters();
    });
});

// Add health checks
services.AddHealthChecks()
    .AddCheck<InvalidationEngineHealthCheck>("cache-invalidation");
```

## 📦 Included Packages

This meta-package includes:

- **Athena.Invalidation** - Core functionality (automatically included)
- **Athena.Invalidation.CQRS** - Command/Event-driven invalidation patterns
- **Athena.Invalidation.Hierarchical** - Complex dependency management
- **Athena.Invalidation.Distributed** - Multi-node cluster synchronization
- **Athena.Invalidation.Monitoring** - OpenTelemetry, Prometheus, health checks

## 🔧 Advanced Scenarios

### Event-Driven Architecture

```csharp
// Register event handlers
services.AddInvalidation(config => 
{
    config.RegisterEventHandler<ProductUpdatedEvent>(async (evt, engine) => {
        await engine.InvalidateByPatternAsync($"product:{evt.ProductId}:*");
        await engine.InvalidateByTableAsync("ProductCatalog");
    });
});
```

### Custom Invalidation Rules

```csharp
// Register custom rules
await _invalidation.RegisterInvalidationRuleAsync(new ConditionalInvalidationRule
{
    Id = "weekend-refresh",
    Condition = ctx => DateTime.Now.DayOfWeek == DayOfWeek.Saturday,
    Action = async ctx => await ctx.Engine.InvalidateByPatternAsync("cached:*")
});
```

### Performance Monitoring

```csharp
// Monitor invalidation performance
services.AddOpenTelemetry()
    .WithTracing(builder => builder.AddSource("Athena.Invalidation"))
    .WithMetrics(builder => builder.AddMeter("Athena.Invalidation"));
```

## 📊 Observability

### Metrics Available

- Invalidation latency and throughput
- Cache hit/miss ratios by provider
- Error rates and retry counts
- Cluster synchronization status

### Health Checks

- Cache provider connectivity
- Event bus availability
- Cluster node health
- Background service status

## 📚 Documentation

- [CQRS Integration Guide](https://github.com/jhbrunoK/Athena.Cache/wiki/CQRS-Integration)
- [Hierarchical Invalidation](https://github.com/jhbrunoK/Athena.Cache/wiki/Hierarchical-Invalidation)
- [Distributed Setup](https://github.com/jhbrunoK/Athena.Cache/wiki/Distributed-Invalidation)
- [Monitoring & Observability](https://github.com/jhbrunoK/Athena.Cache/wiki/Monitoring)

## 📄 License

MIT License - see LICENSE file for details.