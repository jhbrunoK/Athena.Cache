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
    /// 사용자 목록 조회 (캐시 적용 + Users 테이블 변경 시 무효화)
    /// </summary>
    [HttpGet]
    [AthenaCache(ExpirationMinutes = 30)]
    [CacheInvalidateOn("Users")]
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
    /// 특정 사용자 조회 (캐시 적용 + Users, Orders 테이블 변경 시 무효화)
    /// </summary>
    [HttpGet("{id}")]
    [AthenaCache(ExpirationMinutes = 60)]
    [CacheInvalidateOn("Users")]
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
    /// 사용자 생성 (캐시 무효화 없음 - 새 데이터)
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
    /// 사용자 업데이트 (캐시 무효화 - Users 테이블 변경)
    /// </summary>
    [HttpPut("{id}")]
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

        // Users 테이블 변경으로 캐시 무효화 트리거 됨
        return Ok(updatedUser);
    }

    /// <summary>
    /// 사용자 삭제 (캐시 무효화 - Users, Orders 테이블 변경)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteUser(int id)
    {
        var deleted = await userService.DeleteUserAsync(id);
        if (!deleted)
        {
            return NotFound();
        }

        // Users 테이블 변경으로 캐시 무효화 트리거 됨
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