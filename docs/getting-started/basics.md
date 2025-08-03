---
layout: default
title: Basics
---

# Basics

Athena.Cache is designed around **attributes** that you add to your controller actions. The library handles cache key generation, storage, retrieval, and invalidation automatically.

## Core Concept

The central concept in Athena.Cache is **declarative caching** - you declare what should be cached and when it should be invalidated using attributes, and the library handles the rest.

```csharp
[HttpGet]
[AthenaCache(ExpirationMinutes = 30)]           // Cache this response
[CacheInvalidateOn("Users")]                     // Invalidate when Users table changes
public async Task<ActionResult<UserDto[]>> GetUsers()
{
    // Your business logic - no cache code needed!
    return Ok(await _userService.GetUsersAsync());
}
```

## The AthenaCache Attribute

The `[AthenaCache]` attribute marks an action for caching.

### Basic Usage

```csharp
[HttpGet("{id}")]
[AthenaCache(ExpirationMinutes = 60)]
public async Task<UserDto> GetUser(int id)
{
    return await _userService.GetUserAsync(id);
}
// Generated cache key: "UsersController:GetUser_id:123"
// Cache expires after 60 minutes
```

### Expiration Options

```csharp
// Time-based expiration
[AthenaCache(ExpirationMinutes = 30)]

// Sliding expiration (resets timer on access)
[AthenaCache(ExpirationMinutes = 15, SlidingExpiration = true)]

// Use global default (configured in Program.cs)
[AthenaCache]
```

### Cache Key Customization

```csharp
// Custom key pattern
[AthenaCache(KeyPattern = "user_{id}_{role}")]
public async Task<UserDto> GetUserByRole(int id, string role) { ... }
// Result: "user_123_admin"

// Custom cache key prefix
[AthenaCache(KeyPrefix = "api_v2")]
public async Task<DataDto> GetData() { ... }
// Result: "api_v2:DataController:GetData"
```

## Cache Invalidation

The `[CacheInvalidateOn]` attribute specifies when caches should be cleared.

### Table-based Invalidation

```csharp
[HttpPost]
[CacheInvalidateOn("Users")]  // Clear all caches tagged with "Users"
public async Task<UserDto> CreateUser([FromBody] CreateUserRequest request)
{
    var user = await _userService.CreateUserAsync(request);
    // All caches with [CacheInvalidateOn("Users")] are automatically cleared
    return user;
}
```

### Multiple Tables

```csharp
[HttpPut("{id}")]
[CacheInvalidateOn("Users")]        // Clear user-related caches
[CacheInvalidateOn("UserProfiles")] // Also clear profile-related caches
public async Task UpdateUser(int id, [FromBody] UpdateUserRequest request)
{
    await _userService.UpdateUserAsync(id, request);
}
```

### Pattern-based Invalidation

```csharp
[HttpDelete("{id}")]
[CacheInvalidateOn("Users", InvalidationType.Pattern, "user_*")]
public async Task DeleteUser(int id)
{
    await _userService.DeleteUserAsync(id);
    // Clears all cache keys matching "user_*" pattern
}
```

## Cache Status Monitoring

Athena.Cache provides cache status information through HTTP headers.

```bash
# First request (cache miss)
GET /api/users/123
# Response includes: X-Athena-Cache: MISS

# Second request (cache hit)  
GET /api/users/123
# Response includes: X-Athena-Cache: HIT
```

### Programmatic Cache Statistics

```csharp
[ApiController]
public class CacheStatsController : ControllerBase
{
    private readonly IAthenaCache _cache;

    public CacheStatsController(IAthenaCache cache)
    {
        _cache = cache;
    }

    [HttpGet("cache/stats")]
    public async Task<IActionResult> GetStats()
    {
        var stats = await _cache.GetStatisticsAsync();
        return Ok(new
        {
            HitRate = stats.HitRate,
            TotalRequests = stats.TotalRequests,
            CacheSize = stats.CacheSize
        });
    }
}
```

## Disabling Cache

### Skip Caching for Specific Actions

```csharp
[HttpGet("realtime")]
[NoCache]  // This action will never be cached
public async Task<RealtimeData> GetRealtimeData()
{
    return await _dataService.GetLiveDataAsync();
}
```

### Conditional Caching

