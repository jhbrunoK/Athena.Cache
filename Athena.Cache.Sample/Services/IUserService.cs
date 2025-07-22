using Athena.Cache.Sample.Models;

namespace Athena.Cache.Sample.Services
{
    public interface IUserService
    {
        Task<IEnumerable<UserDto>> GetUsersAsync(string? searchName = null, int? minAge = null, bool? isActive = null);
        Task<UserDto?> GetUserByIdAsync(int id);
        Task<UserDto> CreateUserAsync(User user);
        Task<UserDto?> UpdateUserAsync(int id, User user);
        Task<bool> DeleteUserAsync(int id);
    }
}
