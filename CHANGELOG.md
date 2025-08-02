# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.7] - 2024-08-02

### Added
- **Convention-based table name inference system**: Automatically infer table names from controller names (`UsersController` → `Users`)
- **NoConventionInvalidationAttribute**: Disable convention-based inference for specific controllers/actions
- **Multi-table mapping support**: Configure multiple tables for a single controller via `ControllerTableMappings`
- **Custom table inferrer**: Support for custom multi-table inference functions
- **Global exclusion settings**: Exclude specific controllers from convention inference via `ExcludedControllers`
- **ReportsController sample**: Demonstrates manual cache control patterns with `NoConventionInvalidation`

### Changed
- **Reduced boilerplate**: No need to declare `CacheInvalidateOn` for basic table mappings
- **Improved consistency**: Automatic consistency between R (Read) and CUD (Create/Update/Delete) operations
- **Enhanced AthenaCacheActionFilter**: Intelligent merging of explicit declarations and convention-based inference
- **Simplified controller code**: Basic table invalidation now handled automatically by convention

### Fixed
- **Cache invalidation mismatch**: Eliminated possibility of R/CUD invalidation rule inconsistencies
- **Duplicate declarations**: Convention system prevents redundant `CacheInvalidateOn` attributes

### Migration Guide

#### Full Backward Compatibility
- All existing v1.1.6 code continues to work without changes
- Explicit `CacheInvalidateOn` declarations take precedence over convention inference
- No breaking changes introduced

#### Optional Optimizations
```csharp
// Before v1.1.7 (still works)
[HttpGet]
[AthenaCache]
[CacheInvalidateOn("Users")]
public async Task<IActionResult> GetUsers() { ... }

[HttpPost]
[CacheInvalidateOn("Users")]  
public async Task<IActionResult> CreateUser() { ... }

// After v1.1.7 (simplified)
[HttpGet]
[AthenaCache]
public async Task<IActionResult> GetUsers() { ... }  // Users table auto-inferred

[HttpPost]
public async Task<IActionResult> CreateUser() { ... }  // Users table auto-invalidated
```

#### Configuration Options
```csharp
// Enable convention-based inference (default: true)
builder.Services.AddAthenaCacheComplete(options => {
    options.Convention.Enabled = true;
    options.Convention.UsePluralizer = true;
    
    // Multi-table mapping
    options.Convention.ControllerTableMappings["UsersController"] = ["Users", "UserProfiles"];
    
    // Global exclusions
    options.Convention.ExcludedControllers.Add("ReportsController");
});
```

#### Manual Cache Control
```csharp
[NoConventionInvalidation]  // Disable auto-inference
public class ReportsController : ControllerBase
{
    [HttpGet]
    [AthenaCache]  // Caching only, no auto-invalidation
    
    [HttpPost]
    public async Task<IActionResult> Refresh()
    {
        // Manual cache invalidation
        await _cacheInvalidator.InvalidateByPatternAsync("*Report*");
    }
}
```

### Priority System
1. **Explicit CacheInvalidateOn** (highest priority)
2. **NoConventionInvalidation check** (blocks convention)
3. **Global ExcludedControllers** (blocks convention)  
4. **Convention inference** (Settings mapping > Custom function > Default inference)

### Performance Impact
- No performance impact on existing functionality
- Convention inference adds minimal overhead during application startup
- Runtime performance unchanged