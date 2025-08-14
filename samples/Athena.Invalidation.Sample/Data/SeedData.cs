using Athena.Invalidation.Sample.Models;
using Microsoft.EntityFrameworkCore;

namespace Athena.Invalidation.Sample.Data;

public static class SeedData
{
    public static async Task InitializeAsync(SampleDbContext context)
    {
        // 데이터베이스가 생성되었는지 확인
        await context.Database.EnsureCreatedAsync();

        // 이미 데이터가 있으면 종료
        if (await context.Categories.AnyAsync())
        {
            return;
        }

        // 카테고리 시드 데이터
        var categories = new List<Category>
        {
            // 최상위 카테고리들
            new() { Id = 1, Name = "Electronics", Description = "Electronic devices and accessories" },
            new() { Id = 2, Name = "Books", Description = "Physical and digital books" },
            new() { Id = 3, Name = "Clothing", Description = "Apparel and fashion items" },
            new() { Id = 4, Name = "Home & Garden", Description = "Home improvement and garden supplies" },
            
            // 하위 카테고리들 - Electronics
            new() { Id = 5, Name = "Smartphones", Description = "Mobile phones and accessories", ParentCategoryId = 1 },
            new() { Id = 6, Name = "Laptops", Description = "Portable computers", ParentCategoryId = 1 },
            new() { Id = 7, Name = "Audio", Description = "Speakers, headphones, and audio devices", ParentCategoryId = 1 },
            
            // 하위 카테고리들 - Books
            new() { Id = 8, Name = "Fiction", Description = "Fictional literature", ParentCategoryId = 2 },
            new() { Id = 9, Name = "Non-Fiction", Description = "Educational and factual books", ParentCategoryId = 2 },
            new() { Id = 10, Name = "Technical", Description = "Programming and technology books", ParentCategoryId = 2 },
            
            // 하위 카테고리들 - Clothing
            new() { Id = 11, Name = "Men's Clothing", Description = "Men's apparel", ParentCategoryId = 3 },
            new() { Id = 12, Name = "Women's Clothing", Description = "Women's apparel", ParentCategoryId = 3 },
            new() { Id = 13, Name = "Shoes", Description = "Footwear for all ages", ParentCategoryId = 3 }
        };

        context.Categories.AddRange(categories);
        await context.SaveChangesAsync();

        // 사용자 시드 데이터
        var users = new List<User>
        {
            new() 
            { 
                Id = 1, 
                FirstName = "John", 
                LastName = "Doe", 
                Email = "john.doe@example.com", 
                PhoneNumber = "+1234567890",
                DateOfBirth = new DateTime(1985, 6, 15),
                Status = UserStatus.Active,
                LastLoginAt = DateTime.UtcNow.AddDays(-1)
            },
            new() 
            { 
                Id = 2, 
                FirstName = "Jane", 
                LastName = "Smith", 
                Email = "jane.smith@example.com", 
                PhoneNumber = "+1234567891",
                DateOfBirth = new DateTime(1990, 3, 22),
                Status = UserStatus.Active,
                LastLoginAt = DateTime.UtcNow.AddHours(-3)
            },
            new() 
            { 
                Id = 3, 
                FirstName = "Bob", 
                LastName = "Johnson", 
                Email = "bob.johnson@example.com", 
                PhoneNumber = "+1234567892",
                DateOfBirth = new DateTime(1988, 11, 8),
                Status = UserStatus.Active,
                LastLoginAt = DateTime.UtcNow.AddDays(-5)
            },
            new() 
            { 
                Id = 4, 
                FirstName = "Alice", 
                LastName = "Wilson", 
                Email = "alice.wilson@example.com", 
                PhoneNumber = "+1234567893",
                DateOfBirth = new DateTime(1992, 7, 30),
                Status = UserStatus.Inactive
            }
        };

        context.Users.AddRange(users);
        await context.SaveChangesAsync();

        // 상품 시드 데이터
        var products = new List<Product>
        {
            // Electronics - Smartphones
            new() { Id = 1, Name = "iPhone 14 Pro", Description = "Latest iPhone with advanced features", Sku = "IPHONE14PRO", Price = 999.99m, SalePrice = 899.99m, StockQuantity = 50, CategoryId = 5, IsFeatured = true },
            new() { Id = 2, Name = "Samsung Galaxy S23", Description = "Flagship Android smartphone", Sku = "GALAXYS23", Price = 849.99m, StockQuantity = 75, CategoryId = 5, IsFeatured = true },
            new() { Id = 3, Name = "Google Pixel 7", Description = "Pure Android experience", Sku = "PIXEL7", Price = 599.99m, StockQuantity = 30, CategoryId = 5 },
            
            // Electronics - Laptops
            new() { Id = 4, Name = "MacBook Pro 14\"", Description = "Professional laptop with M2 chip", Sku = "MBP14M2", Price = 1999.99m, StockQuantity = 25, CategoryId = 6, IsFeatured = true },
            new() { Id = 5, Name = "Dell XPS 13", Description = "Premium Windows laptop", Sku = "DELLXPS13", Price = 1299.99m, SalePrice = 1199.99m, StockQuantity = 40, CategoryId = 6 },
            new() { Id = 6, Name = "ThinkPad X1 Carbon", Description = "Business laptop with excellent keyboard", Sku = "TPX1C", Price = 1799.99m, StockQuantity = 20, CategoryId = 6 },
            
            // Electronics - Audio
            new() { Id = 7, Name = "AirPods Pro", Description = "Noise-canceling wireless earbuds", Sku = "AIRPODSPRO", Price = 249.99m, StockQuantity = 100, CategoryId = 7, IsFeatured = true },
            new() { Id = 8, Name = "Sony WH-1000XM4", Description = "Premium noise-canceling headphones", Sku = "SONYWH1000XM4", Price = 349.99m, SalePrice = 299.99m, StockQuantity = 60, CategoryId = 7 },
            
            // Books - Fiction
            new() { Id = 9, Name = "The Great Gatsby", Description = "Classic American novel", Sku = "GREATGATSBY", Price = 12.99m, StockQuantity = 200, CategoryId = 8 },
            new() { Id = 10, Name = "To Kill a Mockingbird", Description = "Pulitzer Prize winning novel", Sku = "MOCKINGBIRD", Price = 14.99m, StockQuantity = 150, CategoryId = 8 },
            
            // Books - Technical
            new() { Id = 11, Name = "Clean Code", Description = "A handbook of agile software craftsmanship", Sku = "CLEANCODE", Price = 49.99m, StockQuantity = 80, CategoryId = 10, IsFeatured = true },
            new() { Id = 12, Name = "Design Patterns", Description = "Elements of reusable object-oriented software", Sku = "DESIGNPATTERNS", Price = 54.99m, StockQuantity = 65, CategoryId = 10 },
            
            // Clothing - Men's
            new() { Id = 13, Name = "Men's Cotton T-Shirt", Description = "Comfortable everyday t-shirt", Sku = "MENSSHIRT01", Price = 19.99m, StockQuantity = 300, CategoryId = 11 },
            new() { Id = 14, Name = "Men's Denim Jeans", Description = "Classic blue jeans", Sku = "MENSJEANS01", Price = 79.99m, SalePrice = 59.99m, StockQuantity = 120, CategoryId = 11 },
            
            // Clothing - Women's
            new() { Id = 15, Name = "Women's Summer Dress", Description = "Elegant summer dress", Sku = "WOMENDRESS01", Price = 89.99m, StockQuantity = 90, CategoryId = 12, IsFeatured = true },
            
            // 재고 없는 상품
            new() { Id = 16, Name = "Out of Stock Item", Description = "This item is currently out of stock", Sku = "OUTOFSTOCK", Price = 99.99m, StockQuantity = 0, CategoryId = 1, IsActive = false }
        };

        context.Products.AddRange(products);
        await context.SaveChangesAsync();

        // 주문 시드 데이터
        var orders = new List<Order>
        {
            new() 
            { 
                Id = 1,
                OrderNumber = "ORD-2024-0001",
                OrderDate = DateTime.UtcNow.AddDays(-10),
                Status = OrderStatus.Delivered,
                UserId = 1,
                SubTotal = 949.98m,
                TaxAmount = 85.50m,
                ShippingAmount = 9.99m,
                DiscountAmount = 50.00m,
                TotalAmount = 995.47m,
                ShippingAddress = "123 Main St",
                ShippingCity = "New York",
                ShippingPostalCode = "10001",
                ShippingCountry = "USA",
                Notes = "Please leave at front door",
                ShippedAt = DateTime.UtcNow.AddDays(-8),
                DeliveredAt = DateTime.UtcNow.AddDays(-7)
            },
            new() 
            { 
                Id = 2,
                OrderNumber = "ORD-2024-0002",
                OrderDate = DateTime.UtcNow.AddDays(-5),
                Status = OrderStatus.Processing,
                UserId = 2,
                SubTotal = 1299.99m,
                TaxAmount = 117.00m,
                ShippingAmount = 0.00m, // Free shipping
                DiscountAmount = 0.00m,
                TotalAmount = 1416.99m,
                ShippingAddress = "456 Oak Ave",
                ShippingCity = "Los Angeles",
                ShippingPostalCode = "90210",
                ShippingCountry = "USA"
            },
            new() 
            { 
                Id = 3,
                OrderNumber = "ORD-2024-0003",
                OrderDate = DateTime.UtcNow.AddDays(-2),
                Status = OrderStatus.Confirmed,
                UserId = 3,
                SubTotal = 249.99m,
                TaxAmount = 22.50m,
                ShippingAmount = 9.99m,
                DiscountAmount = 0.00m,
                TotalAmount = 282.48m,
                ShippingAddress = "789 Pine St",
                ShippingCity = "Chicago",
                ShippingPostalCode = "60601",
                ShippingCountry = "USA",
                Notes = "Gift wrap requested"
            }
        };

        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();

        // 주문 아이템 시드 데이터
        var orderItems = new List<OrderItem>
        {
            // Order 1 items
            new() { OrderId = 1, ProductId = 1, Quantity = 1, UnitPrice = 899.99m, ProductName = "iPhone 14 Pro", ProductSku = "IPHONE14PRO" },
            new() { OrderId = 1, ProductId = 11, Quantity = 1, UnitPrice = 49.99m, ProductName = "Clean Code", ProductSku = "CLEANCODE" },
            
            // Order 2 items
            new() { OrderId = 2, ProductId = 5, Quantity = 1, UnitPrice = 1199.99m, ProductName = "Dell XPS 13", ProductSku = "DELLXPS13" },
            new() { OrderId = 2, ProductId = 8, Quantity = 1, UnitPrice = 299.99m, ProductName = "Sony WH-1000XM4", ProductSku = "SONYWH1000XM4" },
            
            // Order 3 items
            new() { OrderId = 3, ProductId = 7, Quantity = 1, UnitPrice = 249.99m, ProductName = "AirPods Pro", ProductSku = "AIRPODSPRO" }
        };

        context.OrderItems.AddRange(orderItems);
        await context.SaveChangesAsync();
    }
}