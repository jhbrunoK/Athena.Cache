using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;

namespace Athena.Cache.Tests.Performance;

public class PerformanceTests
{
    [Fact]
    public async Task MemoryCache_BulkOperations_ShouldBeReasonablyFast()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var mockLogger = new Mock<ILogger<MemoryCacheProvider>>();
        var options = new AthenaCacheOptions { DefaultExpirationMinutes = 30 };
        var cacheProvider = new MemoryCacheProvider(memoryCache, options, mockLogger.Object);

        const int itemCount = 1000;
        var items = Enumerable.Range(1, itemCount)
            .ToDictionary(i => $"key_{i}", i => $"value_{i}");

        var stopwatch = Stopwatch.StartNew();

        // Act - 대량 저장
        await cacheProvider.SetManyAsync(items);

        var setTime = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();

        // Act - 대량 조회
        var results = await cacheProvider.GetManyAsync<string>(items.Keys);

        var getTime = stopwatch.ElapsedMilliseconds;

        // Assert
        setTime.Should().BeLessThan(1000); // 1초 미만
        getTime.Should().BeLessThan(500);  // 0.5초 미만
        results.Should().HaveCount(itemCount);

        var hitCount = results.Values.Count(v => v != null);
        hitCount.Should().Be(itemCount);
    }

    [Fact]
    public async Task CacheKeyGeneration_ShouldBeEfficient()
    {
        // Arrange
        var options = new AthenaCacheOptions
        {
            Namespace = "PerfTest",
            VersionKey = "v1.0"
        };
        var keyGenerator = new DefaultCacheKeyGenerator(options);

        const int iterations = 10000;
        var parameters = new Dictionary<string, object?>
        {
            { "userId", 123 },
            { "category", "electronics" },
            { "sortBy", "price" },
            { "page", 1 },
            { "pageSize", 20 },
            { "includeTax", true }
        };

        var stopwatch = Stopwatch.StartNew();

        // Act
        for (var i = 0; i < iterations; i++)
        {
            keyGenerator.GenerateKey("ProductsController", "GetProducts", parameters);
        }

        stopwatch.Stop();

        // Assert
        var avgTimePerKey = (double)stopwatch.ElapsedMilliseconds / iterations;
        avgTimePerKey.Should().BeLessThan(0.1); // 평균 0.1ms 미만
    }
}