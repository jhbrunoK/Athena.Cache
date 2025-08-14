namespace Athena.Invalidation.Sample.Events;

public abstract class BaseDomainEvent
{
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string EventType => GetType().Name;
}

// 상품 관련 이벤트
public class ProductCreatedEvent : BaseDomainEvent
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public decimal Price { get; set; }
    public bool IsFeatured { get; set; }
}

public class ProductUpdatedEvent : BaseDomainEvent
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int OldCategoryId { get; set; }
    public int NewCategoryId { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public bool OldIsFeatured { get; set; }
    public bool NewIsFeatured { get; set; }
}

public class ProductDeletedEvent : BaseDomainEvent
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
}

public class ProductStockChangedEvent : BaseDomainEvent
{
    public int ProductId { get; set; }
    public int OldStock { get; set; }
    public int NewStock { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ProductPriceChangedEvent : BaseDomainEvent
{
    public int ProductId { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public decimal? OldSalePrice { get; set; }
    public decimal? NewSalePrice { get; set; }
    public string Reason { get; set; } = string.Empty;
}

// 카테고리 관련 이벤트
public class CategoryCreatedEvent : BaseDomainEvent
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int? ParentCategoryId { get; set; }
}

public class CategoryUpdatedEvent : BaseDomainEvent
{
    public int CategoryId { get; set; }
    public string OldName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
    public int? OldParentCategoryId { get; set; }
    public int? NewParentCategoryId { get; set; }
}

public class CategoryDeletedEvent : BaseDomainEvent
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public List<int> AffectedProductIds { get; set; } = new();
}

// 사용자 관련 이벤트
public class UserCreatedEvent : BaseDomainEvent
{
    public int UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}

public class UserUpdatedEvent : BaseDomainEvent
{
    public int UserId { get; set; }
    public string OldEmail { get; set; } = string.Empty;
    public string NewEmail { get; set; } = string.Empty;
    public string UserStatus { get; set; } = string.Empty;
}

public class UserStatusChangedEvent : BaseDomainEvent
{
    public int UserId { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
}

// 주문 관련 이벤트
public class OrderCreatedEvent : BaseDomainEvent
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int UserId { get; set; }
    public decimal TotalAmount { get; set; }
    public List<int> ProductIds { get; set; } = new();
}

public class OrderStatusChangedEvent : BaseDomainEvent
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
}

public class OrderCompletedEvent : BaseDomainEvent
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int UserId { get; set; }
    public decimal TotalAmount { get; set; }
    public List<int> ProductIds { get; set; } = new();
}

// 시스템 이벤트
public class CacheInvalidatedEvent : BaseDomainEvent
{
    public string InvalidationType { get; set; } = string.Empty; // "key", "pattern", "table", "hierarchy"
    public string Target { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int AffectedKeyCount { get; set; }
}

public class DataConsistencyIssueDetectedEvent : BaseDomainEvent
{
    public string IssueType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> AffectedTables { get; set; } = new();
    public string Severity { get; set; } = "Medium"; // Low, Medium, High, Critical
}