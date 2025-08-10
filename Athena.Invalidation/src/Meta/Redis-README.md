# Athena.Invalidation.Redis

Redis-based distributed cache invalidation extension for Athena.Invalidation. Perfect for distributed applications that need cache synchronization across multiple servers.

## 🚀 Quick Start

### Installation

```bash
dotnet add package Athena.Invalidation.Redis
```

### Basic Redis Setup

```csharp
// Program.cs
services.AddInvalidation(config => 
{
    config.UseRedis("localhost:6379")
          .UseMemoryCache(); // Optional: hybrid caching
});
```

### FusionCache Integration

```csharp
services.AddInvalidation(config => 
{
    config.UseFusionCache(fusionOptions => {
        fusionOptions.DefaultEntryOptions.Duration = TimeSpan.FromMinutes(10);
    })
    .WithRedisBackplane("localhost:6379");
});
```

### Multi-Provider Setup

```csharp
services.AddInvalidation(config => 
{
    config.UseRedis("redis-primary:6379")
          .UseMemoryCache()
          .EnableMultiProvider(); // Invalidate both simultaneously
});
```

## 📦 Included Packages

This meta-package includes:

- **Athena.Invalidation** - Core invalidation functionality (automatically included)
- **Athena.Invalidation.Redis** - StackExchange.Redis integration
- **Athena.Invalidation.FusionCache** - High-performance L1/L2 hybrid caching
- **Athena.Invalidation.MultiProvider** - Concurrent multi-cache management

## 🔧 Advanced Usage

### Distributed Invalidation

```csharp
// Invalidate cache across all connected servers
await _invalidation.InvalidateByTableAsync("Products");
```

### Health Checks

```csharp
services.AddHealthChecks()
    .AddRedis("localhost:6379");
```

### Connection Resilience

```csharp
services.AddInvalidation(config => 
{
    config.UseRedis("localhost:6379", redisOptions => {
        redisOptions.ConfigurationOptions.ConnectRetry = 3;
        redisOptions.ConfigurationOptions.ConnectTimeout = 5000;
    });
});
```

## 📚 Documentation

- [Redis Configuration Guide](https://github.com/jhbrunoK/Athena.Cache/wiki/Redis-Setup)
- [FusionCache Integration](https://github.com/jhbrunoK/Athena.Cache/wiki/FusionCache)
- [Multi-Provider Scenarios](https://github.com/jhbrunoK/Athena.Cache/wiki/Multi-Provider)

## 📄 License

MIT License - see LICENSE file for details.