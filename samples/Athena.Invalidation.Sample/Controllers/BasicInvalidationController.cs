using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Sample.Models;
using Athena.Invalidation.Sample.Services;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Invalidation.Sample.Controllers;

/// <summary>
/// 기본적인 무효화 기능을 시연하는 컨트롤러
/// 테이블 기반, 패턴 기반, 키 기반 무효화를 제공합니다.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BasicInvalidationController : ControllerBase
{
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly IUserService _userService;
    private readonly ILogger<BasicInvalidationController> _logger;

    public BasicInvalidationController(
        IInvalidationEngine invalidationEngine,
        IProductService productService,
        ICategoryService categoryService,
        IUserService userService,
        ILogger<BasicInvalidationController> logger)
    {
        _invalidationEngine = invalidationEngine;
        _productService = productService;
        _categoryService = categoryService;
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// 테이블 기반 무효화 - 특정 테이블과 연관된 모든 캐시를 제거합니다
    /// </summary>
    /// <param name="tableName">무효화할 테이블 이름 (products, categories, users, orders)</param>
    [HttpPost("invalidate/table/{tableName}")]
    public async Task<IActionResult> InvalidateByTable(string tableName)
    {
        try
        {
            var validTables = new[] { "products", "categories", "users", "orders", "order_items" };
            if (!validTables.Contains(tableName.ToLowerInvariant()))
            {
                return BadRequest(new { 
                    error = "Invalid table name", 
                    validTables = validTables 
                });
            }

            _logger.LogInformation("Invalidating cache for table: {TableName}", tableName);
            
            await _invalidationEngine.InvalidateByTableAsync(tableName);
            
            return Ok(new { 
                message = $"Successfully invalidated all cache entries for table '{tableName}'",
                tableName = tableName,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache for table {TableName}", tableName);
            return StatusCode(500, new { error = "Failed to invalidate cache", details = ex.Message });
        }
    }

    /// <summary>
    /// 패턴 기반 무효화 - 패턴에 맞는 캐시 키들을 제거합니다
    /// </summary>
    /// <param name="pattern">캐시 키 패턴 (예: product:*, user:email:*, products:category:1:*)</param>
    [HttpPost("invalidate/pattern")]
    public async Task<IActionResult> InvalidateByPattern([FromBody] InvalidatePatternRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Pattern))
            {
                return BadRequest(new { error = "Pattern is required" });
            }

            _logger.LogInformation("Invalidating cache by pattern: {Pattern}", request.Pattern);
            
            await _invalidationEngine.InvalidateByPatternAsync(request.Pattern);
            
            return Ok(new { 
                message = $"Successfully invalidated cache entries matching pattern '{request.Pattern}'",
                pattern = request.Pattern,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache by pattern {Pattern}", request.Pattern);
            return StatusCode(500, new { error = "Failed to invalidate cache", details = ex.Message });
        }
    }

    /// <summary>
    /// 키 기반 무효화 - 정확한 캐시 키를 제거합니다
    /// </summary>
    /// <param name="key">제거할 캐시 키</param>
    [HttpPost("invalidate/key")]
    public async Task<IActionResult> InvalidateByKey([FromBody] InvalidateKeyRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Key))
            {
                return BadRequest(new { error = "Key is required" });
            }

            _logger.LogInformation("Invalidating cache by key: {Key}", request.Key);
            
            await _invalidationEngine.InvalidateByKeyAsync(request.Key);
            
            return Ok(new { 
                message = $"Successfully invalidated cache entry with key '{request.Key}'",
                key = request.Key,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache by key {Key}", request.Key);
            return StatusCode(500, new { error = "Failed to invalidate cache", details = ex.Message });
        }
    }

    /// <summary>
    /// 전체 캐시 초기화 (주의해서 사용하세요!)
    /// </summary>
    [HttpPost("invalidate/all")]
    public async Task<IActionResult> InvalidateAll()
    {
        try
        {
            _logger.LogWarning("Clearing all cache entries");
            
            await _invalidationEngine.ClearAllAsync();
            
            return Ok(new { 
                message = "Successfully cleared all cache entries",
                timestamp = DateTime.UtcNow,
                warning = "All cache entries have been removed. Performance may be affected until cache is rebuilt."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear all cache");
            return StatusCode(500, new { error = "Failed to clear cache", details = ex.Message });
        }
    }

    /// <summary>
    /// 데모: 상품 데이터를 가져온 후 관련 캐시를 무효화하는 시나리오
    /// </summary>
    [HttpPost("demo/product-cache-scenario")]
    public async Task<IActionResult> ProductCacheScenario([FromBody] ProductCacheScenarioRequest request)
    {
        try
        {
            var results = new List<object>();
            
            // 1. 상품 데이터 조회 (캐시됨)
            _logger.LogInformation("Step 1: Fetching product {ProductId}", request.ProductId);
            var product = await _productService.GetByIdAsync(request.ProductId);
            if (product == null)
            {
                return NotFound(new { error = $"Product {request.ProductId} not found" });
            }
            
            results.Add(new { 
                step = 1, 
                action = "fetch_product", 
                productId = request.ProductId, 
                productName = product.Name,
                cached = "Data is now cached"
            });

            // 2. 같은 상품 다시 조회 (캐시 히트)
            _logger.LogInformation("Step 2: Fetching same product again (should hit cache)");
            var productAgain = await _productService.GetByIdAsync(request.ProductId);
            results.Add(new { 
                step = 2, 
                action = "fetch_product_again", 
                result = "Cache hit - data retrieved from cache"
            });

            // 3. 카테고리별 상품 목록 조회 (캐시됨)
            _logger.LogInformation("Step 3: Fetching products by category {CategoryId}", product.CategoryId);
            var categoryProducts = await _productService.GetByCategoryAsync(product.CategoryId);
            results.Add(new { 
                step = 3, 
                action = "fetch_category_products", 
                categoryId = product.CategoryId,
                productCount = categoryProducts.Count,
                cached = "Category products list cached"
            });

            // 4. 선택적 무효화 실행
            if (request.InvalidateType?.ToLowerInvariant() == "key")
            {
                _logger.LogInformation("Step 4: Invalidating specific product key");
                await _invalidationEngine.InvalidateByKeyAsync(Product.GetCacheKey(request.ProductId));
                results.Add(new { 
                    step = 4, 
                    action = "invalidate_by_key", 
                    key = Product.GetCacheKey(request.ProductId),
                    result = "Specific product cache invalidated"
                });
            }
            else if (request.InvalidateType?.ToLowerInvariant() == "pattern")
            {
                _logger.LogInformation("Step 4: Invalidating by pattern");
                var pattern = $"products:category:{product.CategoryId}:*";
                await _invalidationEngine.InvalidateByPatternAsync(pattern);
                results.Add(new { 
                    step = 4, 
                    action = "invalidate_by_pattern", 
                    pattern = pattern,
                    result = "Category-related caches invalidated"
                });
            }
            else if (request.InvalidateType?.ToLowerInvariant() == "table")
            {
                _logger.LogInformation("Step 4: Invalidating entire products table cache");
                await _invalidationEngine.InvalidateByTableAsync(Product.GetTableName());
                results.Add(new { 
                    step = 4, 
                    action = "invalidate_by_table", 
                    table = Product.GetTableName(),
                    result = "All product-related caches invalidated"
                });
            }
            else
            {
                results.Add(new { 
                    step = 4, 
                    action = "no_invalidation", 
                    result = "No invalidation performed - cache remains intact"
                });
            }

            // 5. 데이터 재조회 (캐시 미스 또는 히트)
            _logger.LogInformation("Step 5: Fetching product again after invalidation");
            var finalProduct = await _productService.GetByIdAsync(request.ProductId);
            results.Add(new { 
                step = 5, 
                action = "fetch_after_invalidation", 
                result = request.InvalidateType != null ? "Cache miss - data fetched from database" : "Cache hit - data from cache"
            });

            return Ok(new {
                scenario = "Product Cache Demonstration",
                productId = request.ProductId,
                invalidationType = request.InvalidateType ?? "none",
                steps = results,
                summary = new {
                    message = "Demonstrated basic cache operations and invalidation strategies",
                    recommendation = "Use key-based invalidation for specific items, pattern-based for related groups, table-based for bulk changes"
                },
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute product cache scenario");
            return StatusCode(500, new { error = "Scenario execution failed", details = ex.Message });
        }
    }

    /// <summary>
    /// 추적된 캐시 키 조회 - 특정 테이블과 연결된 캐시 키들을 확인
    /// </summary>
    [HttpGet("tracked-keys/{tableName}")]
    public async Task<IActionResult> GetTrackedKeys(string tableName)
    {
        try
        {
            _logger.LogInformation("Getting tracked keys for table: {TableName}", tableName);
            
            var keys = await _invalidationEngine.GetTrackedKeysAsync(tableName);
            
            return Ok(new {
                tableName = tableName,
                trackedKeys = keys.ToList(),
                keyCount = keys.Count(),
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get tracked keys for table {TableName}", tableName);
            return StatusCode(500, new { error = "Failed to get tracked keys", details = ex.Message });
        }
    }

    /// <summary>
    /// 무효화 엔진 상태 확인
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetEngineStatus()
    {
        try
        {
            var status = await _invalidationEngine.GetStatusAsync();
            
            return Ok(new {
                engineStatus = status,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get engine status");
            return StatusCode(500, new { error = "Failed to get status", details = ex.Message });
        }
    }
}

public class InvalidatePatternRequest
{
    public string Pattern { get; set; } = string.Empty;
}

public class InvalidateKeyRequest
{
    public string Key { get; set; } = string.Empty;
}

public class ProductCacheScenarioRequest
{
    public int ProductId { get; set; }
    public string? InvalidateType { get; set; } // "key", "pattern", "table", or null
}