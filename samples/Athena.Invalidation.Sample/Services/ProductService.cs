using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Sample.Data;
using Athena.Invalidation.Sample.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Athena.Invalidation.Sample.Services;

public class ProductService : IProductService
{
    private readonly SampleDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly ILogger<ProductService> _logger;
    
    private static readonly TimeSpan DefaultCacheExpiry = TimeSpan.FromMinutes(15);

    public ProductService(
        SampleDbContext context,
        IMemoryCache cache,
        IInvalidationEngine invalidationEngine,
        ILogger<ProductService> logger)
    {
        _context = context;
        _cache = cache;
        _invalidationEngine = invalidationEngine;
        _logger = logger;
    }

    public async Task<ProductDto?> GetByIdAsync(int id)
    {
        var cacheKey = Product.GetCacheKey(id);
        
        if (_cache.TryGetValue(cacheKey, out ProductDto? cached))
        {
            _logger.LogDebug("Cache hit for product {ProductId}", id);
            return cached;
        }

        var product = await _context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            return null;

        var dto = MapToDto(product);
        
        _cache.Set(cacheKey, dto, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Product.GetTableName(), cacheKey);
        
        return dto;
    }

    public async Task<ProductDto?> GetBySkuAsync(string sku)
    {
        var cacheKey = $"product:sku:{sku}";
        
        if (_cache.TryGetValue(cacheKey, out ProductDto? cached))
        {
            return cached;
        }

        var product = await _context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Sku == sku);

        if (product == null)
            return null;

        var dto = MapToDto(product);
        
        _cache.Set(cacheKey, dto, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Product.GetTableName(), cacheKey);
        
