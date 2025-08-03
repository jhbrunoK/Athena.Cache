---
layout: default
title: Cache Invalidation
---

# Cache Invalidation

Cache invalidation determines when cached data should be removed or refreshed. Athena.Cache provides multiple invalidation strategies to ensure your cache stays in sync with your data.

## Table-based Invalidation

The most common pattern is to invalidate cache when database tables change.

### Basic Usage

```csharp
[HttpGet]
[AthenaCache(ExpirationMinutes = 30)]
[CacheInvalidateOn("Users")]  // This cache is tagged with "Users"
public async Task<UserDto[]> GetUsers()
{
    return await _userService.GetUsersAsync();
}

[HttpPost]
[CacheInvalidateOn("Users")]  // This clears all "Users" tagged caches
public async Task<UserDto> CreateUser([FromBody] CreateUserRequest request)
{
    return await _userService.CreateUserAsync(request);
}
```

When `CreateUser` is called, it automatically clears the cache from `GetUsers`.

### Multiple Table Dependencies

```csharp
[HttpGet("user-profiles")]
[AthenaCache(ExpirationMinutes = 45)]
[CacheInvalidateOn("Users")]        // Depends on Users table
[CacheInvalidateOn("UserProfiles")] // Also depends on UserProfiles table
public async Task<UserProfileDto[]> GetUserProfiles()
{
    return await _userService.GetUserProfilesAsync();
}

[HttpPut("{id}/profile")]
[CacheInvalidateOn("UserProfiles")]  // Only clears UserProfiles caches
public async Task UpdateUserProfile(int id, [FromBody] UpdateProfileRequest request)
{
    await _userService.UpdateUserProfileAsync(id, request);
}

[HttpDelete("{id}")]
[CacheInvalidateOn("Users")]         // Clears both Users and UserProfiles caches
[CacheInvalidateOn("UserProfiles")]  // because user deletion affects both tables
public async Task DeleteUser(int id)
{
    await _userService.DeleteUserAsync(id);
}
```

## Pattern-based Invalidation

For more granular control, use pattern-based invalidation to target specific cache keys.

### Wildcard Patterns

```csharp
[HttpPut("{id}")]
[CacheInvalidateOn("Users", InvalidationType.Pattern, "user_*")]
public async Task UpdateUser(int id, [FromBody] UpdateUserRequest request)
{
    await _userService.UpdateUserAsync(id, request);
    // Clears: "user_123", "user_456", "user_profile_123", etc.
}
```

### Specific Key Patterns

```csharp
[HttpPut("{id}/avatar")]
[CacheInvalidateOn("Users", InvalidationType.Pattern, "user_{id}_*")]
public async Task UpdateUserAvatar(int id, [FromBody] AvatarRequest request)
{
    await _userService.UpdateUserAvatarAsync(id, request);
    // Clears: "user_123_profile", "user_123_details", etc.
    // Preserves: "user_456_profile", "users_list", etc.
}
```

### Complex Patterns

```csharp
[HttpPost("{userId}/orders")]
[CacheInvalidateOn("Orders", InvalidationType.Pattern, "user_{userId}_orders*")]
[CacheInvalidateOn("Orders", InvalidationType.Pattern, "orders_summary_*")]
public async Task CreateOrder(int userId, [FromBody] CreateOrderRequest request)
{
    await _orderService.CreateOrderAsync(userId, request);
    // Clears user-specific order caches and summary caches
}
```

## Exact Key Invalidation

Invalidate specific cache keys when you know exactly what to clear.

```csharp
[HttpPut("user/{id}/status")]
[CacheInvalidateOn("Users", InvalidationType.Exact, "active_users_count")]
[CacheInvalidateOn("Users", InvalidationType.Exact, "user_statistics")]
public async Task UpdateUserStatus(int id, [FromBody] StatusUpdateRequest request)
{
    await _userService.UpdateUserStatusAsync(id, request);
    // Only clears the specific keys: "active_users_count" and "user_statistics"
}
```

## Programmatic Invalidation

For scenarios where attributes aren't enough, use programmatic invalidation.

### Using ICacheInvalidator

