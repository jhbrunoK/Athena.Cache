using FluentAssertions;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Athena.Cache.Tests.Integration;

public class WebApplicationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public WebApplicationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetUsers_FirstCall_ShouldReturnCacheMiss()
    {
        // Act
        var response = await _client.GetAsync("/api/users");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        response.Headers.Should().ContainKey("X-Athena-Cache");
        response.Headers.GetValues("X-Athena-Cache").First().Should().Be("MISS");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetUsers_SecondCall_ShouldReturnCacheHit()
    {
        // Arrange
        var endpoint = "/api/users?minAge=25";

        // Act - 첫 번째 호출 (MISS 예상)
        var firstResponse = await _client.GetAsync(endpoint);
        var firstContent = await firstResponse.Content.ReadAsStringAsync();

        // Act - 두 번째 호출 (HIT 예상)
        var secondResponse = await _client.GetAsync(endpoint);
        var secondContent = await secondResponse.Content.ReadAsStringAsync();

        // Assert
        firstResponse.IsSuccessStatusCode.Should().BeTrue();
        secondResponse.IsSuccessStatusCode.Should().BeTrue();

        firstResponse.Headers.GetValues("X-Athena-Cache").First().Should().Be("MISS");
        secondResponse.Headers.GetValues("X-Athena-Cache").First().Should().Be("HIT");

        firstContent.Should().Be(secondContent); // 동일한 응답
    }

    [Fact]
    public async Task CreateUser_ShouldNotAffectExistingCache()
    {
        // Arrange
        var getUsersEndpoint = "/api/users";

        // 캐시 생성
        await _client.GetAsync(getUsersEndpoint);

        var newUser = new
        {
            Name = "Test User",
            Email = "test@example.com",
            Age = 25,
            IsActive = true
        };

        var json = JsonSerializer.Serialize(newUser);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act - 사용자 생성
        var createResponse = await _client.PostAsync("/api/users", content);

        // Act - 사용자 목록 다시 조회 (여전히 캐시에서 가져와야 함)
        var getUsersResponse = await _client.GetAsync(getUsersEndpoint);

        // Assert
        createResponse.IsSuccessStatusCode.Should().BeTrue();
        getUsersResponse.Headers.GetValues("X-Athena-Cache").First().Should().Be("HIT");
    }

    [Fact]
    public async Task GetCacheStatistics_ShouldReturnValidData()
    {
        // Arrange
        await _client.GetAsync("/api/users"); // 캐시 MISS 생성
        await _client.GetAsync("/api/users"); // 캐시 HIT 생성

        // Act
        var response = await _client.GetAsync("/api/cache/statistics");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();

        var content = await response.Content.ReadAsStringAsync();
        var stats = JsonSerializer.Deserialize<JsonElement>(content);

        stats.GetProperty("hitCount").GetInt64().Should().BeGreaterThan(0);
        stats.GetProperty("missCount").GetInt64().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task InvalidateCache_ShouldClearCache()
    {
        // Arrange
        var endpoint = "/api/users";

        // 캐시 생성
        await _client.GetAsync(endpoint);
        var cachedResponse = await _client.GetAsync(endpoint);
        cachedResponse.Headers.GetValues("X-Athena-Cache").First().Should().Be("HIT");

        // Act - 캐시 무효화
        var invalidateResponse = await _client.DeleteAsync("/api/cache/invalidate/Users");

        // Act - 다시 조회 (MISS가 되어야 함)
        var afterInvalidateResponse = await _client.GetAsync(endpoint);

        // Assert
        invalidateResponse.IsSuccessStatusCode.Should().BeTrue();
        afterInvalidateResponse.Headers.GetValues("X-Athena-Cache").First().Should().Be("MISS");
    }
}