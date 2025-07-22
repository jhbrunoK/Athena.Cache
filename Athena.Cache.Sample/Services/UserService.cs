using Athena.Cache.Sample.Data;
using Athena.Cache.Sample.Models;
using Microsoft.EntityFrameworkCore;

namespace Athena.Cache.Sample.Services
{
    public class UserService(SampleDbContext context, ILogger<UserService> logger) : IUserService
    {
        private readonly ILogger<UserService> _logger = logger;

        public async Task<IEnumerable<UserDto>> GetUsersAsync(string? searchName = null, int? minAge = null, bool? isActive = null)
        {
            var query = context.Users.AsQueryable();

            if (!string.IsNullOrEmpty(searchName))
            {
                query = query.Where(u => u.Name.Contains(searchName));
            }

            if (minAge.HasValue)
            {
                query = query.Where(u => u.Age >= minAge.Value);
            }

            if (isActive.HasValue)
            {
                query = query.Where(u => u.IsActive == isActive.Value);
            }

            var users = await query
                .Include(u => u.Orders)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Name = u.Name,
                    Email = u.Email,
                    Age = u.Age,
                    IsActive = u.IsActive,
                    OrderCount = u.Orders.Count
                })
                .ToListAsync();

            return users;
        }

        public async Task<UserDto?> GetUserByIdAsync(int id)
        {
            var user = await context.Users
                .Include(u => u.Orders)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return null;

            return new UserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Age = user.Age,
                IsActive = user.IsActive,
                OrderCount = user.Orders.Count
            };
        }

        public async Task<UserDto> CreateUserAsync(User user)
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();

            return new UserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Age = user.Age,
                IsActive = user.IsActive,
                OrderCount = 0
            };
        }

        public async Task<UserDto?> UpdateUserAsync(int id, User updatedUser)
        {
            var user = await context.Users.FindAsync(id);
            if (user == null) return null;

            user.Name = updatedUser.Name;
            user.Email = updatedUser.Email;
            user.Age = updatedUser.Age;
            user.IsActive = updatedUser.IsActive;

            await context.SaveChangesAsync();

            return new UserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Age = user.Age,
                IsActive = user.IsActive,
                OrderCount = user.Orders?.Count ?? 0
            };
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            var user = await context.Users.FindAsync(id);
            if (user == null) return false;

            context.Users.Remove(user);
            await context.SaveChangesAsync();

            return true;
        }
    }
}
