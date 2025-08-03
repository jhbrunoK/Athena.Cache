---
layout: default
title: Cache Key Generation
---

# Cache Key Generation

Athena.Cache automatically generates cache keys from your controller context and method parameters. Understanding how this works helps you optimize cache usage and avoid key collisions.

## Automatic Key Generation

By default, cache keys are generated using this pattern:
```
{Namespace}:{ControllerName}:{ActionName}_{parameters}
```

### Basic Example

```csharp
[AthenaCache(ExpirationMinutes = 30)]
public async Task<UserDto> GetUser(int id)
{
    return await _userService.GetUserAsync(id);
}
// Generated key: "MyApp:UsersController:GetUser_id:123"
```

### Multiple Parameters

```csharp
[AthenaCache(ExpirationMinutes = 30)]
public async Task<UserDto[]> GetUsers(string? search, int page, int size)
{
    return await _userService.GetUsersAsync(search, page, size);
}
// Generated key: "MyApp:UsersController:GetUsers_search:john_page:1_size:10"
```

### Complex Objects

```csharp
public class UserFilter
{
    public string? Name { get; set; }
    public int? MinAge { get; set; }
    public string[]? Roles { get; set; }
}

[AthenaCache(ExpirationMinutes = 15)]
public async Task<UserDto[]> SearchUsers([FromQuery] UserFilter filter)
{
    return await _userService.SearchUsersAsync(filter);
}
// Generated key: "MyApp:UsersController:SearchUsers_filter:Name=john,MinAge=25,Roles=admin,user"
```

## Key Customization

### Custom Key Patterns

Use the `KeyPattern` property to define custom key templates:

```csharp
[AthenaCache(KeyPattern = "user_{id}")]
public async Task<UserDto> GetUser(int id) { ... }
// Result: "user_123"

[AthenaCache(KeyPattern = "users_page_{page}_size_{size}")]
public async Task<UserDto[]> GetUsers(int page, int size) { ... }
// Result: "users_page_1_size_10"

[AthenaCache(KeyPattern = "user_{id}_profile_{includeDetails}")]
public async Task<UserDto> GetUserProfile(int id, bool includeDetails) { ... }
// Result: "user_123_profile_true"
```

### Key Prefixes

Add custom prefixes to distinguish between different versions or environments:

```csharp
[AthenaCache(KeyPrefix = "v2")]
public async Task<DataDto> GetData() { ... }
// Result: "v2:MyApp:DataController:GetData"

[AthenaCache(KeyPrefix = "admin_api")]
public async Task<UserDto[]> GetUsersForAdmin() { ... }
// Result: "admin_api:MyApp:UsersController:GetUsersForAdmin"
```

### Namespace Configuration

Set the global namespace in your configuration:

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Namespace = "MyApp_Prod";  // All keys will start with this
});
```

```json
// appsettings.json
{
  "AthenaCache": {
    "Namespace": "MyApp_Prod"
  }
}
```

## Parameter Handling

### Ignored Parameters

Some parameters are automatically ignored for key generation:

```csharp
[AthenaCache]
public async Task<UserDto[]> GetUsers(
    string search,
    int page,
    [FromServices] ILogger<UsersController> logger)  // Ignored (service injection)
{
    // logger parameter doesn't affect cache key
}
// Key: "MyApp:UsersController:GetUsers_search:john_page:1"
```

### Custom Parameter Inclusion/Exclusion

```csharp
[AthenaCache(IncludeParameters = new[] { "search", "category" })]
public async Task<ProductDto[]> SearchProducts(
    string search, 
    string category, 
    int page,           // Excluded from key
    int size)           // Excluded from key
{
    // Only search and category affect the cache key
}
// Key: "MyApp:ProductsController:SearchProducts_search:laptop_category:electronics"

[AthenaCache(ExcludeParameters = new[] { "trackingId" })]
public async Task<OrderDto> GetOrder(int id, string trackingId)
{
    // trackingId doesn't affect cache key
}
// Key: "MyApp:OrdersController:GetOrder_id:123"
```

## Custom Key Generators

For advanced scenarios, implement your own key generation logic:

```csharp
public class CustomCacheKeyGenerator : ICacheKeyGenerator
{
    public string GenerateKey(string baseKey, object parameters)
    {
        // Custom logic for key generation
        var hash = ComputeHash(parameters);
        return $"custom_{baseKey}_{hash}";
    }
    
    private string ComputeHash(object parameters)
    {
        // Your custom hashing logic
        return parameters.ToString().GetHashCode().ToString("X");
    }
}