```csharp
[ApiController]
public class AdminController : ControllerBase
{
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IUserService _userService;

    public AdminController(ICacheInvalidator cacheInvalidator, IUserService userService)
    {
        _cacheInvalidator = cacheInvalidator;
        _userService = userService;
    }

    [HttpPost("bulk-update-users")]
    public async Task<IActionResult> BulkUpdateUsers([FromBody] BulkUpdateRequest request)
    {
        await _userService.BulkUpdateUsersAsync(request);
        
        // Clear multiple table caches
        await _cacheInvalidator.InvalidateByTableAsync("Users");
        await _cacheInvalidator.InvalidateByTableAsync("UserProfiles");
        await _cacheInvalidator.InvalidateByTableAsync("UserStatistics");
        
        return Ok();
    }

    [HttpPost("clear-cache/{pattern}")]
    public async Task<IActionResult> ClearCacheByPattern(string pattern)
    {
        await _cacheInvalidator.InvalidateByPatternAsync(pattern);
        return Ok($"Cleared caches matching pattern: {pattern}");
    }
}
```

### Conditional Invalidation

```csharp
[HttpPut("{id}")]
public async Task<IActionResult> UpdateUser(
    int id, 
    [FromBody] UpdateUserRequest request,
    [FromServices] ICacheInvalidator cacheInvalidator)
{
    var updatedUser = await _userService.UpdateUserAsync(id, request);
    
    // Conditional invalidation based on what changed
    if (request.EmailChanged)
    {
        await cacheInvalidator.InvalidateByPatternAsync($"user_{id}_*");
    }
    
    if (request.RoleChanged)
    {
        await cacheInvalidator.InvalidateByTableAsync("UserRoles");
        await cacheInvalidator.InvalidateByTableAsync("Permissions");
    }
    
    return Ok(updatedUser);
}
```

## Convention-based Invalidation

Enable automatic invalidation based on controller naming conventions.

### Configuration

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Convention.EnableConventionBasedInvalidation = true;
    options.Convention.ControllerSuffix = "Controller";
    options.Convention.TableSuffix = "";
});
```

### Automatic Behavior

```csharp
// With convention enabled:
public class UsersController : ControllerBase
{
    [HttpGet]
    [AthenaCache]  // Automatically adds [CacheInvalidateOn("Users")]
    public async Task<UserDto[]> GetUsers() { ... }

    [HttpPost]  // Automatically adds [CacheInvalidateOn("Users")]
    public async Task<UserDto> CreateUser([FromBody] CreateUserRequest request) { ... }
}

// Override convention when needed:
[HttpGet("external")]
[AthenaCache]
[NoConventionInvalidation]  // Skip automatic invalidation
public async Task<ExternalDataDto> GetExternalData() { ... }
```

## Distributed Invalidation

When using Redis, invalidation works across all application instances.

### Cross-instance Invalidation

```csharp
// Instance A
[HttpPost]
[CacheInvalidateOn("Products")]
public async Task<ProductDto> CreateProduct([FromBody] CreateProductRequest request)
{
    var product = await _productService.CreateProductAsync(request);
    // This invalidation is automatically propagated to Instance B, C, etc.
    return product;
}
```

### Manual Cross-instance Invalidation

```csharp
[ApiController]
public class CacheManagementController : ControllerBase
{
    private readonly IDistributedCacheInvalidator _distributedInvalidator;

    [HttpDelete("distributed/table/{tableName}")]
    public async Task<IActionResult> InvalidateTableAcrossInstances(string tableName)
    {
        await _distributedInvalidator.InvalidateByTableAsync(tableName);
        return Ok($"Invalidated {tableName} across all instances");
    }

    [HttpDelete("distributed/pattern/{pattern}")]
    public async Task<IActionResult> InvalidatePatternAcrossInstances(string pattern)
    {
        await _distributedInvalidator.InvalidateByPatternAsync(pattern);
        return Ok($"Invalidated pattern {pattern} across all instances");
    }
}
```

## Batch Invalidation

Efficiently invalidate multiple caches in a single operation.

```csharp
[HttpPost("reorganize-departments")]
public async Task<IActionResult> ReorganizeDepartments(
    [FromBody] ReorganizeRequest request,
    [FromServices] ICacheInvalidator cacheInvalidator)
{
    await _organizationService.ReorganizeDepartmentsAsync(request);
    
    // Batch invalidation
    await cacheInvalidator.InvalidateMultipleAsync(new[]
    {
        ("Departments", InvalidationType.Table),
        ("Users", InvalidationType.Table),
        ("employee_*", InvalidationType.Pattern),
        ("department_hierarchy", InvalidationType.Exact)
    });
    
    return Ok();
}
```

## Invalidation Events

Subscribe to invalidation events for monitoring and debugging.

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Events.OnCacheInvalidated = (context) =>
    {
        var logger = context.ServiceProvider.GetService<ILogger<Program>>();
        logger?.LogInformation("Cache invalidated: {InvalidationType} - {Target}", 
            context.InvalidationType, context.Target);
        return Task.CompletedTask;
    };
});
```

