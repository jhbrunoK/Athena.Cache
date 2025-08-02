using Athena.Cache.Core.Attributes;
using Athena.Cache.Core.Enums;
using Athena.Cache.Sample.Models;
using Athena.Cache.Sample.Services;
using Microsoft.AspNetCore.Mvc;

namespace Athena.Cache.Sample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(IUserService userService, ILogger<UsersController> logger) : ControllerBase
{
    /// <summary>
    /// 사용자 목록 조회 (캐시 적용 + Convention 기반 Users 테이블 변경 시 무효화)
    /// </summary>
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers(
        [FromQuery] string? searchName = null,
        [FromQuery] int? minAge = null,
        [FromQuery] bool? isActive = null)
    {
        logger.LogInformation("GetUsers called with searchName: {SearchName}, minAge: {MinAge}, isActive: {IsActive}",
            searchName, minAge, isActive);

        var users = await userService.GetUsersAsync(searchName, minAge, isActive);
        return Ok(users);
    }

    /// <summary>
    /// 특정 사용자 조회 (캐시 적용 + Convention 기반 Users + 명시적 Orders 테이블 변경 시 무효화)
    /// </summary>
    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 60)]
    [CacheInvalidateOn("Orders", InvalidationType.Pattern, "User_*")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        logger.LogInformation("GetUser called with id: {Id}", id);

        var user = await userService.GetUserByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        return Ok(user);
    }

    /// <summary>
    /// 사용자 생성 (Convention 기반 Users 테이블 무효화)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] User user)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var createdUser = await userService.CreateUserAsync(user);
        return CreatedAtAction(nameof(GetUser), new { id = createdUser.Id }, createdUser);
    }

    /// <summary>
    /// 사용자 업데이트 (Convention 기반 Users + 명시적 Orders 테이블 무효화)
    /// </summary>
    [HttpPut("{id}")]
    [CacheInvalidateOn("Orders", InvalidationType.Pattern, "User_*")]
    public async Task<ActionResult<UserDto>> UpdateUser(int id, [FromBody] User user)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var updatedUser = await userService.UpdateUserAsync(id, user);
        if (updatedUser == null)
        {
            return NotFound();
        }

        return Ok(updatedUser);
    }

    /// <summary>
    /// 사용자 삭제 (Convention 기반 Users + 명시적 Orders 테이블 무효화)
    /// </summary>
    [HttpDelete("{id}")]
    [CacheInvalidateOn("Orders", InvalidationType.Related, "Users")]
    public async Task<ActionResult> DeleteUser(int id)
    {
        var deleted = await userService.DeleteUserAsync(id);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// 캐시 비활성화 데모 API
    /// </summary>
    [HttpGet("no-cache")]
    [NoCache]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsersWithoutCache()
    {
        logger.LogInformation("GetUsersWithoutCache called - 캐시 사용 안함");

        var users = await userService.GetUsersAsync();
        return Ok(users);
    }
}