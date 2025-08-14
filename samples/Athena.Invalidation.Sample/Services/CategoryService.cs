using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Sample.Data;
using Athena.Invalidation.Sample.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Athena.Invalidation.Sample.Services;

public class CategoryService : ICategoryService
{
    private readonly SampleDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly ILogger<CategoryService> _logger;
    
    private static readonly TimeSpan DefaultCacheExpiry = TimeSpan.FromMinutes(30);

    public CategoryService(
        SampleDbContext context,
        IMemoryCache cache,
        IInvalidationEngine invalidationEngine,
        ILogger<CategoryService> logger)
    {
        _context = context;
        _cache = cache;
        _invalidationEngine = invalidationEngine;
        _logger = logger;
    }

    public async Task<CategoryDto?> GetByIdAsync(int id)
    {
        var cacheKey = Category.GetCacheKey(id);
        
        if (_cache.TryGetValue(cacheKey, out CategoryDto? cached))
        {
            _logger.LogDebug("Cache hit for category {CategoryId}", id);
            return cached;
        }

        _logger.LogDebug("Cache miss for category {CategoryId}, fetching from database", id);
        
        var category = await _context.Categories
            .Include(c => c.ParentCategory)
            .Include(c => c.SubCategories)
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
            return null;

        var dto = MapToDto(category);
        
        // 캐시에 저장하고 추적
        _cache.Set(cacheKey, dto, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Category.GetTableName(), cacheKey);
        
        _logger.LogDebug("Cached category {CategoryId} with key {CacheKey}", id, cacheKey);
        
        return dto;
    }

    public async Task<List<CategoryDto>> GetAllAsync()
    {
        const string cacheKey = "categories:all";
        
        if (_cache.TryGetValue(cacheKey, out List<CategoryDto>? cached))
        {
            _logger.LogDebug("Cache hit for all categories");
            return cached;
        }

        _logger.LogDebug("Cache miss for all categories, fetching from database");
        
        var categories = await _context.Categories
            .Include(c => c.ParentCategory)
            .Include(c => c.SubCategories)
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var dtos = categories.Select(MapToDto).ToList();
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Category.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<List<CategoryDto>> GetActiveAsync()
    {
        const string cacheKey = "categories:active";
        
        if (_cache.TryGetValue(cacheKey, out List<CategoryDto>? cached))
        {
            _logger.LogDebug("Cache hit for active categories");
            return cached;
        }

        var categories = await _context.Categories
            .Where(c => c.IsActive)
            .Include(c => c.ParentCategory)
            .Include(c => c.SubCategories)
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var dtos = categories.Select(MapToDto).ToList();
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Category.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<List<CategoryDto>> GetRootCategoriesAsync()
    {
        const string cacheKey = "categories:root";
        
        if (_cache.TryGetValue(cacheKey, out List<CategoryDto>? cached))
        {
            return cached;
        }

        var categories = await _context.Categories
            .Where(c => c.ParentCategoryId == null)
            .Include(c => c.SubCategories)
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var dtos = categories.Select(MapToDto).ToList();
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Category.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<List<CategoryDto>> GetSubCategoriesAsync(int parentId)
    {
        var cacheKey = $"categories:parent:{parentId}";
        
        if (_cache.TryGetValue(cacheKey, out List<CategoryDto>? cached))
        {
            return cached;
        }

        var categories = await _context.Categories
            .Where(c => c.ParentCategoryId == parentId)
            .Include(c => c.ParentCategory)
            .Include(c => c.SubCategories)
            .Include(c => c.Products)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var dtos = categories.Select(MapToDto).ToList();
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(Category.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<CategoryDto> CreateAsync(Category category)
    {
        category.CreatedAt = DateTime.UtcNow;
        
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
        
        // 카테고리 테이블 관련 캐시 무효화
        await _invalidationEngine.InvalidateByTableAsync(Category.GetTableName());
        
        _logger.LogInformation("Created category {CategoryId} '{CategoryName}' and invalidated cache", 
            category.Id, category.Name);
        
        return MapToDto(category);
    }

    public async Task<CategoryDto> UpdateAsync(Category category)
    {
        category.UpdatedAt = DateTime.UtcNow;
        
        _context.Categories.Update(category);
        await _context.SaveChangesAsync();
        
        // 해당 카테고리와 관련된 모든 캐시 무효화
        await _invalidationEngine.InvalidateByTableAsync(Category.GetTableName());
        await _invalidationEngine.InvalidateByPatternAsync($"products:category:{category.Id}:*");
        
        _logger.LogInformation("Updated category {CategoryId} '{CategoryName}' and invalidated cache", 
            category.Id, category.Name);
        
        return MapToDto(category);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var category = await _context.Categories
            .Include(c => c.Products)
            .Include(c => c.SubCategories)
            .FirstOrDefaultAsync(c => c.Id == id);
            
        if (category == null)
            return false;

        // 하위 카테고리가 있으면 삭제 불가
        if (category.SubCategories.Any())
        {
            throw new InvalidOperationException("Cannot delete category with subcategories");
        }

        // 상품이 있으면 삭제 불가
        if (category.Products.Any())
        {
            throw new InvalidOperationException("Cannot delete category with products");
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();
        
        // 관련 캐시 무효화
        await _invalidationEngine.InvalidateByTableAsync(Category.GetTableName());
        
        _logger.LogInformation("Deleted category {CategoryId} and invalidated cache", id);
        
        return true;
    }

    public async Task<bool> ActivateAsync(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
            return false;

        category.IsActive = true;
        category.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        await _invalidationEngine.InvalidateByTableAsync(Category.GetTableName());
        
        return true;
    }

    public async Task<bool> DeactivateAsync(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
            return false;

        category.IsActive = false;
        category.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        await _invalidationEngine.InvalidateByTableAsync(Category.GetTableName());
        
        return true;
    }

    private static CategoryDto MapToDto(Category category)
    {
        return new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            IsActive = category.IsActive,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt,
            ParentCategoryId = category.ParentCategoryId,
            ParentCategoryName = category.ParentCategory?.Name,
            ProductCount = category.Products?.Count ?? 0,
            SubCategoryCount = category.SubCategories?.Count ?? 0
        };
    }
}