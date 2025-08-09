using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Athena.Cache.Core.Abstractions;
using Athena.Cache.FusionCache.Extensions;

namespace Athena.Cache.Tests.Integration;

public class FusionCacheBasicTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IAthenaCache _cache;
    private readonly ICacheInvalidator _invalidator;

    public FusionCacheBasicTests()
    {
        var services = new ServiceCollection();
        
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        services.AddAthenaCacheFusionComplete(
            athena => {
                athena.Namespace = "TestApp";
                athena.DefaultExpirationMinutes = 5;
                athena.Logging.LogCacheHitMiss = true;
                athena.Logging.LogInvalidation = true;
            }
        );

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<IAthenaCache>();
        _invalidator = _serviceProvider.GetRequiredService<ICacheInvalidator>();
    }

    [Fact]
    public async Task FusionCacheProvider_BasicOperations_ShouldWork()
    {
        // Arrange
        const string key = "test:basic:key";
        const string value = "test value";

        // Act & Assert - Set
        await _cache.SetAsync(key, value);

        // Act & Assert - Get
        var retrievedValue = await _cache.GetAsync<string>(key);
        Assert.Equal(value, retrievedValue);

        // Act & Assert - Exists
        var exists = await _cache.ExistsAsync(key);
        Assert.True(exists);

        // Act & Assert - Remove
        await _cache.RemoveAsync(key);
        var afterRemoval = await _cache.GetAsync<string>(key);
        Assert.Null(afterRemoval);
    }

    [Fact]
    public async Task FusionCacheKeyGenerator_ShouldGenerateProperKeys()
    {
        // Arrange
        var keyGenerator = _serviceProvider.GetRequiredService<ICacheKeyGenerator>();
        Assert.NotNull(keyGenerator);

        // Act
        var key1 = await keyGenerator.GenerateKeyAsync("UsersController", "GetUser", 
            new Dictionary<string, object?> { {"id", 123}, {"includeDetails", true} });
        
        var key2 = keyGenerator.GenerateTableTrackingKey("Users");

        // Assert
        Assert.Contains("TestApp", key1);
        Assert.Contains("Users", key1);
        Assert.Contains("GetUser", key1);
        
        Assert.Contains("TestApp", key2);
        Assert.Contains("Users", key2);
    }

    [Fact]
    public async Task FusionCacheProvider_Statistics_ShouldTrackHitMiss()
    {
        // Arrange
        const string key = "test:stats:key";
        const string value = "test value";

        // Get initial statistics
        var initialStats = await _cache.GetStatisticsAsync();
        var initialHits = initialStats.HitCount;
        var initialMisses = initialStats.MissCount;

        // Act - Cache miss
        await _cache.GetAsync<string>(key);

        // Act - Cache set
        await _cache.SetAsync(key, value);

        // Act - Cache hit
        await _cache.GetAsync<string>(key);

        // Assert
        var finalStats = await _cache.GetStatisticsAsync();
        Assert.Equal(initialMisses + 1, finalStats.MissCount); // One miss
        Assert.Equal(initialHits + 1, finalStats.HitCount);    // One hit
        Assert.True(finalStats.TotalKeys >= 1);
        Assert.True(finalStats.Uptime > TimeSpan.Zero);
    }

    [Fact]
    public async Task FusionCacheProvider_BatchOperations_ShouldWork()
    {
        // Arrange
        var testData = new Dictionary<string, string>
        {
            { "test:batch:key1", "value1" },
            { "test:batch:key2", "value2" },
            { "test:batch:key3", "value3" }
        };

        // Act - Batch set
        await _cache.SetManyAsync(testData, TimeSpan.FromMinutes(5));

        // Act - Batch get
        var retrievedData = await _cache.GetManyAsync<string>(testData.Keys);

        // Assert
        Assert.Equal(testData.Count, retrievedData.Count);
        foreach (var kvp in testData)
        {
            Assert.True(retrievedData.ContainsKey(kvp.Key));
            Assert.Equal(kvp.Value, retrievedData[kvp.Key]);
        }

        // Act - Batch remove
        await _cache.RemoveManyAsync(testData.Keys);

        // Assert - All should be removed
        var afterRemoval = await _cache.GetManyAsync<string>(testData.Keys);
        foreach (var result in afterRemoval.Values)
        {
            Assert.Null(result);
        }
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}