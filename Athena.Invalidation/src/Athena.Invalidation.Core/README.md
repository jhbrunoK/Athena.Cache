# Athena.Invalidation.Core

Core abstractions and interfaces for the universal cache invalidation engine.

## Installation

```bash
dotnet add package Athena.Invalidation.Core
```

## Key Interfaces

- `IInvalidationEngine` - Main invalidation engine interface
- `ICacheProvider` - Cache provider abstraction  
- `IInvalidationStrategy` - Strategy pattern for invalidation logic
- `IInvalidationContext` - Execution context for invalidation operations

## Usage

This package provides the foundational interfaces. For a complete solution, use the meta-package:

```bash
dotnet add package Athena.Invalidation
```

## License

MIT License