// Register in Program.cs
builder.Services.AddSingleton<ICacheKeyGenerator, CustomCacheKeyGenerator>();
```

## Key Collision Avoidance

### Controller Name Disambiguation

When you have controllers with similar names:

```csharp
// AdminUsersController
[AthenaCache(KeyPrefix = "admin")]
public async Task<UserDto[]> GetUsers() { ... }
// Key: "admin:MyApp:AdminUsersController:GetUsers"

// PublicUsersController  
[AthenaCache(KeyPrefix = "public")]
public async Task<UserDto[]> GetUsers() { ... }
// Key: "public:MyApp:PublicUsersController:GetUsers"
```

### Action Name Disambiguation

```csharp
[AthenaCache(KeyPattern = "users_active")]
public async Task<UserDto[]> GetActiveUsers() { ... }

[AthenaCache(KeyPattern = "users_inactive")]
public async Task<UserDto[]> GetInactiveUsers() { ... }
```

## Key Length Optimization

For very long parameter lists, Athena.Cache automatically uses hash-based keys:

```csharp
[AthenaCache]
public async Task<ReportDto> GenerateReport(
    DateTime startDate,
    DateTime endDate,
    string[] categories,
    string[] regions,
    bool includeDetails,
    ReportFormat format)
{
    // Long parameter list is automatically hashed
}
// Key: "MyApp:ReportsController:GenerateReport_hash:A1B2C3D4E5F6"
```

### Manual Hash Mode

Force hash-based keys for sensitive data:

```csharp
[AthenaCache(UseHashedKey = true)]
public async Task<UserDto> GetUserByEmail(string email)
{
    // Email is hashed for privacy
}
// Key: "MyApp:UsersController:GetUserByEmail_hash:F7E8D9C0B1A2"
```

## Key Inspection

### Debug Key Generation

Enable key logging to see generated keys:

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Logging.LogCacheOperations = true;  // Logs key generation
});
```

### Runtime Key Inspection

```csharp
[ApiController]
public class CacheDebugController : ControllerBase
{
    private readonly ICacheKeyGenerator _keyGenerator;

    [HttpPost("preview-key")]
    public IActionResult PreviewKey([FromBody] KeyPreviewRequest request)
    {
        var key = _keyGenerator.GenerateKey(request.BaseKey, request.Parameters);
        return Ok(new { GeneratedKey = key });
    }
}
```

## Best Practices

### 1. Keep Keys Predictable

```csharp
// Good: Clear, predictable pattern
[AthenaCache(KeyPattern = "product_{id}_details")]
public async Task<ProductDto> GetProduct(int id) { ... }

// Avoid: Complex patterns that are hard to debug
[AthenaCache(KeyPattern = "prod_{id}_{DateTime.Now.Ticks}")]
public async Task<ProductDto> GetProduct(int id) { ... }
```

### 2. Use Meaningful Prefixes

```csharp
// Good: Clear separation between API versions
[AthenaCache(KeyPrefix = "api_v1")]
public async Task<UserDto> GetUserV1(int id) { ... }

[AthenaCache(KeyPrefix = "api_v2")]
public async Task<UserDtoV2> GetUserV2(int id) { ... }
```

### 3. Consider Parameter Sensitivity

```csharp
// Good: Hash sensitive parameters
[AthenaCache(UseHashedKey = true)]
public async Task<UserDto> GetUserBySSN(string ssn) { ... }

// Good: Exclude non-essential parameters
[AthenaCache(ExcludeParameters = new[] { "requestId", "correlationId" })]
public async Task<DataDto> GetData(string query, string requestId, string correlationId) { ... }
```

### 4. Namespace for Multi-tenant Applications

```csharp
// Configure per tenant
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Namespace = $"Tenant_{tenantId}";
});

// Or use custom key patterns
[AthenaCache(KeyPattern = "tenant_{tenantId}_user_{userId}")]
public async Task<UserDto> GetUser(string tenantId, int userId) { ... }
```

## Troubleshooting

### Common Issues

**Keys are too long**
- Use `UseHashedKey = true` for long parameter lists
- Implement custom key generator with shorter patterns

**Key collisions**
- Add unique prefixes to similar actions
- Include more distinguishing parameters in key pattern

**Parameters not included in key**
- Check if parameters are marked with `[FromServices]`
- Verify parameter names match `IncludeParameters` array

**Cache not working as expected**
- Enable key logging to inspect generated keys
- Use cache debug endpoints to verify key generation

For more advanced scenarios, see:
- **[Cache Invalidation]({{ site.baseurl }}/fundamentals/cache-invalidation)** - How invalidation affects key patterns
- **[Performance Optimization]({{ site.baseurl }}/performance/zero-memory-optimization)** - Key generation performance
- **[Custom Providers]({{ site.baseurl }}/advanced/custom-providers)** - Implementing custom key strategies