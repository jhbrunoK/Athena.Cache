using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Sample.Models;
using Athena.Invalidation.Sample.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Athena.Invalidation.Sample.Controllers;

/// <summary>
/// 캐시 워밍업 및 데이터 프리로드를 담당하는 컨트롤러
/// 실제 시나리오 테스트를 위해 캐시를 미리 채웁니다
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DataPreloadController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly IUserService _userService;
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<DataPreloadController> _logger;

    public DataPreloadController(
        IProductService productService,
        ICategoryService categoryService,
        IUserService userService,
        IInvalidationEngine invalidationEngine,
        IMemoryCache memoryCache,
        ILogger<DataPreloadController> logger)
    {
        _productService = productService;
        _categoryService = categoryService;
        _userService = userService;
        _invalidationEngine = invalidationEngine;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    /// <summary>
    /// 캐시 워밍업 - 모든 주요 데이터를 캐시에 로드
    /// </summary>
    [HttpPost("warmup")]
    public async Task<IActionResult> WarmupCache([FromQuery] bool includeAll = false)
    {
        try
        {
            _logger.LogInformation("Starting cache warmup process");
            
            var startTime = DateTime.UtcNow;
            var loadedItems = new
            {
                products = new List<object>(),
                categories = new List<object>(),
                users = new List<object>(),
                additionalCaches = new List<string>()
            };

            // 1. 모든 상품 데이터 로드
            var allProducts = await _productService.GetAllAsync();
            foreach (var product in allProducts)
            {
                // GetByIdAsync를 호출하여 캐시에 저장
                var cachedProduct = await _productService.GetByIdAsync(product.Id);
                loadedItems.products.Add(new
                {
                    id = product.Id,
                    name = product.Name,
                    cacheKey = Product.GetCacheKey(product.Id),
                    cached = true
                });
            }
            _logger.LogInformation("Loaded {Count} products into cache", allProducts.Count);

            // 2. 카테고리별 상품 목록 캐시
            var allCategories = await _categoryService.GetAllAsync();
            foreach (var category in allCategories)
            {
                // 카테고리별 상품 목록도 캐시
                var categoryProducts = await _productService.GetByCategoryAsync(category.Id);
                loadedItems.categories.Add(new
                {
                    id = category.Id,
                    name = category.Name,
                    productCount = categoryProducts.Count,
                    cacheKey = Category.GetCacheKey(category.Id),
                    cached = true
                });
            }
            _logger.LogInformation("Loaded {Count} categories into cache", allCategories.Count);

            // 3. 추가 캐시 (선택적)
            if (includeAll)
            {
                // Featured 상품 캐시
                var featuredProducts = await _productService.GetFeaturedAsync();
                loadedItems.additionalCaches.Add($"Featured products ({featuredProducts.Count} items)");

                // On sale 상품 캐시
                var onSaleProducts = await _productService.GetOnSaleAsync();
                loadedItems.additionalCaches.Add($"On sale products ({onSaleProducts.Count} items)");

                // In stock 상품 캐시
                var inStockProducts = await _productService.GetInStockAsync();
                loadedItems.additionalCaches.Add($"In stock products ({inStockProducts.Count} items)");

                // 사용자 데이터 캐시
                var allUsers = await _userService.GetAllAsync();
                foreach (var user in allUsers)
                {
                    await _userService.GetByIdAsync(user.Id);
                    loadedItems.users.Add(new
                    {
                        id = user.Id,
                        email = user.Email,
                        cacheKey = $"user:{user.Id}",
                        cached = true
                    });
                }
                _logger.LogInformation("Loaded {Count} users into cache", allUsers.Count);
            }

            var elapsedTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            return Ok(new
            {
                message = "캐시 워밍업이 완료되었습니다",
                statistics = new
                {
                    productsLoaded = loadedItems.products.Count,
                    categoriesLoaded = loadedItems.categories.Count,
                    usersLoaded = loadedItems.users.Count,
                    additionalCaches = loadedItems.additionalCaches.Count,
                    totalItems = loadedItems.products.Count + loadedItems.categories.Count + loadedItems.users.Count,
                    elapsedTimeMs = Math.Round(elapsedTime, 2)
                },
                loadedItems = loadedItems,
                includeAll = includeAll,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache warmup failed");
            return StatusCode(500, new { error = "Cache warmup failed", details = ex.Message });
        }
    }

    /// <summary>
    /// 캐시 프리로드 상태 확인
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetPreloadStatus()
    {
        try
        {
            var cachedItems = new
            {
                products = new List<object>(),
                categories = new List<object>(),
                patterns = new List<object>()
            };

            // 상품 캐시 상태 확인
            var productCount = 0;
            for (int i = 1; i <= 20; i++) // 최대 20개 상품 확인
            {
                var key = Product.GetCacheKey(i);
                if (_memoryCache.TryGetValue(key, out ProductDto? product))
                {
                    productCount++;
                    if (productCount <= 5) // 처음 5개만 상세 정보
                    {
                        cachedItems.products.Add(new
                        {
                            id = i,
                            name = product.Name,
                            price = product.Price,
                            stock = product.StockQuantity,
                            cacheKey = key
                        });
                    }
                }
            }

            // 카테고리 캐시 상태 확인
            var categoryCount = 0;
            for (int i = 1; i <= 10; i++) // 최대 10개 카테고리 확인
            {
                var key = Category.GetCacheKey(i);
                if (_memoryCache.TryGetValue(key, out CategoryDto? category))
                {
                    categoryCount++;
                    if (categoryCount <= 3) // 처음 3개만 상세 정보
                    {
                        cachedItems.categories.Add(new
                        {
                            id = i,
                            name = category.Name,
                            cacheKey = key
                        });
                    }
                }
            }

            // 추적된 캐시 키 확인
            var trackedProductKeys = await _invalidationEngine.GetTrackedKeysAsync("products");
            var trackedCategoryKeys = await _invalidationEngine.GetTrackedKeysAsync("categories");
            var trackedUserKeys = await _invalidationEngine.GetTrackedKeysAsync("users");

            // 엔진 상태
            var engineStatus = await _invalidationEngine.GetStatusAsync();

            return Ok(new
            {
                preloadStatus = new
                {
                    isPreloaded = productCount > 0 || categoryCount > 0,
                    recommendation = productCount == 0 
                        ? "캐시가 비어있습니다. POST /api/datapreload/warmup을 실행하세요" 
                        : "캐시가 프리로드되어 있습니다"
                },
                cachedCounts = new
                {
                    products = productCount,
                    categories = categoryCount,
                    totalCached = productCount + categoryCount
                },
                trackedKeys = new
                {
                    products = trackedProductKeys.Count(),
                    categories = trackedCategoryKeys.Count(),
                    users = trackedUserKeys.Count(),
                    total = trackedProductKeys.Count() + trackedCategoryKeys.Count() + trackedUserKeys.Count()
                },
                sampleCachedItems = cachedItems,
                engineMetrics = engineStatus.Metrics,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get preload status");
            return StatusCode(500, new { error = "Failed to get preload status", details = ex.Message });
        }
    }

    /// <summary>
    /// 캐시 초기화 및 재로드
    /// </summary>
    [HttpPost("reset")]
    public async Task<IActionResult> ResetAndReload([FromQuery] bool reloadAfterReset = true)
    {
        try
        {
            _logger.LogWarning("Resetting all cache");

            // 1. 모든 캐시 초기화
            await _invalidationEngine.ClearAllAsync();
            
            var resetResult = new
            {
                cacheCleared = true,
                timestamp = DateTime.UtcNow
            };

            if (!reloadAfterReset)
            {
                return Ok(new
                {
                    message = "캐시가 완전히 초기화되었습니다",
                    resetResult = resetResult,
                    reloaded = false
                });
            }

            // 2. 캐시 재로드
            _logger.LogInformation("Reloading cache after reset");
            
            var reloadStartTime = DateTime.UtcNow;
            var reloadedCount = 0;

            // 핵심 상품만 재로드 (빠른 테스트를 위해)
            var products = await _productService.GetAllAsync();
            foreach (var product in products.Take(10)) // 처음 10개만
            {
                await _productService.GetByIdAsync(product.Id);
                reloadedCount++;
            }

            // 카테고리 재로드
            var categories = await _categoryService.GetAllAsync();
            foreach (var category in categories.Take(5)) // 처음 5개만
            {
                await _categoryService.GetByIdAsync(category.Id);
                reloadedCount++;
            }

            var reloadTime = (DateTime.UtcNow - reloadStartTime).TotalMilliseconds;

            return Ok(new
            {
                message = "캐시가 초기화되고 재로드되었습니다",
                resetResult = resetResult,
                reloadResult = new
                {
                    reloaded = true,
                    itemsReloaded = reloadedCount,
                    reloadTimeMs = Math.Round(reloadTime, 2)
                },
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset and reload cache");
            return StatusCode(500, new { error = "Failed to reset cache", details = ex.Message });
        }
    }

    /// <summary>
    /// 특정 상품들만 선택적으로 캐시에 로드
    /// </summary>
    [HttpPost("preload-products")]
    public async Task<IActionResult> PreloadSpecificProducts([FromBody] PreloadProductsRequest request)
    {
        try
        {
            var loadedProducts = new List<object>();
            var failedProducts = new List<object>();

            foreach (var productId in request.ProductIds)
            {
                try
                {
                    var product = await _productService.GetByIdAsync(productId);
                    if (product != null)
                    {
                        loadedProducts.Add(new
                        {
                            id = productId,
                            name = product.Name,
                            price = product.Price,
                            cacheKey = Product.GetCacheKey(productId),
                            cached = true
                        });
                    }
                    else
                    {
                        failedProducts.Add(new
                        {
                            id = productId,
                            reason = "Product not found"
                        });
                    }
                }
                catch (Exception ex)
                {
                    failedProducts.Add(new
                    {
                        id = productId,
                        reason = ex.Message
                    });
                }
            }

            // 관련 카테고리도 캐시
            if (request.IncludeCategories)
            {
                var categoryIds = new HashSet<int>();
                foreach (var product in loadedProducts)
                {
                    var productData = await _productService.GetByIdAsync(((dynamic)product).id);
                    if (productData != null)
                    {
                        categoryIds.Add(productData.CategoryId);
                    }
                }

                foreach (var categoryId in categoryIds)
                {
                    await _productService.GetByCategoryAsync(categoryId);
                }
            }

            return Ok(new
            {
                message = $"선택한 상품들이 캐시에 로드되었습니다",
                requested = request.ProductIds.Count,
                loaded = loadedProducts.Count,
                failed = failedProducts.Count,
                loadedProducts = loadedProducts,
                failedProducts = failedProducts,
                categoriesIncluded = request.IncludeCategories,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preload specific products");
            return StatusCode(500, new { error = "Failed to preload products", details = ex.Message });
        }
    }
}

// Request DTO
public class PreloadProductsRequest
{
    public List<int> ProductIds { get; set; } = new();
    public bool IncludeCategories { get; set; } = false;
}