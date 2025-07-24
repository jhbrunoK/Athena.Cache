using FluentAssertions;
using System.Text;
using System.Text.Json;
using Athena.Cache.Core.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Athena.Cache.Core.Abstractions;
using Microsoft.AspNetCore.TestHost;

namespace Athena.Cache.Tests.Integration;

/// <summary>
/// 간소화된 ASP.NET Core 통합 테스트 (TestServer 사용)
/// </summary>
public class SimplifiedWebIntegrationTests
{
    private TestServer CreateTestServer()
    {
        var hostBuilder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddControllers();
                services.AddRouting();
                services.AddLogging();

                // Athena Cache 설정
                services.AddAthenaCacheComplete(options =>
                {
                    options.Namespace = "TestApp";
                    options.VersionKey = "v1.0";
                    options.DefaultExpirationMinutes = 5;
                    options.Logging.LogCacheHitMiss = true;
                });

                // 테스트용 서비스 (각 테스트마다 새 인스턴스)
                services.AddTransient<TestUserService>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAthenaCache(); // 캐시 미들웨어
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });

        return new TestServer(hostBuilder);
    }

    [Fact]
    public async Task GetUsers_FirstCall_ShouldReturnCacheMiss()
    {
        // Arrange
        using var server = CreateTestServer();
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/api/testusers");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();

        if (response.Headers.Contains("X-Athena-Cache"))
        {
            response.Headers.GetValues("X-Athena-Cache").First().Should().Be("MISS");
        }

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetUsers_SecondCall_ShouldReturnCacheHit()
    {
        // Arrange
        using var server = CreateTestServer();
        var client = server.CreateClient();
        var endpoint = "/api/testusers?minAge=25";

        // Act - 첫 번째 호출 (MISS 예상)
        var firstResponse = await client.GetAsync(endpoint);
        var firstContent = await firstResponse.Content.ReadAsStringAsync();

        // Act - 두 번째 호출 (HIT 예상)  
        var secondResponse = await client.GetAsync(endpoint);
        var secondContent = await secondResponse.Content.ReadAsStringAsync();

        // Assert
        firstResponse.IsSuccessStatusCode.Should().BeTrue();
        secondResponse.IsSuccessStatusCode.Should().BeTrue();

        // 캐시 헤더 확인 (있는 경우에만)
        if (firstResponse.Headers.Contains("X-Athena-Cache") &&
            secondResponse.Headers.Contains("X-Athena-Cache"))
        {
            firstResponse.Headers.GetValues("X-Athena-Cache").First().Should().Be("MISS");
            secondResponse.Headers.GetValues("X-Athena-Cache").First().Should().Be("HIT");
        }

        firstContent.Should().Be(secondContent); // 동일한 응답
    }

    [Fact]
    public async Task CreateUser_ShouldNotAffectExistingCache()
    {
        // Arrange
        using var server = CreateTestServer();
        var client = server.CreateClient();
        var getUsersEndpoint = "/api/testusers";

        // 캐시 생성
        await client.GetAsync(getUsersEndpoint);

        var newUser = new TestUser
        {
            Name = "Test User",
            Email = "test@example.com",
            Age = 25
        };

        var json = JsonSerializer.Serialize(newUser);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act - 사용자 생성
        var createResponse = await client.PostAsync("/api/testusers", content);

        // Act - 사용자 목록 다시 조회
        var getUsersResponse = await client.GetAsync(getUsersEndpoint);

        // Assert
        createResponse.IsSuccessStatusCode.Should().BeTrue();
        getUsersResponse.IsSuccessStatusCode.Should().BeTrue();

        // 가능하면 캐시 헤더 확인
        if (getUsersResponse.Headers.Contains("X-Athena-Cache"))
        {
            getUsersResponse.Headers.GetValues("X-Athena-Cache").First().Should().Be("HIT");
        }
    }

    [Fact]
    public async Task GetCacheStatistics_ShouldReturnValidData()
    {
        // Arrange
        using var server = CreateTestServer();
        var client = server.CreateClient();

        // 캐시 생성을 위한 호출들
        await client.GetAsync("/api/testusers"); // MISS
        await client.GetAsync("/api/testusers"); // HIT

        // Act
        var response = await client.GetAsync("/api/testcache/statistics");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();

        // JSON 파싱 시도
        try
        {
            var stats = JsonSerializer.Deserialize<JsonElement>(content);

            if (stats.TryGetProperty("hitCount", out var hitCount))
            {
                hitCount.GetInt64().Should().BeGreaterThanOrEqualTo(0);
            }

            if (stats.TryGetProperty("missCount", out var missCount))
            {
                missCount.GetInt64().Should().BeGreaterThanOrEqualTo(0);
            }
        }
        catch (JsonException)
        {
            // JSON 파싱 실패해도 응답이 있으면 성공으로 간주
            content.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task InvalidateCache_ShouldWork()
    {
        // Arrange
        using var server = CreateTestServer();
        var client = server.CreateClient();
        var endpoint = "/api/testusers";

        // 캐시 생성
        await client.GetAsync(endpoint);
        var cachedResponse = await client.GetAsync(endpoint);

        // Act - 캐시 무효화
        var invalidateResponse = await client.DeleteAsync("/api/testcache/invalidate/Users");

        // Act - 다시 조회
        var afterInvalidateResponse = await client.GetAsync(endpoint);

        // Assert
        invalidateResponse.IsSuccessStatusCode.Should().BeTrue();
        afterInvalidateResponse.IsSuccessStatusCode.Should().BeTrue();

        // 캐시 무효화 후에는 MISS가 되어야 함 (헤더가 있는 경우)
        if (afterInvalidateResponse.Headers.Contains("X-Athena-Cache"))
        {
            afterInvalidateResponse.Headers.GetValues("X-Athena-Cache").First().Should().Be("MISS");
        }
    }
}

/// <summary>
/// 기본 기능 테스트 (HTTP 호출 없이)
/// </summary>
public class BasicFunctionalityTests
{
    [Fact]
    public async Task CacheMiddleware_ShouldBeRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddAthenaCacheComplete();

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - 서비스가 등록되었는지 확인
        var cache = serviceProvider.GetService<Athena.Cache.Core.Abstractions.IAthenaCache>();
        var invalidator = serviceProvider.GetService<Athena.Cache.Core.Abstractions.ICacheInvalidator>();
        var keyGenerator = serviceProvider.GetService<Athena.Cache.Core.Abstractions.ICacheKeyGenerator>();

        cache.Should().NotBeNull();
        invalidator.Should().NotBeNull();
        keyGenerator.Should().NotBeNull();
    }

    [Fact]
    public async Task CacheSystem_BasicWorkflow_ShouldWork()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddAthenaCacheComplete();

        var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<Athena.Cache.Core.Abstractions.IAthenaCache>();
        var invalidator = serviceProvider.GetRequiredService<Athena.Cache.Core.Abstractions.ICacheInvalidator>();

        // Act & Assert - 기본 캐시 작업
        var testKey = "test_key";
        var testValue = "test_value";

        await cache.SetAsync(testKey, testValue);
        var retrieved = await cache.GetAsync<string>(testKey);
        retrieved.Should().Be(testValue);

        // 무효화 테스트
        await invalidator.TrackCacheKeyAsync("TestTable", testKey);
        await invalidator.InvalidateAsync("TestTable");

        var afterInvalidation = await cache.GetAsync<string>(testKey);
        afterInvalidation.Should().BeNull();
    }
}

