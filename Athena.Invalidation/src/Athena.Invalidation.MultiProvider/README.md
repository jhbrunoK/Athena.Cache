# Athena.Invalidation.MultiProvider

Multi-provider cache system enabling simultaneous use of multiple cache backends with unified invalidation.

## Installation

```bash
dotnet add package Athena.Invalidation.MultiProvider
```

## Features

- Multiple cache provider support
- Unified invalidation across providers
- Provider failover and redundancy
- Cross-provider synchronization
- Configuration-driven provider selection

## Usage

```csharp
services.AddMultiProviderInvalidation()
    .AddMemoryCache()
    .AddRedisCache()
    .AddFusionCache();
```

For a complete solution with all dependencies, use the meta-package:

```bash
dotnet add package Athena.Invalidation.Redis
```

## License

MIT License