```csharp
[HttpGet]
[AthenaCache(ExpirationMinutes = 30)]
public async Task<UserDto[]> GetUsers([FromQuery] bool skipCache = false)
{
    if (skipCache)
    {
        // Business logic to bypass cache
        Response.Headers.Add("X-Cache-Bypassed", "true");
    }
    
    return await _userService.GetUsersAsync();
}
```

## Convention-based Configuration

Athena.Cache can automatically infer table names from controller names.

```csharp
// With convention enabled (default):
public class UsersController : ControllerBase
{
    [HttpGet]
    [AthenaCache]  
    // Automatically adds [CacheInvalidateOn("Users")] based on controller name
    public async Task<UserDto[]> GetUsers() { ... }
}

// Disable convention for specific actions:
[HttpGet]
[AthenaCache]
[NoConventionInvalidation]  // Don't auto-add invalidation
public async Task<UserDto[]> GetUsersNoConvention() { ... }
```

## Error Handling

Configure how cache errors are handled:

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options =>
{
    options.ErrorHandling.OnCacheError = CacheErrorAction.LogAndContinue;
    options.ErrorHandling.ThrowOnSerializationError = false;
});
```

### Error Handling Options

- **LogAndContinue**: Log the error and continue without cache (default)
- **ThrowException**: Throw the cache error  
- **IgnoreSilently**: Ignore cache errors completely

## Cache Configuration

### Global Configuration

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(options =>
{
    // Basic settings
    options.Namespace = "MyApp";
    options.DefaultExpirationMinutes = 30;
    
    // Convention settings
    options.Convention.EnableConventionBasedInvalidation = true;
    options.Convention.ControllerSuffix = "Controller";
    
    // Logging
    options.Logging.LogCacheHitMiss = true;
    options.Logging.LogCacheInvalidation = true;
});
```

### Using appsettings.json

```json
{
  "AthenaCache": {
    "Namespace": "MyApp",
    "DefaultExpirationMinutes": 30,
    "Convention": {
      "EnableConventionBasedInvalidation": true
    },
    "Logging": {
      "LogCacheHitMiss": true,
      "LogCacheInvalidation": true
    }
  }
}
```

```csharp
// Program.cs
builder.Services.AddAthenaCacheComplete(
    builder.Configuration.GetSection("AthenaCache"));
```

## Complete Example

Here's a complete controller showing common patterns:

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    // Cached list with automatic invalidation
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    [CacheInvalidateOn("Users")]
    public async Task<ActionResult<UserDto[]>> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1)
    {
        return Ok(await _userService.GetUsersAsync(search, page));
    }
    
    // Cached individual user
    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 60)]
    [CacheInvalidateOn("Users")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var user = await _userService.GetUserAsync(id);
        return user == null ? NotFound() : Ok(user);
    }
    
    // Cache invalidation on create
    [HttpPost]
    [CacheInvalidateOn("Users")]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserRequest request)
    {
        var user = await _userService.CreateUserAsync(request);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }
    
    // Cache invalidation on update
    [HttpPut("{id}")]
    [CacheInvalidateOn("Users")]
    [CacheInvalidateOn("UserProfiles")]  // Also invalidate related data
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
    {
        await _userService.UpdateUserAsync(id, request);
        return NoContent();
    }
    
    // Cache invalidation on delete
    [HttpDelete("{id}")]
    [CacheInvalidateOn("Users")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _userService.DeleteUserAsync(id);
        return NoContent();
    }
    
    // Real-time data without caching
    [HttpGet("online")]
    [NoCache]
    public async Task<ActionResult<int>> GetOnlineUserCount()
    {
        return Ok(await _userService.GetOnlineUserCountAsync());
    }
}
```

## Next Steps

Now that you understand the basics, explore specific features:

- **[Cache Key Generation]({{ site.baseurl }}/fundamentals/cache-key-generation)** - How cache keys are created and customized
- **[Cache Invalidation]({{ site.baseurl }}/fundamentals/cache-invalidation)** - Advanced invalidation strategies  
- **[Redis Setup]({{ site.baseurl }}/distributed/redis-setup)** - Configure distributed caching
- **[Performance Optimization]({{ site.baseurl }}/performance/zero-memory-optimization)** - Advanced performance techniques