---
layout: default
title: Source Generator
---

# Source Generator

Athena.Cache.SourceGenerator provides compile-time optimizations that eliminate reflection overhead, enable AOT compilation, and provide build-time validation of your cache configuration.

## Why Source Generation?

Traditional caching libraries rely on runtime reflection to discover cache attributes, which causes:
- **Startup delays** while scanning assemblies
- **Runtime overhead** from reflection calls
- **AOT incompatibility** due to reflection dependencies
- **Runtime errors** from configuration mistakes

The Source Generator solves these issues by analyzing your code at compile time.

## Installation

### Add to Application Project

```bash
# Install in your main application project (not library projects)
dotnet add package Athena.Cache.SourceGenerator
```

### Project Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <!-- Enable AOT if desired -->
    <PublishAot>true</PublishAot>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Athena.Cache.Core" Version="1.0.0" />
    <PackageReference Include="Athena.Cache.SourceGenerator" Version="1.0.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

## How It Works

### Before Source Generator

```csharp
// Runtime reflection scanning (slow)
public void ConfigureServices(IServiceCollection services)
{
    services.AddAthenaCacheComplete();
    
    // At startup, Athena scans all assemblies using reflection
    foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
    {
        if (type.IsSubclassOf(typeof(ControllerBase)))
        {
            foreach (var method in type.GetMethods())
            {
                var cacheAttr = method.GetCustomAttribute<AthenaCacheAttribute>();
                // Expensive reflection calls...
            }
        }
    }
}
```

### With Source Generator

```csharp
// Generated at compile time (fast)
public void ConfigureServices(IServiceCollection services)
{
    services.AddAthenaCacheComplete();
    
    // Generated method - no reflection needed!
    services.ConfigureAthenaCache(registry =>
    {
        MyApp.Generated.AthenaCacheConfiguration.RegisterCacheConfigurations(registry);
    });
}
```

## Automatic Code Generation

The Source Generator analyzes your controllers and automatically generates optimized configuration code.

### Your Controller Code

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    [CacheInvalidateOn("Users")]
    public async Task<UserDto[]> GetUsers(string? search = null, int page = 1)
    {
        return await _userService.GetUsersAsync(search, page);
    }

    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 60, KeyPattern = "user_{id}")]
    [CacheInvalidateOn("Users")]
    public async Task<UserDto> GetUser(int id)
    {
        return await _userService.GetUserAsync(id);
    }

    [HttpPost]
    [CacheInvalidateOn("Users")]
    public async Task<UserDto> CreateUser([FromBody] CreateUserRequest request)
    {
        return await _userService.CreateUserAsync(request);
    }
}
```

### Generated Configuration Code

```csharp
// Generated: AthenaCacheConfiguration.g.cs
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Models;

namespace MyApp.Generated
{
    /// <summary>
    /// Generated cache configuration for compile-time optimization
    /// </summary>
    public static class AthenaCacheConfiguration
    {
        /// <summary>
        /// Registers all cache configurations found in the assembly
        /// </summary>
        public static void RegisterCacheConfigurations(ICacheConfigurationRegistry registry)
        {
            // UsersController configurations
            registry.RegisterConfiguration("UsersController.GetUsers", new CacheConfiguration
            {
                ExpirationMinutes = 30,
                InvalidationTables = new[] { "Users" },
                KeyPattern = null,
                KeyPrefix = null,
                IncludeParameters = new[] { "search", "page" },
                ExcludeParameters = null
            });

            registry.RegisterConfiguration("UsersController.GetUser", new CacheConfiguration
            {
                ExpirationMinutes = 60,
                InvalidationTables = new[] { "Users" },
                KeyPattern = "user_{id}",
                KeyPrefix = null,
                IncludeParameters = new[] { "id" },
                ExcludeParameters = null
            });

            registry.RegisterInvalidationConfiguration("UsersController.CreateUser", new InvalidationConfiguration
            {
                InvalidationTables = new[] { "Users" },
                InvalidationType = InvalidationType.Table
            });
        }
    }
}
```

## Build-time Validation

The Source Generator validates your cache configuration at compile time.

### Convention Validation

```csharp
// ✅ Good: Follows convention
[ApiController]
public class ProductsController : ControllerBase
{
    [CacheInvalidateOn("Products")]  // ✅ Matches controller name
    public async Task<ProductDto> CreateProduct() { /* ... */ }
}

