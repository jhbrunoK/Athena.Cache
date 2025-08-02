using Athena.Cache.Core.Attributes;
using Athena.Cache.Core.Enums;
using Athena.Cache.Sample.Models;
using Athena.Cache.Sample.Services;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Cache.Sample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
    : ControllerBase
{
    /// <summary>
    /// 주문 목록 조회 (Convention 기반 Orders + 명시적 Users 테이블과 연관)
    /// </summary>
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 20)]
    [CacheInvalidateOn("Users", InvalidationType.Related, "Orders")]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders(
        [FromQuery] int? userId = null,
        [FromQuery] decimal? minAmount = null)
    {
        logger.LogInformation("GetOrders called with userId: {UserId}, minAmount: {MinAmount}",
            userId, minAmount);

        var orders = await orderService.GetOrdersAsync(userId, minAmount);
        return Ok(orders);
    }

    /// <summary>
    /// 특정 주문 조회 (Convention 기반 Orders + 명시적 Users 테이블 변경 시 무효화)
    /// </summary>
    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 45)]
    [CacheInvalidateOn("Users")]
    public async Task<ActionResult<OrderDto>> GetOrder(int id)
    {
        var order = await orderService.GetOrderByIdAsync(id);
        if (order == null)
        {
            return NotFound();
        }

        return Ok(order);
    }

    /// <summary>
    /// 주문 생성 (Convention 기반 Orders + 명시적 Users 테이블 무효화)
    /// </summary>
    [HttpPost]
    [CacheInvalidateOn("Users", InvalidationType.Related, "Orders")]
    public async Task<ActionResult<OrderDto>> CreateOrder([FromBody] Order order)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var createdOrder = await orderService.CreateOrderAsync(order);
        return CreatedAtAction(nameof(GetOrder), new { id = createdOrder.Id }, createdOrder);
    }

    /// <summary>
    /// 주문 삭제 (Convention 기반 Orders + 명시적 Users 테이블 무효화)
    /// </summary>
    [HttpDelete("{id}")]
    [CacheInvalidateOn("Users", InvalidationType.Related, "Orders")]
    public async Task<ActionResult> DeleteOrder(int id)
    {
        var deleted = await orderService.DeleteOrderAsync(id);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}