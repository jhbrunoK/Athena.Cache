using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.AspNetCore.Extensions;
using Xunit;

namespace Athena.Invalidation.Tests.Integration;

public class BasicInvalidationEngineTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IInvalidationEngine _engine;

    public BasicInvalidationEngineTests()
    {
        var services = new ServiceCollection();
        
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddInvalidationEngineComplete(options =>
        {
            options.Logging.LogInvalidationEvents = true;
            options.Logging.LogDebugInfo = true;
        });

        _serviceProvider = services.BuildServiceProvider();
        _engine = _serviceProvider.GetRequiredService<IInvalidationEngine>();
    }

    [Fact]
    public async Task InvalidationEngine_BasicOperations_ShouldWork()
    {
        // Arrange
        const string tableName = "TestTable";
        const string cacheKey = "test:cache:key";
        
        // Act & Assert - 키 추적
        await _engine.TrackCacheKeyAsync(tableName, cacheKey);

        // Act & Assert - 추적된 키 조회
        var trackedKeys = await _engine.GetTrackedKeysAsync(tableName);
        Assert.Contains(cacheKey, trackedKeys);

        // Act & Assert - 테이블 무효화
        await _engine.InvalidateByTableAsync(tableName);

        // Act & Assert - 무효화 후 키 확인 (추적 키가 제거되어야 함)
        var keysAfterInvalidation = await _engine.GetTrackedKeysAsync(tableName);
        Assert.Empty(keysAfterInvalidation);
    }

    [Fact]
    public async Task InvalidationEngine_PatternInvalidation_ShouldWork()
    {
        // Arrange
        var cacheProvider = _serviceProvider.GetServices<ICacheProvider>().First();
        
        const string pattern = "test:pattern:*";
        const string matchingKey1 = "test:pattern:123";
        const string matchingKey2 = "test:pattern:456";
        const string nonMatchingKey = "other:key:789";

        // 테스트 데이터 설정
        await cacheProvider.SetAsync(matchingKey1, "value1");
        await cacheProvider.SetAsync(matchingKey2, "value2");
        await cacheProvider.SetAsync(nonMatchingKey, "value3");

        // Act - 패턴 무효화
        await _engine.InvalidateByPatternAsync(pattern);

        // Assert - 패턴에 맞는 키들은 제거되고, 맞지 않는 키는 남아있어야 함
        Assert.False(await cacheProvider.ExistsAsync(matchingKey1));
        Assert.False(await cacheProvider.ExistsAsync(matchingKey2));
        Assert.True(await cacheProvider.ExistsAsync(nonMatchingKey));
        
        // 정리
        await cacheProvider.RemoveAsync(nonMatchingKey);
    }

    [Fact]
    public async Task InvalidationEngine_BatchInvalidation_ShouldWork()
    {
        // Arrange
        var tableNames = new[] { "Table1", "Table2", "Table3" };
        var cacheKeys = new[] { "key1", "key2", "key3" };

        // 각 테이블에 키 추적
        for (int i = 0; i < tableNames.Length; i++)
        {
            await _engine.TrackCacheKeyAsync(tableNames[i], cacheKeys[i]);
        }

        // Act - 배치 무효화
        await _engine.InvalidateBatchAsync(tableNames);

        // Assert - 모든 테이블의 추적된 키가 제거되어야 함
        foreach (var tableName in tableNames)
        {
            var keys = await _engine.GetTrackedKeysAsync(tableName);
            Assert.Empty(keys);
        }
    }

    [Fact]
    public async Task InvalidationEngine_HierarchicalInvalidation_ShouldWork()
    {
        // Arrange
        const string rootTable = "Users";
        var relatedTables = new[] { "UserProfiles", "UserPreferences" };
        
        var rootKey = "user:123";
        var relatedKeys = new[] { "profile:123", "preferences:123" };

        // 키 추적 설정
        await _engine.TrackCacheKeyAsync(rootTable, rootKey);
        for (int i = 0; i < relatedTables.Length; i++)
        {
            await _engine.TrackCacheKeyAsync(relatedTables[i], relatedKeys[i]);
        }

        // Act - 계층적 무효화
        await _engine.InvalidateHierarchyAsync(rootTable, relatedTables, maxDepth: 2);

        // Assert - 루트 테이블과 관련 테이블 모두 무효화되어야 함
        var rootTrackedKeys = await _engine.GetTrackedKeysAsync(rootTable);
        Assert.Empty(rootTrackedKeys);

        foreach (var relatedTable in relatedTables)
        {
            var relatedTrackedKeys = await _engine.GetTrackedKeysAsync(relatedTable);
            Assert.Empty(relatedTrackedKeys);
        }
    }

    [Fact]
    public async Task InvalidationEngine_Status_ShouldReturnHealthy()
    {
        // Act
        var status = await _engine.GetStatusAsync();

        // Assert
        Assert.True(status.IsHealthy);
        Assert.True(status.Uptime > TimeSpan.Zero);
        Assert.NotNull(status.Metrics);
    }

    [Fact]
    public async Task InvalidationEngine_MultipleProviders_ShouldWork()
    {
        // 이 테스트는 여러 캐시 프로바이더가 등록되었을 때를 위한 것
        // 현재는 MemoryCache 프로바이더만 등록되어 있음
        
        // Arrange
        var providers = _serviceProvider.GetServices<ICacheProvider>().ToList();
        
        // Act & Assert
        Assert.NotEmpty(providers);
        Assert.All(providers, provider => Assert.NotNull(provider.ProviderName));

        // 모든 프로바이더가 건강한 상태인지 확인
        foreach (var provider in providers)
        {
            var isHealthy = await provider.IsHealthyAsync();
            Assert.True(isHealthy, $"Provider {provider.ProviderName} is not healthy");
        }
    }

    [Fact]
    public async Task InvalidationEngine_MultipleKeysPerTable_ShouldWork()
    {
        // Arrange
        const string tableName = "MultiKeyTable";
        var keys = new[] { "key1", "key2", "key3", "key4" };

        // Act - 여러 키를 같은 테이블에 추적
        foreach (var key in keys)
        {
            await _engine.TrackCacheKeyAsync(tableName, key);
        }

        // Assert - 모든 키가 추적되는지 확인
        var trackedKeys = (await _engine.GetTrackedKeysAsync(tableName)).ToList();
        Assert.Equal(keys.Length, trackedKeys.Count);
        Assert.All(keys, key => Assert.Contains(key, trackedKeys));

        // Act - 테이블 무효화
        await _engine.InvalidateByTableAsync(tableName);

        // Assert - 모든 키가 제거되었는지 확인
        var keysAfterInvalidation = await _engine.GetTrackedKeysAsync(tableName);
        Assert.Empty(keysAfterInvalidation);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}