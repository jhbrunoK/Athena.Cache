# Athena.Invalidation.FusionCache

FusionCache integration for the universal cache invalidation engine, providing high-performance hybrid caching with L1/L2 layers.

## Installation

```bash
dotnet add package Athena.Invalidation.FusionCache
```

## Features

- FusionCache L1/L2 hybrid caching
- Automatic cache synchronization
- Memory and distributed cache layers
- Advanced cache options and policies
- Fail-safe mechanisms

## Usage

```csharp
services.AddFusionCacheInvalidation();
```

For a complete solution with all dependencies, use the meta-package:

```bash
dotnet add package Athena.Invalidation.Redis
```

## License

MIT License