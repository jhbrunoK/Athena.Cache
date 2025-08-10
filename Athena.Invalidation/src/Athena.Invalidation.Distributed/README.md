# Athena.Invalidation.Distributed

Distributed cache invalidation system for multi-node applications with cluster-wide synchronization.

## Installation

```bash
dotnet add package Athena.Invalidation.Distributed
```

## Features

- Distributed cache invalidation
- Multi-node synchronization
- Event broadcasting across cluster
- Node discovery and health checks
- Conflict resolution strategies

## Usage

```csharp
services.AddDistributedInvalidation(options =>
{
    options.NodeId = "node-1";
    options.BroadcastChannel = "cache-invalidation";
});
```

For a complete solution with all dependencies, use the meta-package:

```bash
dotnet add package Athena.Invalidation.Advanced
```

## License

MIT License