using Athena.Cache.Core.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Athena.Cache.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace Athena.Cache.Tests.EndToEnd;

public class EndToEndScenarioTests
{
    [Fact]
    public async Task CompleteWorkflow_WithMemoryCache_ShouldWorkCorrectly()
    {
        // Arrange - DI 컨테이너 설정
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddAthenaCache(options =>
        {
            options.Namespace = "E2ETest";
            options.DefaultExpirationMinutes = 10;
        });

        var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<IAthenaCache>();
        var invalidator = serviceProvider.GetRequiredService<ICacheInvalidator>();
        var keyGenerator = serviceProvider.GetRequiredService<ICacheKeyGenerator>();

        // Scenario 1: 기본 캐시 작업
        var testData = new { Id = 1, Name = "Test", CreatedAt = DateTime.UtcNow };
        var cacheKey = keyGenerator.GenerateKey("TestController", "GetTest", new Dictionary<string, object?> { { "id", 1 } });

        await cache.SetAsync(cacheKey, testData);
        var retrievedData = await cache.GetAsync<dynamic>(cacheKey);
        retrievedData.Should().NotBeNull();

        // Scenario 2: 테이블 추적 및 무효화
        await invalidator.TrackCacheKeyAsync("TestTable", cacheKey);
        var trackedKeys = await invalidator.GetTrackedKeysAsync("TestTable");
        trackedKeys.Should().Contain(cacheKey);

        // Scenario 3: 무효화 실행
        await invalidator.InvalidateAsync("TestTable");
        var afterInvalidation = await cache.GetAsync<dynamic>(cacheKey);
        afterInvalidation.Should().BeNull();

        // Scenario 4: 통계 확인
        var statistics = await cache.GetStatisticsAsync();
        statistics.Should().NotBeNull();
        statistics.TotalRequests.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CacheWithComplexScenario_ShouldHandleEdgeCases()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAthenaCache();

        var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<IAthenaCache>();
        var invalidator = serviceProvider.GetRequiredService<ICacheInvalidator>();

        // Edge Case 1: Null 값 처리
        await cache.SetAsync("null_test", (string?)null);
        var nullResult = await cache.GetAsync<string>("null_test");
        // null 값은 저장하지 않음

        // Edge Case 2: 빈 컬렉션 처리
        await cache.SetAsync("empty_list", new List<string>());
        var emptyListResult = await cache.GetAsync<List<string>>("empty_list");
        emptyListResult.Should().NotBeNull().And.BeEmpty();

        // Edge Case 3: 대용량 객체 처리
        var largeObject = new
        {
            Id = 1,
            Data = string.Join("", Enumerable.Repeat("Test", 1000)),
            Items = Enumerable.Range(1, 100).Select(i => new { Id = i, Name = $"Item_{i}" }).ToList()
        };

        await cache.SetAsync("large_object", largeObject);
        var largeResult = await cache.GetAsync<dynamic>("large_object");
        largeResult.Should().NotBeNull();

        // Edge Case 4: 동시 무효화 처리
        var keys = Enumerable.Range(1, 50).Select(i => $"concurrent_key_{i}").ToArray();
        var tasks = keys.Select(async key =>
        {
            await cache.SetAsync(key, $"value_{key}");
            await invalidator.TrackCacheKeyAsync("ConcurrentTable", key);
        });

        await Task.WhenAll(tasks);

        // 동시 무효화
        await invalidator.InvalidateAsync("ConcurrentTable");

        // 모든 키가 무효화되었는지 확인
        var remainingKeys = 0;
        foreach (var key in keys)
        {
            var exists = await cache.ExistsAsync(key);
            if (exists) remainingKeys++;
        }

        remainingKeys.Should().Be(0);
    }
}