using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Athena.Invalidation.Sample.Models;

public class Order
{
    public int Id { get; set; }
    
    [Required]
    [StringLength(50)]
    public string OrderNumber { get; set; } = string.Empty;
    
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal ShippingAmount { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }
    
    [StringLength(1000)]
    public string? Notes { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    
    // 사용자 관계
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    
    // 배송 정보
    [Required]
    [StringLength(200)]
    public string ShippingAddress { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string ShippingCity { get; set; } = string.Empty;
    
    [Required]
    [StringLength(20)]
    public string ShippingPostalCode { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string ShippingCountry { get; set; } = string.Empty;
    
    // 주문 아이템들
    [JsonIgnore]
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    
    // 캐시 키 생성용 헬퍼 메서드
    public static string GetCacheKey(int id) => $"order:{id}";
    public static string GetCacheKeyByOrderNumber(string orderNumber) => $"order:number:{orderNumber}";
    public static string GetCacheKeyPattern() => "order:*";
    public static string GetByUserPattern(int userId) => $"orders:user:{userId}:*";
    public static string GetByStatusKey(OrderStatus status) => $"orders:status:{status}";
    public static string GetTableName() => "orders";
}

public class OrderItem
{
    public int Id { get; set; }
    
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    
    public int Quantity { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal => Quantity * UnitPrice;
    
    // 주문 시점의 상품 정보 (스냅샷)
    [Required]
    [StringLength(200)]
    public string ProductName { get; set; } = string.Empty;
    
    [Required]
    [StringLength(50)]
    public string ProductSku { get; set; } = string.Empty;
    
    // 캐시 키 생성용 헬퍼 메서드
    public static string GetTableName() => "order_items";
}

public enum OrderStatus
{
    Pending = 1,
    Confirmed = 2,
    Processing = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6,
    Refunded = 7
}

public class OrderDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    
    // 사용자 정보
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    
    // 배송 정보
    public string ShippingAddress { get; set; } = string.Empty;
    public string ShippingCity { get; set; } = string.Empty;
    public string ShippingPostalCode { get; set; } = string.Empty;
    public string ShippingCountry { get; set; } = string.Empty;
    public string FullShippingAddress { get; set; } = string.Empty;
    
    // 주문 아이템들
    public List<OrderItemDto> Items { get; set; } = new();
    
    public int TotalItems { get; set; }
    public int TotalQuantity { get; set; }
}

public class OrderItemDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class CreateOrderRequest
{
    [Required]
    public int UserId { get; set; }
    
    [Required]
    [StringLength(200)]
    public string ShippingAddress { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string ShippingCity { get; set; } = string.Empty;
    
    [Required]
    [StringLength(20)]
    public string ShippingPostalCode { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string ShippingCountry { get; set; } = string.Empty;
    
    [StringLength(1000)]
    public string? Notes { get; set; }
    
    [Required]
    [MinLength(1)]
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}

public class CreateOrderItemRequest
{
    [Required]
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }
    
    [Required]
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}