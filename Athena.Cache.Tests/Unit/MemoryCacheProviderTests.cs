using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace Athena.Cache.Tests.Unit;

public class MemoryCacheProviderTests
{
    private readonly MemoryCacheProvider _cacheProvider;
    private readonly IMemoryCache _memoryCache;
    private readonly Mock<ILogger<MemoryCacheProvider>> _mockLogger;
    private readonly AthenaCacheOptions _options;

    public MemoryCacheProviderTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _mockLogger = new Mock<ILogger<MemoryCacheProvider>>();

        _options = new AthenaCacheOptions
        {
            Namespace = "TestApp",
            DefaultExpirationMinutes = 30,
            Logging = new CacheLoggingOptions
            {
                LogCacheHitMiss = true,
                LogInvalidation = true
            }
        };

        _cacheProvider = new MemoryCacheProvider(_memoryCache, _options, _mockLogger.Object);
    }

    [Fact]
    public async Task GetAsync_WithExistingKey_ShouldReturnValue()
    {
        // Arrange
        var key = "test_key";
        var value = "test_value";
        await _cacheProvider.SetAsync(key, value);

        // Act
        var result = await _cacheProvider.GetAsync<string>(key);

        // Assert
        result.Should().Be(value);
    }

    [Fact]
    public async Task GetAsync_WithNonExistingKey_ShouldReturnDefault()
    {
        // Act
        var result = await _cacheProvider.GetAsync<string>("non_existing_key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_WithComplexObject_ShouldSerializeAndStore()
    {
        // Arrange
        var key = "user_key";
        var user = new { Id = 1, Name = "John", Age = 30 };

        // Act
        await _cacheProvider.SetAsync(key, user);
        var result = await _cacheProvider.GetAsync<object>(key);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveAsync_ShouldRemoveKeyFromCache()
    {
        // Arrange
        var key = "test_key";
        var value = "test_value";
        await _cacheProvider.SetAsync(key, value);

        // Act
        await _cacheProvider.RemoveAsync(key);
        var result = await _cacheProvider.GetAsync<string>(key);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_WithExistingKey_ShouldReturnTrue()
    {
        // Arrange
        var key = "test_key";
        var value = "test_value";
        await _cacheProvider.SetAsync(key, value);

        // Act
        var exists = await _cacheProvider.ExistsAsync(key);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveByPatternAsync_ShouldRemoveMatchingKeys()
    {
        // Arrange
        await _cacheProvider.SetAsync("user_1", "John");
        await _cacheProvider.SetAsync("user_2", "Jane");
        await _cacheProvider.SetAsync("order_1", "Order1");

        // Act
        await _cacheProvider.RemoveByPatternAsync("user_*");

        // Assert
        var user1 = await _cacheProvider.GetAsync<string>("user_1");
        var user2 = await _cacheProvider.GetAsync<string>("user_2");
        var order1 = await _cacheProvider.GetAsync<string>("order_1");

        user1.Should().BeNull();
        user2.Should().BeNull();
        order1.Should().Be("Order1"); // 패턴에 맞지 않으므로 유지
    }

    [Fact]
    public async Task GetManyAsync_ShouldReturnMultipleValues()
    {
        // Arrange
        var keys = new[] { "key1", "key2", "key3" };
        await _cacheProvider.SetAsync("key1", "value1");
        await _cacheProvider.SetAsync("key2", "value2");
        // key3는 설정하지 않음

        // Act
        var results = await _cacheProvider.GetManyAsync<string>(keys);

        // Assert
        results.Should().HaveCount(3);
        results["key1"].Should().Be("value1");
        results["key2"].Should().Be("value2");
        results["key3"].Should().BeNull();
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldReturnValidStatistics()
    {
        // Arrange
        await _cacheProvider.SetAsync("key1", "value1");
        await _cacheProvider.GetAsync<string>("key1"); // Hit
        await _cacheProvider.GetAsync<string>("key2"); // Miss

        // Act
        var stats = await _cacheProvider.GetStatisticsAsync();

        // Assert
        stats.Should().NotBeNull();
        stats.TotalKeys.Should().Be(1);
        stats.HitCount.Should().Be(1);
        stats.MissCount.Should().Be(1);
        stats.HitRatio.Should().Be(0.5);
    }
}