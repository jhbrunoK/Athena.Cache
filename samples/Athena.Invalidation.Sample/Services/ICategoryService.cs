using Athena.Invalidation.Sample.Models;

namespace Athena.Invalidation.Sample.Services;

public interface ICategoryService
{
    Task<CategoryDto?> GetByIdAsync(int id);
    Task<List<CategoryDto>> GetAllAsync();
    Task<List<CategoryDto>> GetActiveAsync();
    Task<List<CategoryDto>> GetRootCategoriesAsync();
    Task<List<CategoryDto>> GetSubCategoriesAsync(int parentId);
    Task<CategoryDto> CreateAsync(Category category);
    Task<CategoryDto> UpdateAsync(Category category);
    Task<bool> DeleteAsync(int id);
    Task<bool> ActivateAsync(int id);
    Task<bool> DeactivateAsync(int id);
}