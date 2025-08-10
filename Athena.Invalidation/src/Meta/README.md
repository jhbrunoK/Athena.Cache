# Athena.Invalidation

Complete cache invalidation solution for .NET applications. This meta-package includes all core components needed to get started with cache invalidation.

## 🚀 Quick Start

### Installation

```bash
dotnet add package Athena.Invalidation
```

### Basic Usage

```csharp
// Program.cs
services.AddInvalidation();

// Controller
public class ProductController : ControllerBase 
{
    private readonly IInvalidationEngine _invalidation;
    
    [HttpPost]
    public async Task CreateProduct(Product product)
    {
        await _repository.CreateAsync(product);
        
        // Automatic cache invalidation
        await _invalidation.InvalidateByTableAsync("Products");
    }
}
```

### Advanced Configuration

```csharp
services.AddInvalidation(config => 
{
    config.UseMemoryCache()
          .EnableTracking()
          .WithStrategy<BasicInvalidationStrategy>()
          .EnableHierarchicalInvalidation();
});
```

## 📦 Included Packages

This meta-package automatically includes:

- **Athena.Invalidation.Core** - Core abstractions and interfaces
- **Athena.Invalidation.Engine** - Main invalidation engine implementation  
- **Athena.Invalidation.MemoryCache** - Memory cache integration
- **Athena.Invalidation.Strategies** - Built-in invalidation strategies
- **Athena.Invalidation.AspNetCore** - ASP.NET Core integration
- **Athena.Invalidation.Tracking** - Cache key tracking system

## 🔧 Extensions

Need more features? Add these optional packages:

- **Athena.Invalidation.Redis** - Redis and distributed caching support
- **Athena.Invalidation.Advanced** - CQRS, distributed invalidation, monitoring

## 📚 Documentation

- [GitHub Repository](https://github.com/jhbrunoK/Athena.Cache)
- [API Documentation](https://github.com/jhbrunoK/Athena.Cache/wiki)

## 📄 License

MIT License - see LICENSE file for details.