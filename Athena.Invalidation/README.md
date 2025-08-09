# 🚀 Athena.Invalidation

**Universal Cache Invalidation Engine for .NET**

A standalone, high-performance cache invalidation library that works with any caching provider - Redis, MemoryCache, FusionCache, and more.

## ✨ Features

- 🎯 **Universal Compatibility** - Works with any cache library
- 📋 **Multiple Strategies** - Table, Pattern, Key, Batch, and Hierarchical invalidation
- 🏗️ **Pluggable Architecture** - Extensible strategies and providers
- ⚡ **High Performance** - Optimized for zero-allocation scenarios
- 🔧 **Easy Integration** - Simple ASP.NET Core setup

## 🚀 Quick Start

### Installation

```bash
# Core package
dotnet add package Athena.Invalidation.Core

# ASP.NET Core integration
dotnet add package Athena.Invalidation.AspNetCore

# Strategy implementations
dotnet add package Athena.Invalidation.Strategies
```

### Basic Setup

```csharp
// Program.cs
using Athena.Invalidation.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add invalidation engine with MemoryCache
builder.Services.AddInvalidationEngineComplete(options =>
{
    options.Logging.LogInvalidationEvents = true;
});

var app = builder.Build();
```

### Usage

```csharp
public class UserService
{
    private readonly IInvalidationEngine _invalidationEngine;

    public UserService(IInvalidationEngine invalidationEngine)
    {
        _invalidationEngine = invalidationEngine;
    }

    public async Task CreateUserAsync(User user)
    {
        // Your business logic
        await SaveUserToDatabase(user);

        // Invalidate related caches
        await _invalidationEngine.InvalidateByTableAsync("Users");
    }

    public async Task GetUserAsync(int userId)
    {
        var cacheKey = $"user:{userId}";
        
        // Track this cache key with the Users table
        await _invalidationEngine.TrackCacheKeyAsync("Users", cacheKey);
        
        // Your caching logic
        return await GetFromCacheOrDatabase(cacheKey);
    }
}
```

## 🔧 Configuration Options

### Memory + Redis Hybrid

```csharp
builder.Services.AddInvalidationEngineHybrid(
    "localhost:6379", // Redis connection string
    options =>
    {
        options.Performance.UseMemoryPooling = true;
        options.Performance.EnableZeroAllocation = true;
    });
```

### Custom Cache Provider

```csharp
public class MyCacheProvider : ICacheProvider
{
    public string ProviderName => "MyCache";
    
    // Implement interface methods...
}

// Register
builder.Services.AddInvalidationEngine()
    .WithCacheProvider<MyCacheProvider>();
```

## 📋 Invalidation Strategies

### Table-based
```csharp
await engine.InvalidateByTableAsync("Users");
```

### Pattern-based
```csharp
await engine.InvalidateByPatternAsync("user:*");
```

### Batch
```csharp
await engine.InvalidateBatchAsync(new[] { "Users", "Orders", "Products" });
```

### Hierarchical
```csharp
await engine.InvalidateHierarchyAsync("Users", 
    relatedTables: new[] { "UserProfiles", "UserPreferences" },
    maxDepth: 3);
```

## 🏗️ Architecture

```
┌─────────────────────────────────────┐
│        IInvalidationEngine          │
├─────────────────────────────────────┤
│  ┌─────────────────────────────────┐ │
│  │     InvalidationStrategies      │ │
│  │  • Basic                        │ │
│  │  • Hierarchical                 │ │
│  │  • Custom                       │ │
│  └─────────────────────────────────┘ │
├─────────────────────────────────────┤
│  ┌─────────────────────────────────┐ │
│  │      CacheProviders             │ │
│  │  • MemoryCache                  │ │
│  │  • Redis                        │ │
│  │  • FusionCache                  │ │
│  │  • Custom                       │ │
│  └─────────────────────────────────┘ │
└─────────────────────────────────────┘
```

## 📊 Performance

- **Zero Allocation**: Optimized paths for high-frequency operations
- **Parallel Processing**: Concurrent invalidation across multiple providers
- **Memory Pooling**: Efficient object reuse
- **Batch Optimization**: Bulk operations for better throughput

## 🧪 Testing

Run the test suite:

```bash
dotnet test
```

## 📄 License

MIT License - see [LICENSE](LICENSE) for details.

## 🤝 Contributing

Contributions are welcome! Please read our contributing guidelines and submit pull requests.

---

**Note**: This is currently in alpha (v0.1.0-alpha). APIs may change before stable release.