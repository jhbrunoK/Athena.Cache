using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;

namespace Athena.Cache.Tests.Unit;

public class CacheInvalidatorTests
{
    private readonly Mock<IAthenaCache> _mockCache;
    private readonly Mock<ICacheKeyGenerator> _mockKeyGenerator;
    private readonly Mock<ILogger<DefaultCacheInvalidator>> _mockLogger;
    private readonly DefaultCacheInvalidator _invalidator;
    private readonly AthenaCacheOptions _options;

    public CacheInvalidatorTests()
    {
        _mockCache = new Mock<IAthenaCache>();
        _mockKeyGenerator = new Mock<ICacheKeyGenerator>();
        _mockLogger = new Mock<ILogger<DefaultCacheInvalidator>>();

        _options = new AthenaCacheOptions
        {
            Namespace = "TestApp",
            DefaultExpirationMinutes = 30
        };

        _invalidator = new DefaultCacheInvalidator(
            _mockCache.Object,
            _mockKeyGenerator.Object,
            _options,
            _mockLogger.Object);
    }

    [Fact]
    public async Task InvalidateAsync_WithTrackedKeys_ShouldRemoveAllKeys()
    {
        // Arrange
        var tableName = "Users";
        var trackingKey = "TestApp_table_Users";
        var cachedKeys = new[] { "key1", "key2", "key3" };

        _mockKeyGenerator.Setup(x => x.GenerateTableTrackingKey(tableName))
            .Returns(trackingKey);

        _mockCache.Setup(x => x.GetAsync<HashSet<string>>(trackingKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync([..cachedKeys]);

        // Act
        await _invalidator.InvalidateAsync(tableName);

        // Assert
        foreach (var key in cachedKeys)
        {
            _mockCache.Verify(x => x.RemoveAsync(key, It.IsAny<CancellationToken>()), Times.Once);
        }

        _mockCache.Verify(x => x.RemoveAsync(trackingKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateAsync_WithNoTrackedKeys_ShouldNotRemoveAnything()
    {
        // Arrange
        var tableName = "Users";
        var trackingKey = "TestApp_table_Users";

        _mockKeyGenerator.Setup(x => x.GenerateTableTrackingKey(tableName))
            .Returns(trackingKey);

        _mockCache.Setup(x => x.GetAsync<HashSet<string>>(trackingKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // Act
        await _invalidator.InvalidateAsync(tableName);

        // Assert
        _mockCache.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TrackCacheKeyAsync_WithNewKey_ShouldAddToExistingSet()
    {
        // Arrange
        var tableName = "Users";
        var cacheKey = "Users_GetAll_ABC123";
        var trackingKey = "TestApp_table_Users";

        _mockKeyGenerator.Setup(x => x.GenerateTableTrackingKey(tableName))
            .Returns(trackingKey);

        var existingSet = new HashSet<string> { "existing_key" };
        _mockCache.Setup(x => x.GetAsync<HashSet<string>>(trackingKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSet);

        // Act
        await _invalidator.TrackCacheKeyAsync(tableName, cacheKey);

        // Assert
        _mockCache.Verify(x => x.SetAsync(
            trackingKey,
            It.Is<HashSet<string>>(set => set.Contains(cacheKey) && set.Contains("existing_key")),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TrackCacheKeyAsync_WithMultipleTables_ShouldTrackForAllTables()
    {
        // Arrange
        var tableNames = new[] { "Users", "Orders", "Products" };
        var cacheKey = "MultiTable_GetData_XYZ789";
        var trackingKeys = tableNames.Select(t => $"TestApp_table_{t}").ToArray();

        for (var i = 0; i < tableNames.Length; i++)
        {
            _mockKeyGenerator.Setup(x => x.GenerateTableTrackingKey(tableNames[i]))
                .Returns(trackingKeys[i]);
        }

        // Act
        await _invalidator.TrackCacheKeyAsync(tableNames, cacheKey);

        // Assert
        foreach (var trackingKey in trackingKeys)
        {
            _mockCache.Verify(x => x.SetAsync(
                trackingKey,
                It.Is<HashSet<string>>(set => set.Contains(cacheKey)),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task GetTrackedKeysAsync_WithExistingKeys_ShouldReturnAllKeys()
    {
        // Arrange
        var tableName = "Users";
        var trackingKey = "TestApp_table_Users";
        var expectedKeys = new HashSet<string> { "key1", "key2", "key3" };

        _mockKeyGenerator.Setup(x => x.GenerateTableTrackingKey(tableName))
            .Returns(trackingKey);

        _mockCache.Setup(x => x.GetAsync<HashSet<string>>(trackingKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedKeys);

        // Act
        var result = await _invalidator.GetTrackedKeysAsync(tableName);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result.Should().Contain(expectedKeys);
    }

    [Fact]
    public async Task GetTrackedKeysAsync_WithNoExistingKeys_ShouldReturnEmptyCollection()
    {
        // Arrange
        var tableName = "Users";
        var trackingKey = "TestApp_table_Users";

        _mockKeyGenerator.Setup(x => x.GenerateTableTrackingKey(tableName))
            .Returns(trackingKey);

        _mockCache.Setup(x => x.GetAsync<HashSet<string>>(trackingKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // Act
        var result = await _invalidator.GetTrackedKeysAsync(tableName);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidateByPatternAsync_ShouldCallCacheRemoveByPattern()
    {
        // Arrange
        var pattern = "Users_*";

        // Act
        await _invalidator.InvalidateByPatternAsync(pattern);

        // Assert
        _mockCache.Verify(x => x.RemoveByPatternAsync(pattern, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateWithRelatedAsync_ShouldInvalidateAllRelatedTables()
    {
        // Arrange
        var mainTable = "Users";
        var relatedTables = new[] { "Orders", "UserProfiles" };
        var maxDepth = 2;

        var trackingKeys = new Dictionary<string, string>
        {
            { "Users", "TestApp_table_Users" },
            { "Orders", "TestApp_table_Orders" },
            { "UserProfiles", "TestApp_table_UserProfiles" }
        };

        foreach (var kvp in trackingKeys)
        {
            _mockKeyGenerator.Setup(x => x.GenerateTableTrackingKey(kvp.Key))
                .Returns(kvp.Value);

            _mockCache.Setup(x => x.GetAsync<HashSet<string>>(kvp.Value, It.IsAny<CancellationToken>()))
                .ReturnsAsync([$"{kvp.Key}_key1", $"{kvp.Key}_key2"]);
        }

        // Act
        await _invalidator.InvalidateWithRelatedAsync(mainTable, relatedTables, maxDepth);

        // Assert
        // 모든 테이블의 추적 키가 제거되었는지 확인
        foreach (var trackingKey in trackingKeys.Values)
        {
            _mockCache.Verify(x => x.RemoveAsync(trackingKey, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}