using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Athena.Invalidation.Sample.Models;

public class User
{
    public int Id { get; set; }
    
    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;
    
    [Required]
    [EmailAddress]
    [StringLength(200)]
    public string Email { get; set; } = string.Empty;
    
    [StringLength(20)]
    public string? PhoneNumber { get; set; }
    
    public DateTime DateOfBirth { get; set; }
    
    public UserStatus Status { get; set; } = UserStatus.Active;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    
    // 이 사용자의 주문들
    [JsonIgnore]
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    
    // 전체 이름
    public string FullName => $"{FirstName} {LastName}";
    
    // 캐시 키 생성용 헬퍼 메서드
    public static string GetCacheKey(int id) => $"user:{id}";
    public static string GetCacheKeyByEmail(string email) => $"user:email:{email}";
    public static string GetCacheKeyPattern() => "user:*";
    public static string GetActiveUsersKey() => "users:active";
    public static string GetTableName() => "users";
}

public enum UserStatus
{
    Active = 1,
    Inactive = 2,
    Suspended = 3,
    Deleted = 4
}

public class UserDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateTime DateOfBirth { get; set; }
    public int Age { get; set; }
    public UserStatus Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalSpent { get; set; }
}

public class CreateUserRequest
{
    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;
    
    [Required]
    [EmailAddress]
    [StringLength(200)]
    public string Email { get; set; } = string.Empty;
    
    [Phone]
    [StringLength(20)]
    public string? PhoneNumber { get; set; }
    
    [Required]
    public DateTime DateOfBirth { get; set; }
}