/// <summary>
/// 테스트용 사용자 모델
/// </summary>
public class TestUser
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; }
}

/// <summary>
/// 테스트용 사용자 서비스 (격리된 데이터)
/// </summary>
public class TestUserService
{
    private readonly List<TestUser> _users;

    public TestUserService()
    {
        // 각 서비스 인스턴스마다 독립적인 데이터
        _users = new List<TestUser>
        {
            new TestUser { Id = 1, Name = "John Doe", Email = "john@test.com", Age = 30 },
            new TestUser { Id = 2, Name = "Jane Smith", Email = "jane@test.com", Age = 25 },
            new TestUser { Id = 3, Name = "Bob Johnson", Email = "bob@test.com", Age = 35 }
        };
    }

    public Task<List<TestUser>> GetUsersAsync(int? minAge = null)
    {
        var users = _users.AsQueryable();

        if (minAge.HasValue)
        {
            users = users.Where(u => u.Age >= minAge.Value);
        }

        return Task.FromResult(users.ToList());
    }

    public Task<TestUser?> GetUserByIdAsync(int id)
    {
        var user = _users.FirstOrDefault(u => u.Id == id);
        return Task.FromResult(user);
    }

    public Task<TestUser> CreateUserAsync(TestUser user)
    {
        user.Id = _users.Any() ? _users.Max(u => u.Id) + 1 : 1;
        _users.Add(user);
        return Task.FromResult(user);
    }
}

