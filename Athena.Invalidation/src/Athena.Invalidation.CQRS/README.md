# Athena.Invalidation.CQRS

CQRS (Command Query Responsibility Segregation) pattern support for the universal cache invalidation engine.

## Installation

```bash
dotnet add package Athena.Invalidation.CQRS
```

## Features

- Command and Query separation
- Event sourcing integration
- Cache invalidation commands
- Query-specific caching strategies
- Event-driven invalidation

## Usage

```csharp
services.AddCQRSInvalidation();
```

For a complete solution with all dependencies, use the meta-package:

```bash
dotnet add package Athena.Invalidation
```

## License

MIT License