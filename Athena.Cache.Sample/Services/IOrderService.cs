using Athena.Cache.Sample.Models;

namespace Athena.Cache.Sample.Services
{
    public interface IOrderService
    {
        Task<IEnumerable<OrderDto>> GetOrdersAsync(int? userId = null, decimal? minAmount = null);
        Task<OrderDto?> GetOrderByIdAsync(int id);
        Task<OrderDto> CreateOrderAsync(Order order);
        Task<bool> DeleteOrderAsync(int id);
    }
}
