using Athena.Invalidation.Sample.Models;

namespace Athena.Invalidation.Sample.Services;

public interface IUserService
{
    Task<UserDto?> GetByIdAsync(int id);
    Task<UserDto?> GetByEmailAsync(string email);
    Task<List<UserDto>> GetAllAsync();
    Task<List<UserDto>> GetActiveAsync();
    Task<List<UserDto>> GetByStatusAsync(UserStatus status);
    Task<UserDto> CreateAsync(CreateUserRequest request);
    Task<UserDto> UpdateAsync(int id, CreateUserRequest request);
    Task<bool> DeleteAsync(int id);
    Task<bool> UpdateStatusAsync(int id, UserStatus status);
    Task<bool> RecordLoginAsync(int id);
    Task<UserDto?> GetUserWithStatsAsync(int id);
}