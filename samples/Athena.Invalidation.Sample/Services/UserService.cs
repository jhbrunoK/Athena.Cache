using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Sample.Data;
using Athena.Invalidation.Sample.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Athena.Invalidation.Sample.Services;

public class UserService : IUserService
{
    private readonly SampleDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly ILogger<UserService> _logger;
    
    private static readonly TimeSpan DefaultCacheExpiry = TimeSpan.FromMinutes(20);

    public UserService(
        SampleDbContext context,
        IMemoryCache cache,
        IInvalidationEngine invalidationEngine,
        ILogger<UserService> logger)
    {
        _context = context;
        _cache = cache;
        _invalidationEngine = invalidationEngine;
        _logger = logger;
    }

    public async Task<UserDto?> GetByIdAsync(int id)
    {
        var cacheKey = User.GetCacheKey(id);
        
        if (_cache.TryGetValue(cacheKey, out UserDto? cached))
        {
            return cached;
        }

        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return null;

        var dto = await MapToDtoAsync(user);
        
        _cache.Set(cacheKey, dto, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(User.GetTableName(), cacheKey);
        
        return dto;
    }

    public async Task<UserDto?> GetByEmailAsync(string email)
    {
        var cacheKey = User.GetCacheKeyByEmail(email);
        
        if (_cache.TryGetValue(cacheKey, out UserDto? cached))
        {
            return cached;
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
            return null;

        var dto = await MapToDtoAsync(user);
        
        _cache.Set(cacheKey, dto, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(User.GetTableName(), cacheKey);
        
        return dto;
    }

    public async Task<List<UserDto>> GetAllAsync()
    {
        const string cacheKey = "users:all";
        
        if (_cache.TryGetValue(cacheKey, out List<UserDto>? cached))
        {
            return cached;
        }

        var users = await _context.Users
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync();

        var dtos = new List<UserDto>();
        foreach (var user in users)
        {
            dtos.Add(await MapToDtoAsync(user));
        }
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(User.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<List<UserDto>> GetActiveAsync()
    {
        var cacheKey = User.GetActiveUsersKey();
        
        if (_cache.TryGetValue(cacheKey, out List<UserDto>? cached))
        {
            return cached;
        }

        var users = await _context.Users
            .Where(u => u.Status == UserStatus.Active)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync();

        var dtos = new List<UserDto>();
        foreach (var user in users)
        {
            dtos.Add(await MapToDtoAsync(user));
        }
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(User.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<List<UserDto>> GetByStatusAsync(UserStatus status)
    {
        var cacheKey = $"users:status:{status}";
        
        if (_cache.TryGetValue(cacheKey, out List<UserDto>? cached))
        {
            return cached;
        }

        var users = await _context.Users
            .Where(u => u.Status == status)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync();

        var dtos = new List<UserDto>();
        foreach (var user in users)
        {
            dtos.Add(await MapToDtoAsync(user));
        }
        
        _cache.Set(cacheKey, dtos, DefaultCacheExpiry);
        await _invalidationEngine.TrackCacheKeyAsync(User.GetTableName(), cacheKey);
        
        return dtos;
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request)
    {
        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            DateOfBirth = request.DateOfBirth,
            Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        
        await _invalidationEngine.InvalidateByTableAsync(User.GetTableName());
        
        _logger.LogInformation("Created user {UserId} '{UserEmail}' and invalidated cache", 
            user.Id, user.Email);
        
        return await MapToDtoAsync(user);
    }

    public async Task<UserDto> UpdateAsync(int id, CreateUserRequest request)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            throw new ArgumentException($"User with ID {id} not found");

        var oldEmail = user.Email;
        
        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Email = request.Email;
        user.PhoneNumber = request.PhoneNumber;
        user.DateOfBirth = request.DateOfBirth;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        
        // 사용자 관련 캐시 무효화
        await _invalidationEngine.InvalidateByTableAsync(User.GetTableName());
        
        // 이메일이 변경된 경우 이전 이메일 캐시도 무효화
        if (oldEmail != request.Email)
        {
            await _invalidationEngine.InvalidateByKeyAsync(User.GetCacheKeyByEmail(oldEmail));
        }
        
        return await MapToDtoAsync(user);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var user = await _context.Users
            .Include(u => u.Orders)
            .FirstOrDefaultAsync(u => u.Id == id);
            
        if (user == null)
            return false;

        // 주문이 있으면 삭제 불가 (상태만 변경)
        if (user.Orders.Any())
        {
            user.Status = UserStatus.Deleted;
            user.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.Users.Remove(user);
        }
        
        await _context.SaveChangesAsync();
        await _invalidationEngine.InvalidateByTableAsync(User.GetTableName());
        
        return true;
    }

    public async Task<bool> UpdateStatusAsync(int id, UserStatus status)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return false;

        user.Status = status;
        user.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        await _invalidationEngine.InvalidateByTableAsync(User.GetTableName());
        
        return true;
    }

    public async Task<bool> RecordLoginAsync(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return false;

        user.LastLoginAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        
        // 로그인 시간 업데이트는 해당 사용자 캐시만 무효화
        await _invalidationEngine.InvalidateByKeyAsync(User.GetCacheKey(id));
        await _invalidationEngine.InvalidateByKeyAsync(User.GetCacheKeyByEmail(user.Email));
        
        return true;
    }

    public async Task<UserDto?> GetUserWithStatsAsync(int id)
    {
        var cacheKey = $"user:stats:{id}";
        
        if (_cache.TryGetValue(cacheKey, out UserDto? cached))
        {
            return cached;
        }

        var user = await _context.Users
            .Include(u => u.Orders)
            .FirstOrDefaultAsync(u => u.Id == id);
            
        if (user == null)
            return null;

        var dto = await MapToDtoAsync(user, includeStats: true);
        
        _cache.Set(cacheKey, dto, TimeSpan.FromMinutes(10)); // 통계는 짧은 캐시 시간
        await _invalidationEngine.TrackCacheKeyAsync([User.GetTableName(), Order.GetTableName()], cacheKey);
        
        return dto;
    }

    private async Task<UserDto> MapToDtoAsync(User user, bool includeStats = false)
    {
        var dto = new UserDto
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            DateOfBirth = user.DateOfBirth,
            Age = DateTime.UtcNow.Year - user.DateOfBirth.Year,
            Status = user.Status,
            StatusText = user.Status.ToString(),
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            LastLoginAt = user.LastLoginAt
        };

        if (includeStats)
        {
            var orders = await _context.Orders
                .Where(o => o.UserId == user.Id)
                .ToListAsync();
                
            dto.TotalOrders = orders.Count;
            dto.TotalSpent = orders.Sum(o => o.TotalAmount);
        }

        return dto;
    }
}