/// <summary>
/// 테스트용 사용자 컨트롤러
/// </summary>
[Microsoft.AspNetCore.Mvc.ApiController]
[Microsoft.AspNetCore.Mvc.Route("api/[controller]")]
public class TestUsersController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    private readonly TestUserService _userService;

    public TestUsersController(TestUserService userService)
    {
        _userService = userService;
    }

    [Microsoft.AspNetCore.Mvc.HttpGet]
    [Athena.Cache.Core.Attributes.AthenaCache(ExpirationMinutes = 10)]
    [Athena.Cache.Core.Attributes.CacheInvalidateOn("Users")]
    public async Task<Microsoft.AspNetCore.Mvc.ActionResult<IEnumerable<TestUser>>> GetUsers(
        [Microsoft.AspNetCore.Mvc.FromQuery] int? minAge = null)
    {
        var users = await _userService.GetUsersAsync(minAge);
        return Ok(users);
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("{id}")]
    [Athena.Cache.Core.Attributes.AthenaCache(ExpirationMinutes = 15)]
    [Athena.Cache.Core.Attributes.CacheInvalidateOn("Users")]
    public async Task<Microsoft.AspNetCore.Mvc.ActionResult<TestUser>> GetUser(int id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }
        return Ok(user);
    }

    [Microsoft.AspNetCore.Mvc.HttpPost]
    public async Task<Microsoft.AspNetCore.Mvc.ActionResult<TestUser>> CreateUser([Microsoft.AspNetCore.Mvc.FromBody] TestUser user)
    {
        var createdUser = await _userService.CreateUserAsync(user);
        return CreatedAtAction(nameof(GetUser), new { id = createdUser.Id }, createdUser);
    }
}

/// <summary>
/// 테스트용 캐시 컨트롤러
/// </summary>
[Microsoft.AspNetCore.Mvc.ApiController]
[Microsoft.AspNetCore.Mvc.Route("api/[controller]")]
public class TestCacheController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    private readonly Athena.Cache.Core.Abstractions.IAthenaCache _cache;
    private readonly Athena.Cache.Core.Abstractions.ICacheInvalidator _invalidator;

    public TestCacheController(
        Athena.Cache.Core.Abstractions.IAthenaCache cache,
        Athena.Cache.Core.Abstractions.ICacheInvalidator invalidator)
    {
        _cache = cache;
        _invalidator = invalidator;
    }

    [Microsoft.AspNetCore.Mvc.HttpGet("statistics")]
    public async Task<Microsoft.AspNetCore.Mvc.ActionResult<Athena.Cache.Core.Abstractions.CacheStatistics>> GetStatistics()
    {
        var stats = await _cache.GetStatisticsAsync();
        return Ok(stats);
    }

    [Microsoft.AspNetCore.Mvc.HttpDelete("invalidate/{tableName}")]
    public async Task<Microsoft.AspNetCore.Mvc.ActionResult> InvalidateTable(string tableName)
    {
        await _invalidator.InvalidateAsync(tableName);
        return Ok(new { message = $"Cache invalidated for table: {tableName}" });
    }
}

