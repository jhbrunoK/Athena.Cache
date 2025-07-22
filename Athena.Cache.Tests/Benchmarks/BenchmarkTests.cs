using System.Diagnostics;
using FluentAssertions;

namespace Athena.Cache.Tests.Benchmarks;

/// <summary>
/// 벤치마크 테스트 (성능 비교용)
/// </summary>
public class BenchmarkTests
{
    [Fact]
    public async Task CompareKeyGenerationPerformance()
    {
        // 여러 키 생성 방식의 성능 비교
        var iterations = 10000;
        var parameters = new Dictionary<string, object?>
        {
            { "userId", 12345 },
            { "category", "electronics" },
            { "price", 99.99m },
            { "inStock", true }
        };

        // 방법 1: SHA256 해싱 (현재 구현)
        var athenaOptions = new Core.Configuration.AthenaCacheOptions { Namespace = "Bench" };
        var keyGenerator = new Core.Implementations.DefaultCacheKeyGenerator(athenaOptions);

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            keyGenerator.GenerateKey("TestController", "TestAction", parameters);
        }
        var sha256Time = stopwatch.ElapsedMilliseconds;

        // 방법 2: 단순 문자열 연결 (비교군)
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var simpleKey = $"Bench_TestController_TestAction_{string.Join("_", parameters.Select(p => $"{p.Key}:{p.Value}"))}";
        }
        var simpleTime = stopwatch.ElapsedMilliseconds;

        // 결과 출력 (실제 테스트에서는 로깅으로 대체)
        Console.WriteLine($"SHA256 방식: {sha256Time}ms, 단순 방식: {simpleTime}ms");

        // SHA256 방식이 합리적인 성능을 보이는지 확인
        sha256Time.Should().BeLessThan(iterations); // 평균 1ms/1000회 미만
    }

    [Fact]
    public async Task CompareCacheProviderPerformance()
    {
        // MemoryCache vs InMemoryTestCache 성능 비교
        const int operations = 1000;

        // MemoryCache 테스트
        var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

        var athenaOptions = new Core.Configuration.AthenaCacheOptions();
        var logger = new Moq.Mock<Microsoft.Extensions.Logging.ILogger<Core.Implementations.MemoryCacheProvider>>();
        var memoryCacheProvider = new Core.Implementations.MemoryCacheProvider(memoryCache, athenaOptions, logger.Object);

        var stopwatch = Stopwatch.StartNew();

        // 쓰기 성능
        for (int i = 0; i < operations; i++)
        {
            await memoryCacheProvider.SetAsync($"key_{i}", $"value_{i}");
        }
        var memoryWriteTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();

        // 읽기 성능  
        for (int i = 0; i < operations; i++)
        {
            await memoryCacheProvider.GetAsync<string>($"key_{i}");
        }
        var memoryReadTime = stopwatch.ElapsedMilliseconds;

        // InMemoryTestCache 테스트
        var testCache = new Mocks.InMemoryTestCache();

        stopwatch.Restart();
        for (int i = 0; i < operations; i++)
        {
            await testCache.SetAsync($"key_{i}", $"value_{i}");
        }
        var testWriteTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        for (int i = 0; i < operations; i++)
        {
            await testCache.GetAsync<string>($"key_{i}");
        }
        var testReadTime = stopwatch.ElapsedMilliseconds;

        // 결과 비교
        Console.WriteLine($"MemoryCache - 쓰기: {memoryWriteTime}ms, 읽기: {memoryReadTime}ms");
        Console.WriteLine($"TestCache - 쓰기: {testWriteTime}ms, 읽기: {testReadTime}ms");

        // 둘 다 합리적인 성능을 보여야 함
        memoryWriteTime.Should().BeLessThan(1000);
        memoryReadTime.Should().BeLessThan(500);
        testWriteTime.Should().BeLessThan(1000);
        testReadTime.Should().BeLessThan(500);
    }
}