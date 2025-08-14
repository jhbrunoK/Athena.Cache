using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Athena.Invalidation.Sample.Models;

public class Product
{
    public int Id { get; set; }
    
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(1000)]
    public string? Description { get; set; }
    
    [Required]
    [StringLength(50)]
    public string Sku { get; set; } = string.Empty;
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal? SalePrice { get; set; }
    
    public int StockQuantity { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    public bool IsFeatured { get; set; } = false;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // 카테고리 관계
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    
    // 주문 아이템들
    [JsonIgnore]
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    
    // 캐시 키 생성용 헬퍼 메서드
    public static string GetCacheKey(int id) => $"product:{id}";
    public static string GetCacheKeyPattern() => "product:*";
    public static string GetByCategoryPattern(int categoryId) => $"products:category:{categoryId}:*";
    public static string GetFeaturedKey() => "products:featured";
    public static string GetTableName() => "products";
}

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal EffectivePrice => SalePrice ?? Price;
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsInStock => StockQuantity > 0;
    public bool IsOnSale => SalePrice.HasValue && SalePrice < Price;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
}

public class CreateProductRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(1000)]
    public string? Description { get; set; }
    
    [Required]
    [StringLength(50)]
    public string Sku { get; set; } = string.Empty;
    
    [Range(0.01, double.MaxValue)]
    public decimal Price { get; set; }
    
    [Range(0.01, double.MaxValue)]
    public decimal? SalePrice { get; set; }
    
    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }
    
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; } = false;
    
    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }
}