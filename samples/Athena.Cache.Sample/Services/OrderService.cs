using Athena.Cache.Sample.Data;
using Athena.Cache.Sample.Models;
using Microsoft.EntityFrameworkCore;

namespace Athena.Cache.Sample.Services;

public class OrderService(SampleDbContext context, ILogger<OrderService> logger) : IOrderService
{
    private readonly ILogger<OrderService> _logger = logger;

    public async Task<IEnumerable<OrderDto>> GetOrdersAsync(int? userId = null, decimal? minAmount = null)
    {
        var query = context.Orders.Include(o => o.User).AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(o => o.UserId == userId.Value);
        }

        if (minAmount.HasValue)
        {
            query = query.Where(o => o.Amount >= minAmount.Value);
        }

        var orders = await query
            .Select(o => new OrderDto
            {
                Id = o.Id,
                UserName = o.User.Name,
                ProductName = o.ProductName,
                Amount = o.Amount,
                OrderDate = o.OrderDate
            })
            .ToListAsync();

        return orders;
    }

    public async Task<OrderDto?> GetOrderByIdAsync(int id)
    {
        var order = await context.Orders
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return null;

        return new OrderDto
        {
            Id = order.Id,
            UserName = order.User.Name,
            ProductName = order.ProductName,
            Amount = order.Amount,
            OrderDate = order.OrderDate
        };
    }

    public async Task<OrderDto> CreateOrderAsync(Order order)
    {
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var user = await context.Users.FindAsync(order.UserId);

        return new OrderDto
        {
            Id = order.Id,
            UserName = user?.Name ?? "Unknown",
            ProductName = order.ProductName,
            Amount = order.Amount,
            OrderDate = order.OrderDate
        };
    }

    public async Task<bool> DeleteOrderAsync(int id)
    {
        var order = await context.Orders.FindAsync(id);
        if (order == null) return false;

        context.Orders.Remove(order);
        await context.SaveChangesAsync();

        return true;
    }
}