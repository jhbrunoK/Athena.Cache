using Athena.Cache.Sample.Models;

namespace Athena.Cache.Sample.Data;

public static class SeedData
{
    public static async Task InitializeAsync(SampleDbContext context)
    {
        if (context.Users.Any()) return; // 이미 데이터가 있으면 스킵

        var users = new[]
        {
            new User { Name = "John Doe", Email = "john@example.com", Age = 30 },
            new User { Name = "Jane Smith", Email = "jane@example.com", Age = 25 },
            new User { Name = "Bob Johnson", Email = "bob@example.com", Age = 35 },
            new User { Name = "Alice Brown", Email = "alice@example.com", Age = 28 },
            new User { Name = "Charlie Wilson", Email = "charlie@example.com", Age = 40 }
        };

        context.Users.AddRange(users);
        await context.SaveChangesAsync();

        var orders = new[]
        {
            new Order { UserId = 1, ProductName = "Laptop", Amount = 1200.99m },
            new Order { UserId = 1, ProductName = "Mouse", Amount = 25.50m },
            new Order { UserId = 2, ProductName = "Keyboard", Amount = 89.99m },
            new Order { UserId = 2, ProductName = "Monitor", Amount = 299.99m },
            new Order { UserId = 3, ProductName = "Tablet", Amount = 499.99m },
            new Order { UserId = 4, ProductName = "Phone", Amount = 799.99m },
            new Order { UserId = 5, ProductName = "Headphones", Amount = 199.99m }
        };

        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();
    }
}