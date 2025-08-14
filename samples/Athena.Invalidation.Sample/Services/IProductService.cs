using Athena.Invalidation.Sample.Models;

namespace Athena.Invalidation.Sample.Services;

public interface IProductService
{
    Task<ProductDto?> GetByIdAsync(int id);
    Task<ProductDto?> GetBySkuAsync(string sku);
    Task<List<ProductDto>> GetAllAsync();
    Task<List<ProductDto>> GetActiveAsync();
    Task<List<ProductDto>> GetFeaturedAsync();
    Task<List<ProductDto>> GetByCategoryAsync(int categoryId);
    Task<List<ProductDto>> GetOnSaleAsync();
    Task<List<ProductDto>> GetInStockAsync();
    Task<List<ProductDto>> SearchAsync(string searchTerm);
    Task<ProductDto> CreateAsync(CreateProductRequest request);
    Task<ProductDto> UpdateAsync(int id, CreateProductRequest request);
    Task<bool> DeleteAsync(int id);
    Task<bool> UpdateStockAsync(int id, int newStock);
    Task<bool> UpdatePriceAsync(int id, decimal newPrice, decimal? newSalePrice = null);
    Task<bool> ToggleFeaturedAsync(int id);
    Task<bool> ToggleActiveAsync(int id);
}