# Invalidus.Core

**The Universal Cache Invalidation Engine** - Core abstractions and interfaces for intelligent cache invalidation across any cache provider.

## 🎯 What is Invalidus?

Invalidus is **not another caching library**. Instead, it's a specialized invalidation engine that works **with** your existing cache libraries to provide intelligent, coordinated cache invalidation.

### Why Invalidus?

- **🤝 Collaborative**: Works with Redis, Memory Cache, FusionCache, and any custom cache
- **🧠 Intelligent**: CQRS, Event-Driven, and Hierarchical invalidation patterns
- **🚀 Performance**: Batch operations, smart tracking, and distributed coordination
- **🔧 Extensible**: Plugin-based strategies for custom invalidation logic

## 🏗️ Architecture

```
Your App ──► Invalidus Engine ──► Multiple Cache Providers
              │
              ├─► Redis Cache
              ├─► Memory Cache  
              ├─► FusionCache
              └─► Custom Cache
```

## 🚀 Quick Start

### 1. Install Package

```bash
dotnet add package Invalidus.Core
```

### 2. Basic Usage

```csharp
// Register with DI
services.AddInvalidus(builder =>
{
    builder.UseRedis()           // Your existing Redis setup
           .UseMemoryCache()     // Your existing Memory cache
           .EnableCQRS()         // Event-driven invalidation
           .EnableHierarchical(); // Cascade invalidation
});

// Inject and use
public class UserService
{
    private readonly IInvalidationEngine _invalidation;
    
    public UserService(IInvalidationEngine invalidation)
    {
        _invalidation = invalidation;
    }
    
    public async Task UpdateUserAsync(User user)
    {
        await _userRepository.UpdateAsync(user);
        
        // Invalidate across ALL cache providers
        await _invalidation.InvalidateByTableAsync("Users");
    }
}
```

## 🎛️ Core Interfaces

### IInvalidationEngine
The main engine that orchestrates cache invalidation across providers.

```csharp
// Table-based invalidation
await engine.InvalidateByTableAsync("Users");

// Pattern-based invalidation  
await engine.InvalidateByPatternAsync("user:*");

// Event-driven invalidation (CQRS)
await engine.InvalidateOnEventAsync(userUpdatedEvent);

// Hierarchical invalidation
await engine.InvalidateHierarchyAsync("Users", ["UserProfiles", "UserSettings"]);
```

### ICacheProvider
Unified interface that any cache can implement for Invalidus integration.

```csharp
// Get/Set operations
await provider.GetAsync<User>("user:123");
await provider.SetAsync("user:123", user, TimeSpan.FromHours(1));

// Invalidation operations
await provider.RemoveAsync("user:123");
await provider.RemoveByPatternAsync("user:*");
```

### IInvalidationStrategy
Plugin system for custom invalidation logic.

```csharp
public class SmartInvalidationStrategy : InvalidationStrategyBase
{
    public override string StrategyName => "Smart";
    public override int Priority => 100;
    
    public override bool CanHandle(IInvalidationContext context)
    {
        return context.Type == InvalidationType.Table;
    }
    
    public override async Task<InvalidationResult> ExecuteAsync(
        IInvalidationContext context, 
        CancellationToken cancellationToken)
    {
        // Your custom invalidation logic here
        // Can analyze patterns, predict dependencies, etc.
    }
}
```

## 🎯 Invalidation Patterns

### 1. Table-Based Invalidation
```csharp
// When Users table changes, invalidate all user-related cache
await engine.InvalidateByTableAsync("Users");
```

### 2. Pattern-Based Invalidation
```csharp
// Remove all cache keys starting with "user:"
await engine.InvalidateByPatternAsync("user:*");
```

### 3. Event-Driven Invalidation (CQRS)
```csharp
// Automatically invalidate based on domain events
await engine.InvalidateOnEventAsync(new UserUpdatedEvent 
{ 
    UserId = "123", 
    ChangedFields = ["Email", "Name"] 
});
```

### 4. Hierarchical Invalidation
```csharp
// Cascade through related entities
await engine.InvalidateHierarchyAsync("Users", new[] 
{ 
    "UserProfiles", 
    "UserSettings", 
    "UserPreferences" 
}, maxDepth: 3);
```

### 5. Batch Operations
```csharp
// Efficient bulk invalidation
await engine.InvalidateBatchAsync(new[] 
{ 
    "Users", 
    "Products", 
    "Orders" 
});
```

## 🔧 Extension Points

### Custom Cache Provider
```csharp
public class MyCustomCacheProvider : ICacheProvider
{
    public string ProviderName => "MyCache";
    public CacheProviderType ProviderType => CacheProviderType.Custom;
    
    // Implement cache operations
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        // Your cache implementation
    }
    
    // Implement invalidation operations  
    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken)
    {
        // Your invalidation implementation
    }
}
```

### Custom Invalidation Strategy
```csharp
public class MyInvalidationStrategy : InvalidationStrategyBase
{
    public override string StrategyName => "MyStrategy";
    public override int Priority => 50;
    
    public override bool CanHandle(IInvalidationContext context)
    {
        return context.Metadata.ContainsKey("MyCustomFlag");
    }
    
    public override async Task<InvalidationResult> ExecuteAsync(
        IInvalidationContext context, 
        CancellationToken cancellationToken)
    {
        // Your custom logic
        return InvalidationResult.Success(invalidatedCount, executionTime);
    }
}
```

## 📊 Monitoring & Diagnostics

```csharp
// Get engine status
var status = await engine.GetStatusAsync();
Console.WriteLine($"Health: {status.IsHealthy}");
Console.WriteLine($"Active Rules: {status.ActiveRules}");
Console.WriteLine($"Tracked Keys: {status.TrackedKeys}");

// Get provider statistics
foreach (var provider in cacheProviders)
{
    var stats = await provider.GetStatisticsAsync();
    Console.WriteLine($"{provider.ProviderName}: {stats.HitRatio:P2} hit ratio");
}
```

## 🚀 Performance Features

- **Batch Operations**: Process multiple invalidations efficiently
- **Smart Tracking**: Only track what needs tracking
- **Async/Await**: Fully asynchronous for high throughput
- **Provider Optimization**: Each provider can optimize based on its strengths
- **Connection Pooling**: Shared resources across operations

## 🔗 Ecosystem

Invalidus Core is the foundation. See related packages:

- **Invalidus.Redis** - Redis provider implementation
- **Invalidus.AspNetCore** - ASP.NET Core integration
- **Invalidus.CQRS** - Advanced CQRS/Event Sourcing features
- **Invalidus.Monitoring** - Metrics and health checks

---

**Invalidus** - *Because cache invalidation shouldn't be hard* 🚀