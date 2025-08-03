# ⚡ Athena.Cache.SourceGenerator

**Compile-time cache configuration generation for Athena Cache with AOT support and zero-runtime overhead**

Athena.Cache.SourceGenerator provides compile-time analysis and code generation for Athena Cache, automatically generating optimal cache configurations, reducing runtime overhead, and enabling full AOT (Ahead-of-Time) compilation support.

## ✨ Key Features

- ⚡ **Compile-time Generation**: Zero runtime overhead with pre-generated configurations
- 🎯 **AOT Support**: Full compatibility with Native AOT compilation
- 🔍 **Automatic Discovery**: Finds controllers and cache attributes automatically
- 🏗️ **Code Generation**: Generates optimized cache configuration classes
- 📊 **Build-time Validation**: Catches configuration errors at compile time
- 🎨 **Convention Analysis**: Validates naming conventions and patterns
- 🧠 **Intelligent Inference**: Automatically infers table relationships
- 📈 **Performance Optimization**: Eliminates reflection and runtime discovery
- 🔧 **Incremental Generation**: Fast builds with incremental compilation support

## 🚀 Quick Start

### Installation

```bash
# Add Source Generator to your application project
dotnet add package Athena.Cache.SourceGenerator

# Core dependency (if not already installed)
dotnet add package Athena.Cache.Core
```

> **⚠️ Important**: Add the Source Generator to your **application project** (the one with controllers), not to library projects.

### Project File Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
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

### Basic Usage

The Source Generator automatically analyzes your controllers and generates cache configurations:

```csharp
// Your controller (no additional setup required)
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    [CacheInvalidateOn("Users")]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
    {
        // Your implementation
        return Ok(await _userService.GetUsersAsync());
    }

    [HttpPost]
    [CacheInvalidateOn("Users", "UserProfiles")]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserRequest request)
    {
        // Your implementation
        return Ok(await _userService.CreateUserAsync(request));
    }
}
```

### Generated Code

The Source Generator automatically creates optimized cache configuration:

```csharp
// Generated: AthenaCacheConfiguration.g.cs
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Models;

namespace YourApp.Generated
{
    public static class AthenaCacheConfiguration
    {
        public static void RegisterCacheConfigurations(ICacheConfigurationRegistry registry)
        {
            // Users controller configurations
            registry.RegisterConfiguration("UsersController.GetUsers", new CacheConfiguration
            {
                ExpirationMinutes = 30,
                InvalidationTables = new[] { "Users" }
            });

            registry.RegisterConfiguration("UsersController.CreateUser", new CacheConfiguration
            {
                InvalidationTables = new[] { "Users", "UserProfiles" }
            });

            // Additional controllers...
        }
    }
}
```

## 🔧 Advanced Configuration

### MSBuild Properties

You can customize the Source Generator behavior through MSBuild properties:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <!-- Source Generator Configuration -->
    <AthenaCacheGenerateDebugOutput>true</AthenaCacheGenerateDebugOutput>
    <AthenaCacheNamespace>MyApp.Cache.Generated</AthenaCacheNamespace>
    <AthenaCacheConfigurationClassName>CacheRegistry</AthenaCacheConfigurationClassName>
  </PropertyGroup>

</Project>
```

### Generator Attributes

Control generation behavior with additional attributes:

```csharp
// Exclude specific controllers from generation
[GeneratedCacheIgnore]
[ApiController]
public class InternalController : ControllerBase
{
    // This controller will be ignored by the generator
}

// Custom cache key generation
[ApiController]
public class ProductsController : ControllerBase
{
    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 60)]
    [GeneratedCacheKey("Product_{id}")]  // Custom key template
    public async Task<ProductDto> GetProduct(int id)
    {
        return await _productService.GetProductAsync(id);
    }
}
```

## 📊 Build-time Analysis

### Convention Validation

The Source Generator validates naming conventions and provides helpful diagnostics:

```csharp
// ✅ Good: Follows convention
[ApiController]
public class UsersController : ControllerBase
{
    [CacheInvalidateOn("Users")]  // ✅ Matches controller name
    public async Task<UserDto> CreateUser() { /* ... */ }
}

