namespace Athena.Invalidation.Sample.Commands;

public abstract class BaseCommand
{
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = "system";
}

public class CreateProductCommand : BaseCommand
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? SalePrice { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; } = false;
    public int CategoryId { get; set; }
}

public class UpdateProductCommand : BaseCommand
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? SalePrice { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; } = false;
    public int CategoryId { get; set; }
}

public class DeleteProductCommand : BaseCommand
{
    public int ProductId { get; set; }
}

public class UpdateProductStockCommand : BaseCommand
{
    public int ProductId { get; set; }
    public int NewStockQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class UpdateProductPriceCommand : BaseCommand
{
    public int ProductId { get; set; }
    public decimal NewPrice { get; set; }
    public decimal? NewSalePrice { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class BulkUpdateProductPricesCommand : BaseCommand
{
    public List<ProductPriceUpdate> Updates { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

public class ProductPriceUpdate
{
    public int ProductId { get; set; }
    public decimal NewPrice { get; set; }
    public decimal? NewSalePrice { get; set; }
}