using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Sample.Models;
using Athena.Invalidation.Sample.Services;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Invalidation.Sample.Controllers;

/// <summary>
/// 배치 무효화 기능을 시연하는 컨트롤러
/// 여러 테이블을 한번에 무효화하거나 대량의 캐시 작업을 처리합니다.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BatchInvalidationController : ControllerBase
{
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly IUserService _userService;
    private readonly ILogger<BatchInvalidationController> _logger;

    public BatchInvalidationController(
        IInvalidationEngine invalidationEngine,
        IProductService productService,
        ICategoryService categoryService,
        IUserService userService,
        ILogger<BatchInvalidationController> logger)
    {
        _invalidationEngine = invalidationEngine;
        _productService = productService;
        _categoryService = categoryService;
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// 배치 테이블 무효화 - 여러 테이블을 한번에 무효화합니다
    /// </summary>
    /// <param name="request">무효화할 테이블 목록</param>
    [HttpPost("invalidate/batch-tables")]
    public async Task<IActionResult> InvalidateBatchTables([FromBody] BatchTablesRequest request)
    {
        try
        {
            if (request.TableNames == null || !request.TableNames.Any())
            {
                return BadRequest(new { error = "TableNames array is required and must not be empty" });
            }

            var validTables = new[] { "products", "categories", "users", "orders", "order_items" };
            var invalidTables = request.TableNames.Where(t => !validTables.Contains(t.ToLowerInvariant())).ToList();
            
            if (invalidTables.Any())
            {
                return BadRequest(new { 
                    error = "Invalid table names found", 
                    invalidTables = invalidTables,
                    validTables = validTables 
                });
            }

            _logger.LogInformation("Batch invalidating tables: {TableNames}", string.Join(", ", request.TableNames));
            
            var startTime = DateTime.UtcNow;
            await _invalidationEngine.InvalidateBatchAsync(request.TableNames);
            var endTime = DateTime.UtcNow;
            
            return Ok(new { 
                message = "Successfully invalidated multiple tables",
                tableNames = request.TableNames,
                tableCount = request.TableNames.Count(),
                duration = (endTime - startTime).TotalMilliseconds,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch invalidate tables {TableNames}", string.Join(", ", request.TableNames ?? new string[0]));
            return StatusCode(500, new { error = "Failed to batch invalidate", details = ex.Message });
        }
    }

    /// <summary>
    /// 배치 패턴 무효화 - 여러 패턴을 한번에 무효화합니다
    /// </summary>
    [HttpPost("invalidate/batch-patterns")]
    public async Task<IActionResult> InvalidateBatchPatterns([FromBody] BatchPatternsRequest request)
    {
        try
        {
            if (request.Patterns == null || !request.Patterns.Any())
            {
                return BadRequest(new { error = "Patterns array is required and must not be empty" });
            }

            _logger.LogInformation("Batch invalidating patterns: {Patterns}", string.Join(", ", request.Patterns));
            
            var results = new List<object>();
            var startTime = DateTime.UtcNow;
            
            foreach (var pattern in request.Patterns)
            {
                try
                {
                    var patternStartTime = DateTime.UtcNow;
                    await _invalidationEngine.InvalidateByPatternAsync(pattern);
                    var patternEndTime = DateTime.UtcNow;
                    
                    results.Add(new {
                        pattern = pattern,
                        success = true,
                        duration = (patternEndTime - patternStartTime).TotalMilliseconds
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to invalidate pattern {Pattern}", pattern);
                    results.Add(new {
                        pattern = pattern,
                        success = false,
                        error = ex.Message
                    });
                }
            }
            
            var endTime = DateTime.UtcNow;
            var successCount = results.Count(r => 
            {
                var prop = r.GetType().GetProperty("success");
                return prop?.GetValue(r) as bool? ?? false;
            });
            
            return Ok(new { 
                message = "Batch pattern invalidation completed",
                totalPatterns = request.Patterns.Count(),
                successfulPatterns = successCount,
                failedPatterns = request.Patterns.Count() - successCount,
                results = results,
                totalDuration = (endTime - startTime).TotalMilliseconds,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch invalidate patterns");
            return StatusCode(500, new { error = "Failed to batch invalidate patterns", details = ex.Message });
        }
    }

    /// <summary>
    /// 배치 키 무효화 - 여러 캐시 키를 한번에 무효화합니다
    /// </summary>
    [HttpPost("invalidate/batch-keys")]
    public async Task<IActionResult> InvalidateBatchKeys([FromBody] BatchKeysRequest request)
    {
        try
        {
            if (request.Keys == null || !request.Keys.Any())
            {
                return BadRequest(new { error = "Keys array is required and must not be empty" });
            }

            _logger.LogInformation("Batch invalidating {KeyCount} keys", request.Keys.Count());
            
            var results = new List<object>();
            var startTime = DateTime.UtcNow;
            
            foreach (var key in request.Keys)
            {
                try
                {
                    var keyStartTime = DateTime.UtcNow;
                    await _invalidationEngine.InvalidateByKeyAsync(key);
                    var keyEndTime = DateTime.UtcNow;
                    
                    results.Add(new {
                        key = key,
                        success = true,
                        duration = (keyEndTime - keyStartTime).TotalMilliseconds
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to invalidate key {Key}", key);
                    results.Add(new {
                        key = key,
                        success = false,
                        error = ex.Message
                    });
                }
            }
            
            var endTime = DateTime.UtcNow;
            var successCount = results.Count(r => 
            {
                var prop = r.GetType().GetProperty("success");
                return prop?.GetValue(r) as bool? ?? false;
            });
            
            return Ok(new { 
                message = "Batch key invalidation completed",
                totalKeys = request.Keys.Count(),
                successfulKeys = successCount,
                failedKeys = request.Keys.Count() - successCount,
                results = results,
                totalDuration = (endTime - startTime).TotalMilliseconds,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch invalidate keys");
            return StatusCode(500, new { error = "Failed to batch invalidate keys", details = ex.Message });
        }
    }

    /// <summary>
    /// 데모: 대량 데이터 업데이트 시나리오
    /// 여러 상품의 가격을 업데이트하고 관련 캐시를 배치로 무효화
    /// </summary>
    [HttpPost("demo/bulk-price-update")]
    public async Task<IActionResult> BulkPriceUpdateScenario([FromBody] BulkPriceUpdateRequest request)
    {
        try
        {
            if (request.ProductUpdates == null || !request.ProductUpdates.Any())
            {
                return BadRequest(new { error = "ProductUpdates array is required" });
            }

            var results = new List<object>();
            var updatedProducts = new List<int>();
            var affectedCategories = new HashSet<int>();
            
            _logger.LogInformation("Starting bulk price update for {ProductCount} products", request.ProductUpdates.Count());
            
            // 1. 각 상품 정보 조회 및 캐시 (배치 조회 시뮬레이션)
            foreach (var update in request.ProductUpdates)
            {
                var product = await _productService.GetByIdAsync(update.ProductId);
                if (product != null)
                {
                    affectedCategories.Add(product.CategoryId);
                    results.Add(new {
                        step = "cache_product",
                        productId = update.ProductId,
                        productName = product.Name,
                        currentPrice = product.Price,
                        newPrice = update.NewPrice,
                        cached = true
                    });
                }
            }

            // 2. 가격 업데이트 (실제 업데이트는 시뮬레이션)
            foreach (var update in request.ProductUpdates)
            {
                try
                {
                    var success = await _productService.UpdatePriceAsync(update.ProductId, update.NewPrice, update.NewSalePrice);
                    if (success)
                    {
                        updatedProducts.Add(update.ProductId);
                        results.Add(new {
                            step = "update_price",
                            productId = update.ProductId,
                            success = true,
                            newPrice = update.NewPrice,
                            newSalePrice = update.NewSalePrice
                        });
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new {
                        step = "update_price",
                        productId = update.ProductId,
                        success = false,
                        error = ex.Message
                    });
                }
            }

            // 3. 배치 무효화 전략 실행
            var invalidationStrategy = request.InvalidationStrategy?.ToLowerInvariant() ?? "smart";
            
            switch (invalidationStrategy)
            {
                case "individual":
                    // 개별 상품 키만 무효화
                    var productKeys = updatedProducts.Select(id => Product.GetCacheKey(id)).ToList();
                    await InvalidateKeysInternal(productKeys);
                    results.Add(new {
                        step = "invalidation",
                        strategy = "individual",
                        keysInvalidated = productKeys.Count,
                        keys = productKeys
                    });
                    break;
                    
                case "pattern":
                    // 패턴 기반 무효화 (상품 관련)
                    var patterns = new[] { "product:*", "products:*" };
                    foreach (var pattern in patterns)
                    {
                        await _invalidationEngine.InvalidateByPatternAsync(pattern);
                    }
                    results.Add(new {
                        step = "invalidation",
                        strategy = "pattern",
                        patterns = patterns
                    });
                    break;
                    
                case "table":
                    // 테이블 전체 무효화
                    await _invalidationEngine.InvalidateByTableAsync(Product.GetTableName());
                    results.Add(new {
                        step = "invalidation",
                        strategy = "table",
                        table = Product.GetTableName()
                    });
                    break;
                    
                case "smart":
                default:
                    // 스마트 전략: 개별 키 + 관련 목록 캐시
                    var smartKeys = new List<string>();
                    
                    // 개별 상품 키들
                    smartKeys.AddRange(updatedProducts.Select(id => Product.GetCacheKey(id)));
                    
                    // 가격 관련 목록 캐시
                    smartKeys.Add("products:on-sale");
                    smartKeys.Add("products:featured");
                    
                    // 영향받은 카테고리 캐시
                    foreach (var categoryId in affectedCategories)
                    {
                        smartKeys.Add($"products:category:{categoryId}");
                    }
                    
                    await InvalidateKeysInternal(smartKeys);
                    results.Add(new {
                        step = "invalidation",
                        strategy = "smart",
                        keysInvalidated = smartKeys.Count,
                        individualProducts = updatedProducts.Count,
                        affectedCategories = affectedCategories.Count,
                        keys = smartKeys
                    });
                    break;
            }

            // 4. 성능 메트릭 계산
            var performanceMetrics = new {
                totalProductsRequested = request.ProductUpdates.Count(),
                successfulUpdates = updatedProducts.Count,
                affectedCategoriesCount = affectedCategories.Count,
                invalidationStrategy = invalidationStrategy,
                estimatedCacheImpact = invalidationStrategy switch
                {
                    "individual" => "Low - only specific product caches cleared",
                    "pattern" => "Medium - all product-related caches cleared",
                    "table" => "High - entire product table cache cleared",
                    "smart" => "Optimal - targeted invalidation of affected caches",
                    _ => "Unknown"
                }
            };

            return Ok(new {
                scenario = "Bulk Price Update Demonstration",
                summary = new {
                    message = "Demonstrated batch operations with strategic cache invalidation",
                    recommendation = "Use 'smart' strategy for optimal balance of consistency and performance"
                },
                metrics = performanceMetrics,
                results = results,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute bulk price update scenario");
            return StatusCode(500, new { error = "Scenario execution failed", details = ex.Message });
        }
    }

    /// <summary>
    /// 데모: 카테고리 재구성 시나리오
    /// 카테고리 구조 변경시 계층적 캐시 무효화
    /// </summary>
    [HttpPost("demo/category-restructure")]
    public async Task<IActionResult> CategoryRestructureScenario([FromBody] CategoryRestructureRequest request)
    {
        try
        {
            var results = new List<object>();
            
            _logger.LogInformation("Starting category restructure scenario");
            
            // 1. 현재 카테고리 구조 캐시
            var allCategories = await _categoryService.GetAllAsync();
            var rootCategories = await _categoryService.GetRootCategoriesAsync();
            results.Add(new {
                step = 1,
                action = "cache_current_structure",
                totalCategories = allCategories.Count,
                rootCategories = rootCategories.Count,
                cached = "Category hierarchy cached"
            });

            // 2. 영향받는 카테고리들의 상품 캐시
            var affectedProducts = new List<int>();
            foreach (var categoryId in request.AffectedCategoryIds ?? new List<int>())
            {
                var products = await _productService.GetByCategoryAsync(categoryId);
                affectedProducts.AddRange(products.Select(p => p.Id));
                results.Add(new {
                    step = 2,
                    action = "cache_category_products",
                    categoryId = categoryId,
                    productCount = products.Count,
                    cached = $"Products for category {categoryId} cached"
                });
            }

            // 3. 배치 무효화 실행
            var tablesToInvalidate = new List<string> { "categories", "products" };
            await _invalidationEngine.InvalidateBatchAsync(tablesToInvalidate);
            
            results.Add(new {
                step = 3,
                action = "batch_table_invalidation",
                tables = tablesToInvalidate,
                result = "Category and product caches invalidated"
            });

            // 4. 패턴 기반 무효화로 관련 캐시 정리
            var patternsToInvalidate = new[] {
                "categories:*",
                "products:category:*",
                "category:*"
            };
            
            foreach (var pattern in patternsToInvalidate)
            {
                await _invalidationEngine.InvalidateByPatternAsync(pattern);
            }
            
            results.Add(new {
                step = 4,
                action = "pattern_based_cleanup",
                patterns = patternsToInvalidate,
                result = "Related caches cleaned up"
            });

            // 5. 캐시 재구축 (시뮬레이션)
            var newRootCategories = await _categoryService.GetRootCategoriesAsync();
            results.Add(new {
                step = 5,
                action = "rebuild_cache",
                newStructure = "Cache rebuilt with new category structure",
                rootCategories = newRootCategories.Count
            });

            return Ok(new {
                scenario = "Category Restructure Demonstration",
                affectedCategories = request.AffectedCategoryIds?.Count() ?? 0,
                affectedProducts = affectedProducts.Count,
                invalidationSteps = results,
                recommendations = new {
                    message = "For large structural changes, use batch operations combined with pattern-based cleanup",
                    strategy = "1. Batch invalidate main tables, 2. Pattern cleanup for related caches, 3. Selective rebuild"
                },
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute category restructure scenario");
            return StatusCode(500, new { error = "Scenario execution failed", details = ex.Message });
        }
    }

    private async Task InvalidateKeysInternal(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            await _invalidationEngine.InvalidateByKeyAsync(key);
        }
    }
}

public class BatchTablesRequest
{
    public IEnumerable<string> TableNames { get; set; } = new List<string>();
}

public class BatchPatternsRequest
{
    public IEnumerable<string> Patterns { get; set; } = new List<string>();
}

public class BatchKeysRequest
{
    public IEnumerable<string> Keys { get; set; } = new List<string>();
}

public class BulkPriceUpdateRequest
{
    public IEnumerable<ProductPriceUpdate> ProductUpdates { get; set; } = new List<ProductPriceUpdate>();
    public string? InvalidationStrategy { get; set; } // "individual", "pattern", "table", "smart"
}

public class ProductPriceUpdate
{
    public int ProductId { get; set; }
    public decimal NewPrice { get; set; }
    public decimal? NewSalePrice { get; set; }
}

public class CategoryRestructureRequest
{
    public IEnumerable<int>? AffectedCategoryIds { get; set; }
}