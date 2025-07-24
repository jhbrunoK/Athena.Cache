using Athena.Cache.Core.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Athena.Cache.Redis;
using FluentAssertions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Athena.Cache.Tests.Integration;

public class RedisCacheProviderIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithPortBinding(6379, true)
        .Build();
    private RedisCacheProvider? _cacheProvider;
    private IConnectionMultiplexer? _redis;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var connectionString = _redisContainer.GetConnectionString();
        _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);

        var athenaOptions = new AthenaCacheOptions
        {
            Namespace = "IntegrationTest",
            DefaultExpirationMinutes = 5
        };

        var redisOptions = new RedisCacheOptions
        {
            ConnectionString = connectionString,
            DatabaseId = 0
        };

        var mockLogger = new Mock<ILogger<RedisCacheProvider>>();

        _cacheProvider = new RedisCacheProvider(_redis, athenaOptions, redisOptions, mockLogger.Object);
    }

    public async Task DisposeAsync()
    {
        _redis?.Dispose();
        await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task SetAndGetAsync_ShouldWorkCorrectly()
    {
        // Arrange
        var key = "integration_test_key";
        var value = new { Id = 1, Name = "Integration Test", CreatedAt = DateTime.UtcNow };

        // Act
        await _cacheProvider!.SetAsync(key, value);
        var result = await _cacheProvider.GetAsync<object>(key);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetManyAsync_ShouldReturnMultipleValues()
    {
        // Arrange
        var keys = new[] { "batch_key1", "batch_key2", "batch_key3" };
        await _cacheProvider!.SetAsync("batch_key1", "value1");
        await _cacheProvider.SetAsync("batch_key2", "value2");
        // batch_key3는 설정하지 않음

        // Act
        var results = await _cacheProvider.GetManyAsync<string>(keys);

        // Assert
        results.Should().HaveCount(3);
        results["batch_key1"].Should().Be("value1");
        results["batch_key2"].Should().Be("value2");
        results["batch_key3"].Should().BeNull();
    }

    [Fact]
    public async Task SetManyAsync_ShouldStoreMultipleValues()
    {
        // Arrange
        var keyValuePairs = new Dictionary<string, string>
        {
            { "multi_key1", "multi_value1" },
            { "multi_key2", "multi_value2" },
            { "multi_key3", "multi_value3" }
        };

        // Act
        await _cacheProvider!.SetManyAsync(keyValuePairs, TimeSpan.FromMinutes(10));

        // Assert
        foreach (var kvp in keyValuePairs)
        {
            var value = await _cacheProvider.GetAsync<string>(kvp.Key);
            value.Should().Be(kvp.Value);
        }
    }

    [Fact]
    public async Task RemoveByPatternAsync_WithRedis_ShouldRemoveMatchingKeys()
    {
        // Arrange
        await _cacheProvider!.SetAsync("pattern_user_1", "User1");
        await _cacheProvider.SetAsync("pattern_user_2", "User2");
        await _cacheProvider.SetAsync("pattern_order_1", "Order1");

        // Act
        await _cacheProvider.RemoveByPatternAsync("*pattern_user_*");

        // Assert
        var user1 = await _cacheProvider.GetAsync<string>("pattern_user_1");
        var user2 = await _cacheProvider.GetAsync<string>("pattern_user_2");
        var order1 = await _cacheProvider.GetAsync<string>("pattern_order_1");

        user1.Should().BeNull();
        user2.Should().BeNull();
        order1.Should().Be("Order1");
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnCorrectStatus()
    {
        // Arrange
        var existingKey = "existing_key";
        var nonExistingKey = "non_existing_key";

        await _cacheProvider!.SetAsync(existingKey, "some_value");

        // Act & Assert
        var exists = await _cacheProvider.ExistsAsync(existingKey);
        var notExists = await _cacheProvider.ExistsAsync(nonExistingKey);

        exists.Should().BeTrue();
        notExists.Should().BeFalse();
    }
}