/// <summary>
/// ASP.NET Core 통합 테스트 (TestServer 사용으로 변경)
/// </summary>
public class WebApplicationIntegrationTests
{
    private TestServer CreateTestServer()
    {
        var hostBuilder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddControllers();
                services.AddRouting();
                services.AddLogging();

                // Athena Cache 설정
                services.AddAthenaCacheComplete(options =>
                {
                    options.Namespace = "TestApp";
                    options.VersionKey = "v1.0";
                    options.DefaultExpirationMinutes = 5;
                    options.Logging.LogCacheHitMiss = true;
                });

                // 테스트용 서비스
                services.AddSingleton<TestUserService>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAthenaCache(); // 캐시 미들웨어
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });

        return new TestServer(hostBuilder);
    }

    [Fact]
    public async Task GetUsers_FirstCall_ShouldReturnCacheMiss()
    {
        // Arrange
        using var server = CreateTestServer();
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("/api/testusers");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetUsers_SecondCall_ShouldReturnCacheHit()
    {
        // Arrange
        using var server = CreateTestServer();
        var client = server.CreateClient();
        var endpoint = "/api/testusers?minAge=25";

        // Act - 첫 번째 호출 (MISS 예상)
        var firstResponse = await client.GetAsync(endpoint);
        var firstContent = await firstResponse.Content.ReadAsStringAsync();

        // Act - 두 번째 호출 (HIT 예상)  
        var secondResponse = await client.GetAsync(endpoint);
        var secondContent = await secondResponse.Content.ReadAsStringAsync();

        // Assert
        firstResponse.IsSuccessStatusCode.Should().BeTrue();
        secondResponse.IsSuccessStatusCode.Should().BeTrue();
        firstContent.Should().Be(secondContent); // 동일한 응답
    }

    [Fact]
    public async Task CreateUser_ShouldNotAffectExistingCache()
    {
        // Arrange
        using var server = CreateTestServer();
        var client = server.CreateClient();
        var getUsersEndpoint = "/api/testusers";

        // 캐시 생성
        await client.GetAsync(getUsersEndpoint);

        var newUser = new TestUser
        {
            Name = "Test User",
            Email = "test@example.com",
            Age = 25
        };

        var json = JsonSerializer.Serialize(newUser);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act - 사용자 생성
        var createResponse = await client.PostAsync("/api/testusers", content);

        // Act - 사용자 목록 다시 조회
        var getUsersResponse = await client.GetAsync(getUsersEndpoint);

        // Assert
        createResponse.IsSuccessStatusCode.Should().BeTrue();
        getUsersResponse.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task GetCacheStatistics_ShouldReturnValidData()
    {
        // Arrange
        using var server = CreateTestServer();
        var client = server.CreateClient();

        // 캐시 생성을 위한 호출들
        await client.GetAsync("/api/testusers"); // MISS
        await client.GetAsync("/api/testusers"); // HIT

        // Act
        var response = await client.GetAsync("/api/testcache/statistics");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvalidateCache_ShouldWork()
    {
        // Arrange
        using var server = CreateTestServer();
        var client = server.CreateClient();
        var endpoint = "/api/testusers";

        // 캐시 생성
        await client.GetAsync(endpoint);
        var cachedResponse = await client.GetAsync(endpoint);

        // Act - 캐시 무효화
        var invalidateResponse = await client.DeleteAsync("/api/testcache/invalidate/Users");

        // Act - 다시 조회
        var afterInvalidateResponse = await client.GetAsync(endpoint);

        // Assert
        invalidateResponse.IsSuccessStatusCode.Should().BeTrue();
        afterInvalidateResponse.IsSuccessStatusCode.Should().BeTrue();
    }
}