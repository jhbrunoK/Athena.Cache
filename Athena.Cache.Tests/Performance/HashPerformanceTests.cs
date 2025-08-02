using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Implementations;
using FluentAssertions;
using System.Diagnostics;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;

namespace Athena.Cache.Tests.Performance;

/// <summary>
/// SHA256 vs XxHash3 성능 비교 테스트
/// </summary>
public class HashPerformanceTests
{
    [Fact]
    public async Task XxHash3_vs_SHA256_HashingPerformance()
    {
        // Arrange
        const int iterations = 10000;
        var testInputs = new[]
        {
            "simple string",
            """{"userId":123,"category":"electronics","sortBy":"price","page":1,"pageSize":20,"includeTax":true}""",
            "Very long string with lots of content that should test the performance of hash functions with larger inputs to see how they scale with data size",
            new string('A', 1000), // 1KB 문자열
            new string('B', 10000)  // 10KB 문자열
        };

        foreach (var input in testInputs)
        {
            Console.WriteLine($"\n=== 입력 크기: {input.Length} bytes ===");
            
            // XxHash3 성능 측정
            var stopwatch = Stopwatch.StartNew();
            ulong xxHashResult = 0;
            
            for (int i = 0; i < iterations; i++)
            {
                var inputBytes = Encoding.UTF8.GetBytes(input);
                xxHashResult = XxHash3.HashToUInt64(inputBytes);
            }
            
            var xxHashTime = stopwatch.ElapsedMilliseconds;
            
            // SHA256 성능 측정
            stopwatch.Restart();
            byte[] sha256Result = null!;
            
            for (int i = 0; i < iterations; i++)
            {
                var inputBytes = Encoding.UTF8.GetBytes(input);
                sha256Result = SHA256.HashData(inputBytes);
            }
            
            var sha256Time = stopwatch.ElapsedMilliseconds;

            // 결과 출력
            Console.WriteLine($"XxHash3: {xxHashTime}ms (결과: {xxHashResult})");
            Console.WriteLine($"SHA256: {sha256Time}ms (결과: {Convert.ToHexString(sha256Result)[..16]}...)");
            Console.WriteLine($"성능 향상: {(double)sha256Time / xxHashTime:F1}x 빠름");
            
            // 성능 검증 - CI 환경을 고려하여 유연한 성능 검증
            // XxHash3는 일반적으로 빠르지만 환경에 따라 차이가 있을 수 있음
            if (input.Length > 1000) // 아주 큰 데이터에서만 엄격한 성능 검증
            {
                xxHashTime.Should().BeLessThanOrEqualTo((long)(sha256Time * 1.5), "XxHash3가 SHA256보다 1.5배 이상 느리지 않아야 함 (큰 데이터)");
            }
            else
            {
                xxHashTime.Should().BeLessThanOrEqualTo(sha256Time * 3, "XxHash3가 SHA256보다 3배 이상 느리지 않아야 함 (작은 데이터)");
            }
            
            // 결과가 다름을 확인 (해시 알고리즘이 실제로 작동하는지)
            xxHashResult.Should().NotBe(0, "XxHash3 결과가 0이 아니어야 함");
            sha256Result.Should().NotBeEmpty("SHA256 결과가 비어있지 않아야 함");
        }
    }