### Custom Invalidation Handlers

```csharp
public class CacheInvalidationHandler
{
    private readonly ILogger<CacheInvalidationHandler> _logger;
    private readonly IMetricsCollector _metrics;

    public async Task HandleInvalidation(CacheInvalidationContext context)
    {
        // Log invalidation
        _logger.LogInformation("Invalidated: {Type} - {Target}", 
            context.InvalidationType, context.Target);
        
        // Record metrics
        await _metrics.RecordInvalidationAsync(context.InvalidationType, context.Target);
        
        // Custom business logic
        if (context.Target == "Users")
        {
            await NotifyUserManagementSystemAsync();
        }
    }
}
```

## Best Practices

### 1. Use Table-based for Most Cases

```csharp
// Good: Clear and predictable
[CacheInvalidateOn("Users")]
[CacheInvalidateOn("UserProfiles")]

// Avoid: Too many specific patterns
[CacheInvalidateOn("Users", InvalidationType.Pattern, "user_123_*")]
[CacheInvalidateOn("Users", InvalidationType.Pattern, "user_456_*")]
// ... (better to use table-based)
```

### 2. Be Specific with Patterns

```csharp
// Good: Specific pattern
[CacheInvalidateOn("Orders", InvalidationType.Pattern, "user_{userId}_orders*")]

// Avoid: Too broad pattern
[CacheInvalidateOn("Orders", InvalidationType.Pattern, "*")]  // Clears everything!
```

### 3. Group Related Invalidations

```csharp
// Good: Group related invalidations together
[HttpDelete("{id}")]
[CacheInvalidateOn("Users")]
[CacheInvalidateOn("UserProfiles")]
[CacheInvalidateOn("UserStatistics")]
public async Task DeleteUser(int id) { ... }
```

### 4. Use Conditional Invalidation for Complex Scenarios

```csharp
// Good: Only invalidate what actually changed
public async Task UpdateUserProfile(int userId, UpdateProfileRequest request)
{
    var changes = await _userService.UpdateUserProfileAsync(userId, request);
    
    if (changes.ProfileChanged)
        await _cacheInvalidator.InvalidateByTableAsync("UserProfiles");
    
    if (changes.PreferencesChanged)
        await _cacheInvalidator.InvalidateByTableAsync("UserPreferences");
}
```

## Troubleshooting

### Cache Not Being Invalidated

1. **Check invalidation tags match**:
   ```csharp
   [CacheInvalidateOn("Users")]     // Tag: "Users"
   [CacheInvalidateOn("User")]      // Tag: "User" - different!
   ```

2. **Verify invalidation methods are called**:
   ```csharp
   // Make sure the invalidating action is actually executed
   [HttpPost]
   [CacheInvalidateOn("Users")]
   public async Task CreateUser(...) {
       // If this throws an exception, invalidation won't happen
       await _userService.CreateUserAsync(...);
   }
   ```

3. **Check distributed invalidation setup**:
   ```csharp
   // Ensure IDistributedCacheInvalidator is registered for Redis scenarios
   builder.Services.AddAthenaCacheRedisComplete(...);
   ```

### Invalidation Logging

Enable invalidation logging to debug issues:

```csharp
builder.Services.AddAthenaCacheComplete(options =>
{
    options.Logging.LogCacheInvalidation = true;
});
```

For more advanced scenarios, see:
- **[Redis Setup]({{ site.baseurl }}/distributed/redis-setup)** - Distributed invalidation
- **[Monitoring]({{ site.baseurl }}/monitoring/real-time-dashboards)** - Track invalidation events
- **[Performance]({{ site.baseurl }}/performance/zero-memory-optimization)** - Optimize invalidation performance