using Athena.Cache.Core.Attributes;
using Athena.Cache.Core.Enums;
using Athena.Cache.Sample.Models;
using Athena.Cache.Sample.Services;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Cache.Sample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    /// <summary>
    /// 주문 목록 조회 (Orders, Users 테이블과 연관)
    /// </summary>
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 20)]
    [CacheInvalidateOn("Orders")]
    [CacheInvalidateOn("Users", InvalidationType.Related, "Orders")]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders(
        [FromQuery] int? userId = null,
        [FromQuery] decimal? minAmount = null)
    {
        _logger.LogInformation("GetOrders called with userId: {UserId}, minAmount: {MinAmount}",
            userId, minAmount);

        var orders = await _orderService.GetOrdersAsync(userId, minAmount);
        return Ok(orders);
    }

    /// <summary>
    /// 특정 주문 조회
    /// </summary>
    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 45)]
    [CacheInvalidateOn("Orders")]
    [CacheInvalidateOn("Users")]
    public async Task<ActionResult<OrderDto>> GetOrder(int id)
    {
        var order = await _orderService.GetOrderByIdAsync(id);
        if (order == null)
        {
            return NotFound();
        }

        return Ok(order);
    }

    /// <summary>
    /// 주문 생성
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrder([FromBody] Order order)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var createdOrder = await _orderService.CreateOrderAsync(order);
        return CreatedAtAction(nameof(GetOrder), new { id = createdOrder.Id }, createdOrder);
    }

    /// <summary>
    /// 주문 삭제
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteOrder(int id)
    {
        var deleted = await _orderService.DeleteOrderAsync(id);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}