// ⚠️ Warning: Convention mismatch
[ApiController]
public class ProductsController : ControllerBase
{
    [CacheInvalidateOn("Items")]  // ⚠️ May not match intended table
    public async Task<ProductDto> CreateProduct() { /* ... */ }
}
```

### Build Output

```text
Build started...
1>AthenaCacheSourceGenerator: Found 3 controllers with cache attributes
1>AthenaCacheSourceGenerator: Generated cache configuration for UsersController
1>AthenaCacheSourceGenerator: Generated cache configuration for ProductsController  
1>AthenaCacheSourceGenerator: Warning ATHENA001: Table name 'Items' in ProductsController.CreateProduct doesn't follow convention. Did you mean 'Products'?
1>Build succeeded with warnings.
```

## Compilation Diagnostics

The Source Generator provides helpful diagnostics during compilation.

### Warning Examples

```csharp
// ATHENA001: Convention mismatch
[CacheInvalidateOn("WrongTableName")]  
public async Task UpdateProduct() { /* ... */ }

// ATHENA002: Long expiration warning  
[AthenaCache(ExpirationMinutes = 1440)]  // 24 hours - unusually long
public async Task GetStaticData() { /* ... */ }

// ATHENA003: Empty invalidation table
[CacheInvalidateOn("")]  // Empty string
public async Task UpdateData() { /* ... */ }
```

### Error Examples

```csharp
// ATHENA004: Invalid key pattern
[AthenaCache(KeyPattern = "invalid_{nonExistentParameter}")]
public async Task GetData(int id) { /* ... */ }  // 'nonExistentParameter' not found

// ATHENA005: Conflicting attributes
[AthenaCache]
[NoCache]  // Conflicting attributes
public async Task GetData() { /* ... */ }
```

## AOT Compilation Support

The Source Generator enables full AOT compilation support.

### AOT Project Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Athena.Cache.Core" Version="1.0.0" />
    <PackageReference Include="Athena.Cache.SourceGenerator" Version="1.0.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

### AOT-Compatible Code

```csharp
// This code is fully AOT compatible with Source Generator
[ApiController]
public class AOTCompatibleController : ControllerBase
{
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    public async Task<DataDto[]> GetData()
    {
        // No reflection used - fully AOT compatible
        return await _dataService.GetDataAsync();
    }
}
```

### Publishing for AOT

```bash
# Publish as AOT binary
dotnet publish -c Release -r linux-x64 --self-contained

# The resulting binary:
# - Contains no reflection dependencies
# - Starts up faster (no reflection scanning)
# - Uses less memory (no reflection caches)
# - Has better performance (no reflection overhead)
```

## Debugging Generated Code

### Enable Debug Output

```xml
<PropertyGroup>
    <AthenaCacheGenerateDebugOutput>true</AthenaCacheGenerateDebugOutput>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

This outputs generated files to the `Generated` folder for inspection.

### Generated File Structure

```
Generated/
├── Athena.Cache.SourceGenerator/
│   ├── AthenaCacheConfiguration.g.cs
│   ├── CacheMetadata.g.cs
│   └── DiagnosticInfo.g.cs
```

### Sample Generated Metadata

```csharp
// Generated: CacheMetadata.g.cs
namespace MyApp.Generated
{
    internal static class CacheMetadata
    {
        public static readonly CacheControllerInfo[] Controllers = new[]
        {
            new CacheControllerInfo
            {
                Name = "UsersController",
                Actions = new[]
                {
                    new CacheActionInfo
                    {
                        Name = "GetUsers",
                        HasCacheAttribute = true,
                        ExpirationMinutes = 30,
                        InvalidationTables = new[] { "Users" },
                        Parameters = new[] { "search", "page" }
                    }
                }
            }
        };
    }
}
```

## Performance Benefits

### Startup Performance

```csharp
// Benchmark results
[MemoryDiagnoser]
public class StartupBenchmarks
{
    [Benchmark]
    public void StartupWithReflection()
    {
        // Traditional approach: 50ms startup overhead
        var services = new ServiceCollection();
        services.AddAthenaCacheComplete();
        // Reflection scanning happens here
    }

    [Benchmark]
    public void StartupWithSourceGenerator()
    {
        // Source Generator approach: 5ms startup overhead
        var services = new ServiceCollection();
        services.AddAthenaCacheComplete();
        services.ConfigureAthenaCache(registry =>
        {
            // Pre-generated configuration - no reflection
            AthenaCacheConfiguration.RegisterCacheConfigurations(registry);
        });
    }
}
```

### Runtime Performance

```csharp
// No reflection overhead during cache operations
[Benchmark]
public async Task CacheOperationWithSourceGenerator()
{
    // Cache configuration is pre-built
    // No runtime reflection calls
    // Faster cache key generation
    // Better memory usage
    var result = await _controller.GetUsers();
}
```

## MSBuild Integration