// ⚠️ Warning: Convention mismatch
[ApiController]
public class ProductsController : ControllerBase
{
    [CacheInvalidateOn("Items")]  // ⚠️ May not match intended table
    public async Task<ProductDto> CreateProduct() { /* ... */ }
}
```

### Compile-time Diagnostics

The generator provides helpful compile-time warnings and errors:

```text
Warning ATHENA001: Table name 'Items' in ProductsController.CreateProduct doesn't follow convention. Did you mean 'Products'?
Warning ATHENA002: Cache expiration of 1440 minutes (24 hours) is unusually long. Consider shorter expiration.
Error ATHENA003: CacheInvalidateOn attribute specifies empty table name in UsersController.UpdateUser.
```

## 🎯 Performance Benefits

### Generated Code Example

```csharp
// Instead of runtime reflection:
public void DiscoverCacheConfigurations()
{
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

// Generated optimized code:
public static void RegisterCacheConfigurations(ICacheConfigurationRegistry registry)
{
    registry.RegisterConfiguration("UsersController.GetUsers", new CacheConfiguration
    {
        ExpirationMinutes = 30,
        InvalidationTables = new[] { "Users" }
    });
    // Direct, optimized registration
}
```

## 🔍 Debugging and Diagnostics

### Generated Code Inspection

Enable debug output to see generated code:

```xml
<PropertyGroup>
    <AthenaCacheGenerateDebugOutput>true</AthenaCacheGenerateDebugOutput>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

This will output generated files to the `Generated` folder for inspection.

### Build Diagnostics

View Source Generator diagnostics in build output:

```text
Build started...
1>AthenaCacheSourceGenerator: Found 5 controllers with cache attributes
1>AthenaCacheSourceGenerator: Generated cache configuration for UsersController
1>AthenaCacheSourceGenerator: Generated cache configuration for ProductsController
1>AthenaCacheSourceGenerator: Warning: OrdersController.GetOrders has long expiration (24 hours)
1>Build succeeded with warnings.
```

## 🏗️ Integration with Build Process

### CI/CD Considerations

```yaml
# GitHub Actions example
name: Build and Test

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
      
    - name: Build
      run: dotnet build --no-restore --configuration Release
      # Source Generator runs automatically during build
      
    - name: Test
      run: dotnet test --no-build --configuration Release
      
    - name: Publish AOT
      run: dotnet publish --configuration Release --runtime linux-x64 --self-contained
      # Generated code enables AOT compilation
```

### Docker Support

```dockerfile
# Dockerfile for AOT-compiled app with Source Generator
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY *.csproj .
RUN dotnet restore

COPY . .
# Source Generator runs during build
RUN dotnet publish -c Release -r linux-x64 --self-contained -p:PublishAot=true -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["./YourApp"]
```

## 🔧 Customization Options

### Custom Templates

Create custom code generation templates:

```csharp
// Custom template provider
public class CustomCacheTemplateProvider : ICacheTemplateProvider
{
    public string GenerateCacheConfiguration(ControllerInfo controller)
    {
        return $"""
            // Custom generated configuration for {controller.Name}
            registry.RegisterConfiguration("{controller.Name}.{controller.ActionName}", 
                new OptimizedCacheConfiguration
                {{
                    ExpirationMinutes = {controller.ExpirationMinutes},
                    InvalidationTables = new[] {{ {string.Join(", ", controller.Tables.Select(t => $"\"{t}\""))} }},
                    CustomProperty = "GeneratedValue"
                }});
            """;
    }
}
```

### Integration with Existing Code

```csharp
// Program.cs - Use generated configurations
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAthenaCacheComplete(options =>
{
    options.Namespace = "MyApp";
    // Other options...
});

// Register generated cache configurations
builder.Services.ConfigureAthenaCache(registry =>
{
    // Generated method automatically registers all configurations
    YourApp.Generated.AthenaCacheConfiguration.RegisterCacheConfigurations(registry);
});
```

## 🔌 Integration with Other Packages

Seamlessly works with:
- **[Athena.Cache.Core](https://www.nuget.org/packages/Athena.Cache.Core/)**: Provides the base caching functionality
- **[Athena.Cache.Redis](https://www.nuget.org/packages/Athena.Cache.Redis/)**: Optimizes Redis configurations
- **[Athena.Cache.Monitoring](https://www.nuget.org/packages/Athena.Cache.Monitoring/)**: Generates monitoring configurations
- **[Athena.Cache.Analytics](https://www.nuget.org/packages/Athena.Cache.Analytics/)**: Creates analytics metadata

## 🧪 Testing with Source Generator

### Unit Testing Generated Code

```csharp
[Test]
public void GeneratedCacheConfiguration_ShouldRegisterAllControllers()
{
    // Arrange
    var registry = new MockCacheConfigurationRegistry();
    
    // Act
    AthenaCacheConfiguration.RegisterCacheConfigurations(registry);
    
    // Assert
    Assert.AreEqual(5, registry.RegisteredConfigurations.Count);
    Assert.IsTrue(registry.HasConfiguration("UsersController.GetUsers"));
    Assert.IsTrue(registry.HasConfiguration("ProductsController.GetProducts"));
}

[Test]
public void UsersController_GetUsers_ShouldHaveCorrectCacheConfiguration()
{
    // Arrange
    var registry = new MockCacheConfigurationRegistry();
    AthenaCacheConfiguration.RegisterCacheConfigurations(registry);
    
    // Act
    var config = registry.GetConfiguration("UsersController.GetUsers");
    
    // Assert
    Assert.AreEqual(30, config.ExpirationMinutes);
    Assert.Contains("Users", config.InvalidationTables);
}
```

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/jhbrunoK/Athena.Cache/blob/main/LICENSE.txt) file for details.

## 🐛 Issues & Support

- **Repository**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)
- **Issues**: [https://github.com/jhbrunoK/Athena.Cache/issues](https://github.com/jhbrunoK/Athena.Cache/issues)
- **Documentation**: [https://github.com/jhbrunoK/Athena.Cache](https://github.com/jhbrunoK/Athena.Cache)