        return dto;
    }

    public async Task<List<ProductDto>> GetAllAsync()
    {
        const string cacheKey = "products:all";
        
        if (_cache.TryGetValue(cacheKey, out List<ProductDto>? cached))
        {
            return cached;
        }

        var products = await _context.Products
            .Include(p => p.Category)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var dtos = products.Select(MapToDto).ToList();
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Product.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<List<ProductDto>> GetActiveAsync()
    {
        const string cacheKey = "products:active";
        
        if (_cache.TryGetValue(cacheKey, out List<ProductDto>? cached))
        {
            return cached;
        }

        var products = await _context.Products
            .Where(p => p.IsActive)
            .Include(p => p.Category)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var dtos = products.Select(MapToDto).ToList();
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Product.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<List<ProductDto>> GetFeaturedAsync()
    {
        var cacheKey = Product.GetFeaturedKey();
        
        if (_cache.TryGetValue(cacheKey, out List<ProductDto>? cached))
        {
            return cached;
        }

        var products = await _context.Products
            .Where(p => p.IsFeatured && p.IsActive)
            .Include(p => p.Category)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var dtos = products.Select(MapToDto).ToList();
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Product.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<List<ProductDto>> GetByCategoryAsync(int categoryId)
    {
        var cacheKey = $"products:category:{categoryId}";
        
        if (_cache.TryGetValue(cacheKey, out List<ProductDto>? cached))
        {
            return cached;
        }

        var products = await _context.Products
            .Where(p => p.CategoryId == categoryId && p.IsActive)
            .Include(p => p.Category)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var dtos = products.Select(MapToDto).ToList();
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync([Product.GetTableName(), Category.GetTableName()], cacheKey);
        
        return dtos;
    }

    public async Task<List<ProductDto>> GetOnSaleAsync()
    {
        const string cacheKey = "products:on-sale";
        
        if (_cache.TryGetValue(cacheKey, out List<ProductDto>? cached))
        {
            return cached;
        }

        var products = await _context.Products
            .Where(p => p.SalePrice != null && p.SalePrice < p.Price && p.IsActive)
            .Include(p => p.Category)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var dtos = products.Select(MapToDto).ToList();
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Product.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<List<ProductDto>> GetInStockAsync()
    {
        const string cacheKey = "products:in-stock";
        
        if (_cache.TryGetValue(cacheKey, out List<ProductDto>? cached))
        {
            return cached;
        }

        var products = await _context.Products
            .Where(p => p.StockQuantity > 0 && p.IsActive)
            .Include(p => p.Category)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var dtos = products.Select(MapToDto).ToList();
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Product.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<List<ProductDto>> SearchAsync(string searchTerm)
    {
        var cacheKey = $"products:search:{searchTerm.ToLowerInvariant()}";
        
        if (_cache.TryGetValue(cacheKey, out List<ProductDto>? cached))
        {
            return cached;
        }

        var products = await _context.Products
            .Where(p => p.IsActive && (
                p.Name.Contains(searchTerm) ||
                p.Description!.Contains(searchTerm) ||
                p.Sku.Contains(searchTerm) ||
                p.Category.Name.Contains(searchTerm)))
            .Include(p => p.Category)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var dtos = products.Select(MapToDto).ToList();
        
        _cache.Set(cacheKey, dtos, TimeSpan.FromMinutes(5)); // 짧은 캐시 시간
        await _invalidationEngine.TrackCacheKeyAsync([Product.GetTableName(), Category.GetTableName()], cacheKey);
        
        return dtos;
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request)
    {
        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Sku = request.Sku,
            Price = request.Price,
            SalePrice = request.SalePrice,
            StockQuantity = request.StockQuantity,
            IsActive = request.IsActive,
            IsFeatured = request.IsFeatured,
            CategoryId = request.CategoryId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        
        // 상품 및 관련 캐시 무효화
        await _invalidationEngine.InvalidateByTableAsync(Product.GetTableName());
        await _invalidationEngine.InvalidateByPatternAsync($"products:category:{request.CategoryId}*");
        
        _logger.LogInformation("Created product {ProductId} '{ProductName}' and invalidated cache", 
            product.Id, product.Name);
        
        return MapToDto(product);
    }

    public async Task<ProductDto> UpdateAsync(int id, CreateProductRequest request)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            throw new ArgumentException($"Product with ID {id} not found");

        var oldCategoryId = product.CategoryId;
        
        product.Name = request.Name;
        product.Description = request.Description;
        product.Sku = request.Sku;
        product.Price = request.Price;
        product.SalePrice = request.SalePrice;
        product.StockQuantity = request.StockQuantity;
        product.IsActive = request.IsActive;
        product.IsFeatured = request.IsFeatured;
        product.CategoryId = request.CategoryId;
        product.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        
        // 상품 및 관련 캐시 무효화
        await _invalidationEngine.InvalidateByTableAsync(Product.GetTableName());
        await _invalidationEngine.InvalidateByPatternAsync($"products:category:{oldCategoryId}*");
        if (oldCategoryId != request.CategoryId)
        {
            await _invalidationEngine.InvalidateByPatternAsync($"products:category:{request.CategoryId}*");
        }
        
        return MapToDto(product);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return false;

        _context.Products.Remove(product);
        await _context.SaveChangesAsync();
        
        await _invalidationEngine.InvalidateByTableAsync(Product.GetTableName());
        await _invalidationEngine.InvalidateByPatternAsync($"products:category:{product.CategoryId}*");
        
        return true;
    }

    public async Task<bool> UpdateStockAsync(int id, int newStock)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return false;

        product.StockQuantity = newStock;
        product.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        
        // 재고 관련 캐시만 무효화
        await _invalidationEngine.InvalidateByKeyAsync(Product.GetCacheKey(id));
        await _invalidationEngine.InvalidateByKeyAsync("products:in-stock");
        
        return true;
    }

    public async Task<bool> UpdatePriceAsync(int id, decimal newPrice, decimal? newSalePrice = null)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return false;

        product.Price = newPrice;
        product.SalePrice = newSalePrice;
        product.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        
        // 가격 관련 캐시 무효화
        await _invalidationEngine.InvalidateByKeyAsync(Product.GetCacheKey(id));
        await _invalidationEngine.InvalidateByKeyAsync("products:on-sale");
        
        return true;
    }

    public async Task<bool> ToggleFeaturedAsync(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return false;

        product.IsFeatured = !product.IsFeatured;
        product.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        
        await _invalidationEngine.InvalidateByKeyAsync(Product.GetCacheKey(id));
        await _invalidationEngine.InvalidateByKeyAsync(Product.GetFeaturedKey());
        
        return true;
    }

    public async Task<bool> ToggleActiveAsync(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return false;

        product.IsActive = !product.IsActive;
        product.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        
        await _invalidationEngine.InvalidateByTableAsync(Product.GetTableName());
        
        return true;
    }

    private static ProductDto MapToDto(Product product)
    {
        return new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Sku = product.Sku,
            Price = product.Price,
            SalePrice = product.SalePrice,
            StockQuantity = product.StockQuantity,
            IsActive = product.IsActive,
            IsFeatured = product.IsFeatured,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name ?? ""
        };
    }
}