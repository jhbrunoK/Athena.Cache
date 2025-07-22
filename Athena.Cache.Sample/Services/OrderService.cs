using Athena.Cache.Sample.Data;
using Athena.Cache.Sample.Models;
using Microsoft.EntityFrameworkCore;

namespace Athena.Cache.Sample.Services;

public class OrderService : IOrderService
{
    private readonly SampleDbContext _context;
    private readonly ILogger<OrderService> _logger;

    public OrderService(SampleDbContext context, ILogger<OrderService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IEnumerable<OrderDto>> GetOrdersAsync(int? userId = null, decimal? minAmount = null)
    {
        var query = _context.Orders.Include(o => o.User).AsQueryable();

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
        var order = await _context.Orders
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
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(order.UserId);

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
        var order = await _context.Orders.FindAsync(id);
        if (order == null) return false;

        _context.Orders.Remove(order);
        await _context.SaveChangesAsync();

        return true;
    }
}