    [Fact]
    public async Task CacheKeyGenerator_Performance_Comparison()
    {
        // Arrange
        const int iterations = 5000;
        var options = new AthenaCacheOptions
        {
            Namespace = "PerfTest",
            VersionKey = "v2.0"
        };
        
        // XxHash3 기반 키 생성기 (새 구현)
        var newKeyGenerator = new DefaultCacheKeyGenerator(options);
        
        var testParameters = new Dictionary<string, object?>[]
        {
            new() { { "userId", 123 }, { "category", "electronics" } },
            new() { { "userId", 456 }, { "category", "books" }, { "sortBy", "price" }, { "page", 1 } },
            new() { { "search", "laptop computer" }, { "minPrice", 500.0 }, { "maxPrice", 2000.0 }, { "inStock", true } },
            new() { { "filters", new[] { "brand", "rating", "availability" } }, { "limit", 50 } }
        };

        var totalNewTime = 0L;
        var totalCacheHits = 0;

        foreach (var parameters in testParameters)
        {
            Console.WriteLine($"\n=== 파라미터 수: {parameters.Count} ===");
            
            // 새 키 생성기 성능 측정 (키 캐싱 포함)
            var stopwatch = Stopwatch.StartNew();
            var generatedKeys = new HashSet<string>();
            
            for (int i = 0; i < iterations; i++)
            {
                var key = newKeyGenerator.GenerateKey("TestController", "TestAction", parameters);
                generatedKeys.Add(key);
            }
            
            var newTime = stopwatch.ElapsedMilliseconds;
            totalNewTime += newTime;
            
            // 키 캐싱이 동작하는지 확인 (같은 파라미터로 여러 번 호출해도 같은 키)
            generatedKeys.Should().HaveCount(1, "같은 파라미터는 같은 키를 생성해야 함");
            
            // 결과 출력
            Console.WriteLine($"XxHash3 + 캐싱: {newTime}ms");
            Console.WriteLine($"평균 시간/키: {(double)newTime / iterations:F4}ms");
            Console.WriteLine($"생성된 키: {generatedKeys.First()}");
        }

        // 전체 성능 목표 검증
        var avgTimePerKey = (double)totalNewTime / (iterations * testParameters.Length);
        Console.WriteLine($"\n=== 전체 성능 결과 ===");
        Console.WriteLine($"총 시간: {totalNewTime}ms");
        Console.WriteLine($"평균 키 생성 시간: {avgTimePerKey:F4}ms");
        
        // 성능 목표: 평균 0.1ms 미만 (현실적인 목표, 키 캐싱 효과로 실제로는 더 빠름)
        avgTimePerKey.Should().BeLessThan(0.1, "키 생성이 평균 0.1ms 미만이어야 함");
    }

    [Fact]
    public async Task KeyCaching_Effectiveness_Test()
    {
        // Arrange
        var options = new AthenaCacheOptions { Namespace = "CacheTest" };
        var keyGenerator = new DefaultCacheKeyGenerator(options);
        
        var parameters = new Dictionary<string, object?>
        {
            { "userId", 123 },
            { "category", "electronics" },
            { "page", 1 }
        };

        // 첫 번째 호출 (캐시 미스)
        var stopwatch = Stopwatch.StartNew();
        var key1 = keyGenerator.GenerateKey("TestController", "TestAction", parameters);
        var firstCallTime = stopwatch.ElapsedTicks;

        // 두 번째 호출 (캐시 히트)
        stopwatch.Restart();
        var key2 = keyGenerator.GenerateKey("TestController", "TestAction", parameters);
        var secondCallTime = stopwatch.ElapsedTicks;

        // 결과 검증
        key1.Should().Be(key2, "같은 파라미터는 같은 키를 반환해야 함");
        secondCallTime.Should().BeLessThan(firstCallTime, "캐시된 키 조회가 더 빨라야 함");
        
        Console.WriteLine($"첫 번째 호출: {firstCallTime} ticks");
        Console.WriteLine($"두 번째 호출: {secondCallTime} ticks");
        Console.WriteLine($"캐시 효과: {(double)firstCallTime / secondCallTime:F1}x 빠름");
        Console.WriteLine($"생성된 키: {key1}");
    }

    [Fact]
    public async Task Base36_Encoding_Performance()
    {
        // Arrange
        const int iterations = 10000;
        var testValues = new ulong[]
        {
            0UL,
            123456789UL,
            ulong.MaxValue / 2,
            ulong.MaxValue
        };

        foreach (var value in testValues)
        {
            var stopwatch = Stopwatch.StartNew();
            string result = null!;
            
            for (int i = 0; i < iterations; i++)
            {
                result = ConvertToBase36(value);
            }
            
            var elapsed = stopwatch.ElapsedMilliseconds;
            
            Console.WriteLine($"값: {value} → Base36: {result} ({elapsed}ms, {iterations} 회)");
            
            // 성능 목표: 50ms 미만 (현실적인 목표)
            elapsed.Should().BeLessThan(50, "Base36 인코딩이 빨라야 함");
        }
    }

    /// <summary>
    /// UInt64를 Base36 문자열로 변환 (테스트용 복사)
    /// </summary>
    private static string ConvertToBase36(ulong value)
    {
        const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value == 0) return "0";

        var result = new StringBuilder();
        while (value > 0)
        {
            result.Insert(0, chars[(int)(value % 36)]);
            value /= 36;
        }
        
        return result.ToString();
    }
}