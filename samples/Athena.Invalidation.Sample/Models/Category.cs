using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Athena.Invalidation.Sample.Models;

public class Category
{
    public int Id { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string? Description { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // 상위 카테고리 (계층 구조)
    public int? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    
    // 하위 카테고리들
    [JsonIgnore]
    public ICollection<Category> SubCategories { get; set; } = new List<Category>();
    
    // 이 카테고리의 상품들
    [JsonIgnore]
    public ICollection<Product> Products { get; set; } = new List<Product>();
    
    // 캐시 키 생성용 헬퍼 메서드
    public static string GetCacheKey(int id) => $"category:{id}";
    public static string GetCacheKeyPattern() => "category:*";
    public static string GetTableName() => "categories";
}

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? ParentCategoryId { get; set; }
    public string? ParentCategoryName { get; set; }
    public int ProductCount { get; set; }
    public int SubCategoryCount { get; set; }
}