### Custom MSBuild Properties

```xml
<PropertyGroup>
    <!-- Generator behavior -->
    <AthenaCacheGenerateDebugOutput>false</AthenaCacheGenerateDebugOutput>
    <AthenaCacheNamespace>$(RootNamespace).Generated</AthenaCacheNamespace>
    <AthenaCacheConfigurationClassName>CacheRegistry</AthenaCacheConfigurationClassName>
    
    <!-- Validation settings -->
    <AthenaCacheValidateConventions>true</AthenaCacheValidateConventions>
    <AthenaCacheTreatWarningsAsErrors>false</AthenaCacheTreatWarningsAsErrors>
    
    <!-- Performance settings -->
    <AthenaCacheEnableOptimizations>true</AthenaCacheEnableOptimizations>
</PropertyGroup>
```

### Build Events

```xml
<Target Name="AthenaCachePostBuild" AfterTargets="Build">
    <Message Text="Athena Cache Source Generator completed" Importance="high" />
    <Message Text="Generated cache configurations: $(AthenaCacheGeneratedConfigurations)" />
</Target>
```

## Integration with CI/CD

### GitHub Actions

```yaml
name: Build and Test with Source Generator

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'
        
    - name: Restore dependencies
      run: dotnet restore
      
    - name: Build with Source Generator
      run: dotnet build --no-restore --configuration Release
      # Source Generator runs automatically during build
      
    - name: Test
      run: dotnet test --no-build --configuration Release
      
    - name: Publish AOT
      run: dotnet publish --configuration Release --runtime linux-x64 --self-contained -p:PublishAot=true
```

### Docker with AOT

```dockerfile
# Multi-stage build with AOT
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY *.csproj ./
RUN dotnet restore

# Copy source code
COPY . .

# Build with Source Generator and publish as AOT
RUN dotnet publish -c Release -r linux-x64 --self-contained -p:PublishAot=true -o /app/publish

# Runtime stage (smaller base image for AOT)
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# AOT binary runs directly (no .NET runtime needed)
ENTRYPOINT ["./MyApp"]
```

## Advanced Configuration

### Custom Code Templates

Create custom templates for generated code:

```csharp
// CustomCacheGenerator.cs
[Generator]
public class CustomCacheGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Custom generation logic
        var customTemplate = """
            // Custom cache configuration template
            public static void RegisterCustomCacheConfigurations(ICacheConfigurationRegistry registry)
            {
                {{#each controllers}}
                // {{name}} configurations
                {{#each actions}}
                registry.RegisterConfiguration("{{../name}}.{{name}}", new CacheConfiguration
                {
                    ExpirationMinutes = {{expirationMinutes}},
                    CustomProperty = "{{customValue}}"
                });
                {{/each}}
                {{/each}}
            }
            """;
    }
}
```

### Integration with Existing Code

```csharp
// Combine generated configuration with manual configuration
public void ConfigureServices(IServiceCollection services)
{
    services.AddAthenaCacheComplete();
    
    services.ConfigureAthenaCache(registry =>
    {
        // Register generated configurations
        AthenaCacheConfiguration.RegisterCacheConfigurations(registry);
        
        // Add manual configurations for special cases
        registry.RegisterConfiguration("CustomController.SpecialAction", new CacheConfiguration
        {
            ExpirationMinutes = 120,
            KeyPattern = "special_{id}_{timestamp}",
            InvalidationTables = new[] { "SpecialData", "AuditLog" }
        });
    });
}
```

## Troubleshooting

### Common Issues

**Source Generator not running**
```xml
<!-- Ensure correct package reference -->
<PackageReference Include="Athena.Cache.SourceGenerator" Version="1.0.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

**Generated code not found**
```bash
# Clean and rebuild
dotnet clean
dotnet build
```

**AOT compilation fails**
```xml
<!-- Check for unsupported reflection usage -->
<PropertyGroup>
    <PublishAot>true</PublishAot>
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>link</TrimMode>
</PropertyGroup>
```

**Build warnings about conventions**
```csharp
// Fix naming conventions or suppress warnings
[CacheInvalidateOn("Users")]  // Match controller name
// OR
[CacheInvalidateOn("CustomTable")]
[SuppressMessage("ATHENA001", "Intentional table name difference")]
```

## Next Steps

- **[Zero Memory Optimization]({{ site.baseurl }}/performance/zero-memory-optimization)** - Memory optimization techniques
- **[Production Tuning]({{ site.baseurl }}/performance/production-tuning)** - Overall performance tuning
- **[AOT Deployment]({{ site.baseurl }}/advanced/aot-deployment)** - AOT deployment strategies