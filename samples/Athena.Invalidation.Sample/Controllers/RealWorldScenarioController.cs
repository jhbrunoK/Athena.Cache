using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Sample.Models;
using Athena.Invalidation.Sample.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Athena.Invalidation.Sample.Controllers;

/// <summary>
/// 실제 비즈니스 시나리오를 시연하는 컨트롤러
/// 데이터 변경과 캐시 무효화가 실제로 어떻게 동작하는지 보여줍니다
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class RealWorldScenarioController : ControllerBase
{
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<RealWorldScenarioController> _logger;

    public RealWorldScenarioController(
        IInvalidationEngine invalidationEngine,
        IProductService productService,
        ICategoryService categoryService,
        IMemoryCache memoryCache,
        ILogger<RealWorldScenarioController> logger)
    {
        _invalidationEngine = invalidationEngine;
        _productService = productService;
        _categoryService = categoryService;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    /// <summary>
    /// 상품 조회 - 캐시 상태 정보와 함께 반환
    /// </summary>
    [HttpGet("products/{id}")]
    public async Task<IActionResult> GetProduct(int id)
    {
        try
        {
            var cacheKey = Product.GetCacheKey(id);
            var wasCached = _memoryCache.TryGetValue(cacheKey, out _);
            
            // 상품 조회 (캐시 또는 DB에서)
            var startTime = DateTime.UtcNow;
            var product = await _productService.GetByIdAsync(id);
            var fetchTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            
            if (product == null)
            {
                return NotFound(new { error = $"Product {id} not found" });
            }

            // 캐시 후 상태 확인
            var isCachedNow = _memoryCache.TryGetValue(cacheKey, out _);
            
            return Ok(new
            {
                product = product,
                cacheInfo = new
                {
                    cacheKey = cacheKey,
                    wasCached = wasCached,
                    isCachedNow = isCachedNow,
                    cacheHit = wasCached,
                    fetchTimeMs = Math.Round(fetchTime, 2),
                    dataSource = wasCached ? "Cache" : "Database"
                },
                message = wasCached 
                    ? "데이터가 캐시에서 빠르게 조회되었습니다" 
                    : "데이터가 데이터베이스에서 조회되고 캐시에 저장되었습니다",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get product {ProductId}", id);
            return StatusCode(500, new { error = "Failed to get product", details = ex.Message });
        }
    }

    /// <summary>
    /// 상품 가격 변경 - 실제 데이터 수정 후 자동 캐시 무효화
    /// </summary>
    [HttpPut("products/{id}/price")]
    public async Task<IActionResult> UpdateProductPrice(int id, [FromBody] UpdatePriceRequest request)
    {
        try
        {
            var cacheKey = Product.GetCacheKey(id);
            
            // 변경 전 캐시 상태
            var wasCached = _memoryCache.TryGetValue(cacheKey, out ProductDto? cachedProduct);
            decimal? oldPrice = cachedProduct?.Price;
            decimal? oldSalePrice = cachedProduct?.SalePrice;

            _logger.LogInformation("Updating price for product {ProductId}: {OldPrice} -> {NewPrice}", 
                id, oldPrice, request.NewPrice);

            // 실제 가격 업데이트
            var success = await _productService.UpdatePriceAsync(id, request.NewPrice, request.NewSalePrice);
            
            if (!success)
            {
                return NotFound(new { error = $"Product {id} not found or update failed" });
            }

            // 캐시 무효화 (가격 변경 이벤트)
            _logger.LogInformation("Invalidating cache for product {ProductId} after price update", id);
            await _invalidationEngine.InvalidateByKeyAsync(cacheKey);
            
            // 패턴 기반 무효화 (카테고리 관련 캐시도 무효화)
            if (cachedProduct != null)
            {
                var categoryPattern = Product.GetByCategoryPattern(cachedProduct.CategoryId);
                await _invalidationEngine.InvalidateByPatternAsync(categoryPattern);
            }

            // 변경 후 데이터 조회 (캐시 재구성)
            var updatedProduct = await _productService.GetByIdAsync(id);

            return Ok(new
            {
                message = "상품 가격이 성공적으로 변경되고 관련 캐시가 무효화되었습니다",
                productId = id,
                priceChange = new
                {
                    oldPrice = oldPrice,
                    newPrice = request.NewPrice,
                    oldSalePrice = oldSalePrice,
                    newSalePrice = request.NewSalePrice,
                    priceChanged = oldPrice != request.NewPrice,
                    salePriceChanged = oldSalePrice != request.NewSalePrice
                },
                cacheInvalidation = new
                {
                    invalidatedKeys = new[] { cacheKey },
                    invalidatedPatterns = cachedProduct != null 
                        ? new[] { Product.GetByCategoryPattern(cachedProduct.CategoryId) }
                        : Array.Empty<string>(),
                    wasCached = wasCached,
                    cacheCleared = true,
                    cacheRebuilt = true
                },
                updatedProduct = updatedProduct,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update product price for {ProductId}", id);
            return StatusCode(500, new { error = "Failed to update product price", details = ex.Message });
        }
    }

    /// <summary>
    /// 주문 생성 - 재고 감소 및 관련 캐시 무효화
    /// </summary>
    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        try
        {
            var invalidatedKeys = new List<string>();
            var stockUpdates = new List<object>();

            _logger.LogInformation("Creating order with {ItemCount} items", request.Items.Count);

            // 각 주문 항목에 대해 재고 업데이트
            foreach (var item in request.Items)
            {
                var product = await _productService.GetByIdAsync(item.ProductId);
                if (product == null)
                {
                    return BadRequest(new { error = $"Product {item.ProductId} not found" });
                }

                var oldStock = product.StockQuantity;
                var newStock = oldStock - item.Quantity;
                
                if (newStock < 0)
                {
                    return BadRequest(new { error = $"Insufficient stock for product {item.ProductId}" });
                }

                // 재고 업데이트
                await _productService.UpdateStockAsync(item.ProductId, newStock);
                
                // 캐시 무효화
                var cacheKey = Product.GetCacheKey(item.ProductId);
                await _invalidationEngine.InvalidateByKeyAsync(cacheKey);
                invalidatedKeys.Add(cacheKey);

                stockUpdates.Add(new
                {
                    productId = item.ProductId,
                    productName = product.Name,
                    oldStock = oldStock,
                    orderQuantity = item.Quantity,
                    newStock = newStock,
                    cacheKeyInvalidated = cacheKey
                });
            }

            // 카테고리별 캐시도 무효화 (재고 변경으로 인한)
            var affectedCategories = new HashSet<int>();
            foreach (var item in request.Items)
            {
                var product = await _productService.GetByIdAsync(item.ProductId);
                if (product != null)
                {
                    affectedCategories.Add(product.CategoryId);
                }
            }

            foreach (var categoryId in affectedCategories)
            {
                var pattern = Product.GetByCategoryPattern(categoryId);
                await _invalidationEngine.InvalidateByPatternAsync(pattern);
                invalidatedKeys.Add($"pattern:{pattern}");
            }

            return Ok(new
            {
                message = "주문이 성공적으로 생성되고 재고가 업데이트되었습니다",
                orderId = Guid.NewGuid().ToString(),
                orderSummary = new
                {
                    customerName = request.CustomerName,
                    itemCount = request.Items.Count,
                    totalQuantity = request.Items.Sum(i => i.Quantity)
                },
                stockUpdates = stockUpdates,
                cacheInvalidation = new
                {
                    invalidatedKeys = invalidatedKeys,
                    affectedCategories = affectedCategories.Count,
                    totalInvalidations = invalidatedKeys.Count
                },
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create order");
            return StatusCode(500, new { error = "Failed to create order", details = ex.Message });
        }
    }

    /// <summary>
    /// 현재 캐시 상태 확인 - 추적된 키와 통계
    /// </summary>
    [HttpGet("cache-status")]
    public async Task<IActionResult> GetCacheStatus()
    {
        try
        {
            // 각 테이블별 추적된 키 조회
            var productKeys = await _invalidationEngine.GetTrackedKeysAsync("products");
            var categoryKeys = await _invalidationEngine.GetTrackedKeysAsync("categories");
            var userKeys = await _invalidationEngine.GetTrackedKeysAsync("users");

            // 엔진 상태 조회
            var engineStatus = await _invalidationEngine.GetStatusAsync();

            // 실제 캐시된 항목 확인 (샘플)
            var cachedProducts = new List<object>();
            for (int i = 1; i <= 5; i++)
            {
                var key = Product.GetCacheKey(i);
                if (_memoryCache.TryGetValue(key, out ProductDto? product))
                {
                    cachedProducts.Add(new
                    {
                        productId = i,
                        productName = product.Name,
                        cacheKey = key,
                        price = product.Price,
                        stock = product.StockQuantity
                    });
                }
            }

            return Ok(new
            {
                cacheStatus = new
                {
                    engineHealthy = engineStatus.IsHealthy,
                    uptime = engineStatus.Uptime,
                    totalTrackedKeys = productKeys.Count() + categoryKeys.Count() + userKeys.Count()
                },
                trackedKeys = new
                {
                    products = productKeys.ToList(),
                    categories = categoryKeys.ToList(),
                    users = userKeys.ToList()
                },
                cachedProducts = cachedProducts,
                metrics = engineStatus.Metrics,
                message = cachedProducts.Any() 
                    ? $"현재 {cachedProducts.Count}개의 상품이 캐시되어 있습니다" 
                    : "캐시가 비어있습니다. 데이터 프리로드를 실행하세요",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache status");
            return StatusCode(500, new { error = "Failed to get cache status", details = ex.Message });
        }
    }

    /// <summary>
    /// 통합 시나리오 - 상품 조회 → 가격 변경 → 재조회 플로우
    /// </summary>
    [HttpPost("scenario/price-update-flow")]
    public async Task<IActionResult> PriceUpdateScenario([FromBody] PriceUpdateScenarioRequest request)
    {
        try
        {
            var steps = new List<object>();
            var cacheKey = Product.GetCacheKey(request.ProductId);

            // Step 1: 초기 상품 조회
            var wasCached1 = _memoryCache.TryGetValue(cacheKey, out _);
            var startTime1 = DateTime.UtcNow;
            var product1 = await _productService.GetByIdAsync(request.ProductId);
            var fetchTime1 = (DateTime.UtcNow - startTime1).TotalMilliseconds;

            if (product1 == null)
            {
                return NotFound(new { error = $"Product {request.ProductId} not found" });
            }

            steps.Add(new
            {
                step = 1,
                action = "초기 상품 조회",
                cacheHit = wasCached1,
                fetchTimeMs = Math.Round(fetchTime1, 2),
                dataSource = wasCached1 ? "Cache" : "Database",
                productData = new { product1.Name, product1.Price, product1.SalePrice, product1.StockQuantity }
            });

            // Step 2: 두 번째 조회 (캐시 히트 확인)
            var wasCached2 = _memoryCache.TryGetValue(cacheKey, out _);
            var startTime2 = DateTime.UtcNow;
            var product2 = await _productService.GetByIdAsync(request.ProductId);
            var fetchTime2 = (DateTime.UtcNow - startTime2).TotalMilliseconds;

            steps.Add(new
            {
                step = 2,
                action = "두 번째 조회 (캐시 히트 예상)",
                cacheHit = wasCached2,
                fetchTimeMs = Math.Round(fetchTime2, 2),
                dataSource = wasCached2 ? "Cache" : "Database",
                speedup = wasCached2 && fetchTime1 > 0 ? $"{Math.Round(fetchTime1 / fetchTime2, 1)}x faster" : "N/A"
            });

            // Step 3: 가격 변경
            var oldPrice = product1.Price;
            var success = await _productService.UpdatePriceAsync(request.ProductId, request.NewPrice, request.NewSalePrice);
            
            // 캐시 무효화
            await _invalidationEngine.InvalidateByKeyAsync(cacheKey);

            steps.Add(new
            {
                step = 3,
                action = "가격 변경 및 캐시 무효화",
                priceChange = new { oldPrice, newPrice = request.NewPrice },
                cacheInvalidated = true,
                invalidatedKey = cacheKey
            });

            // Step 4: 변경 후 조회 (캐시 미스 예상)
            var wasCached3 = _memoryCache.TryGetValue(cacheKey, out _);
            var startTime3 = DateTime.UtcNow;
            var product3 = await _productService.GetByIdAsync(request.ProductId);
            var fetchTime3 = (DateTime.UtcNow - startTime3).TotalMilliseconds;

            steps.Add(new
            {
                step = 4,
                action = "가격 변경 후 조회 (캐시 미스 예상)",
                cacheHit = wasCached3,
                fetchTimeMs = Math.Round(fetchTime3, 2),
                dataSource = wasCached3 ? "Cache" : "Database",
                productData = new { product3!.Name, product3.Price, product3.SalePrice, product3.StockQuantity },
                priceUpdated = product3.Price == request.NewPrice
            });

            // Step 5: 최종 조회 (캐시 재구성 확인)
            var wasCached4 = _memoryCache.TryGetValue(cacheKey, out _);
            var startTime4 = DateTime.UtcNow;
            var product4 = await _productService.GetByIdAsync(request.ProductId);
            var fetchTime4 = (DateTime.UtcNow - startTime4).TotalMilliseconds;

            steps.Add(new
            {
                step = 5,
                action = "최종 조회 (캐시 재구성 확인)",
                cacheHit = wasCached4,
                fetchTimeMs = Math.Round(fetchTime4, 2),
                dataSource = wasCached4 ? "Cache" : "Database",
                cacheRebuilt = wasCached4
            });

            return Ok(new
            {
                scenario = "가격 변경 및 캐시 무효화 전체 플로우",
                productId = request.ProductId,
                steps = steps,
                summary = new
                {
                    totalSteps = steps.Count,
                    cacheHits = steps.Count(s => ((dynamic)s).cacheHit),
                    cacheMisses = steps.Count(s => !((dynamic)s).cacheHit),
                    priceSuccessfullyUpdated = product3!.Price == request.NewPrice,
                    cacheSuccessfullyInvalidated = !wasCached3,
                    cacheSuccessfullyRebuilt = wasCached4
                },
                message = "가격 변경과 캐시 무효화가 정상적으로 작동했습니다",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute price update scenario");
            return StatusCode(500, new { error = "Scenario execution failed", details = ex.Message });
        }
    }
}

// Request DTOs
public class UpdatePriceRequest
{
    public decimal NewPrice { get; set; }
    public decimal? NewSalePrice { get; set; }
}

public class CreateOrderRequest
{
    public string CustomerName { get; set; } = string.Empty;
    public List<OrderItemRequest> Items { get; set; } = new();
}

public class OrderItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public class PriceUpdateScenarioRequest
{
    public int ProductId { get; set; }
    public decimal NewPrice { get; set; }
    public decimal? NewSalePrice { get; set; }
}