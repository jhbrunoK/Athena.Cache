using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Diagnostics;
using Athena.Cache.Core.Implementations;
using Athena.Cache.Core.Middleware;
using Athena.Cache.Core.ObjectPools;
using FluentAssertions;
using Microsoft.Extensions.ObjectPool;
using System.Diagnostics;

namespace Athena.Cache.Tests.Performance;

/// <summary>
/// Phase 4 고급 최적화 성능 테스트
/// </summary>
public class Phase4OptimizationTests
{
    [Fact]
    public async Task ObjectPooling_Performance_Test()
    {
        // Arrange
        const int iterations = 10000;
        var poolProvider = new DefaultObjectPoolProvider();
        var pool = new CachedResponsePool(new TestServiceProvider(poolProvider));

        // 일반 생성 vs Object Pool 성능 비교
        var stopwatch = Stopwatch.StartNew();
        
        // 일반 객체 생성
        for (int i = 0; i < iterations; i++)
        {
            var response = new CachedResponse();
            response.Initialize(200, "application/json", "test content", new(), DateTime.UtcNow.AddMinutes(10));
        }
        
        var regularTime = stopwatch.ElapsedMilliseconds;
        
        // Object Pool 사용
        stopwatch.Restart();
        var pooledObjects = new List<CachedResponse>();
        
        for (int i = 0; i < iterations; i++)
        {
            var response = pool.Get();
            response.Initialize(200, "application/json", "test content", new(), DateTime.UtcNow.AddMinutes(10));
            pooledObjects.Add(response);
        }
        
        // 풀에 반환
        foreach (var obj in pooledObjects)
        {
            pool.Return(obj);
        }
        
        var poolTime = stopwatch.ElapsedMilliseconds;
        
        Console.WriteLine($"일반 생성: {regularTime}ms");
        Console.WriteLine($"Object Pool: {poolTime}ms");
        Console.WriteLine($"성능 향상: {(double)regularTime / poolTime:F1}x");
        
        // Object Pool이 더 빠르거나 비슷해야 함
        poolTime.Should().BeLessThanOrEqualTo((long)(regularTime * 1.5), "Object Pool이 크게 느리지 않아야 함");
    }

    [Fact]
    public async Task ValueTask_vs_Task_Performance()
    {
        // Arrange
        const int iterations = 50000;
        var options = new AthenaCacheOptions { Namespace = "PerfTest" };
        var keyGenerator = new DefaultCacheKeyGenerator(options);
        
        var parameters = new Dictionary<string, object?>
        {
            { "userId", 123 },
            { "category", "test" }
        };

        // Task 버전 (동기 메서드를 Task.FromResult로 래핑한다고 가정)
        var stopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < iterations; i++)
        {
            var key = keyGenerator.GenerateKey("TestController", "TestAction", parameters);
        }
        
        var syncTime = stopwatch.ElapsedMilliseconds;
        
        // ValueTask 버전
        stopwatch.Restart();
        
        for (int i = 0; i < iterations; i++)
        {
            var key = await keyGenerator.GenerateKeyAsync("TestController", "TestAction", parameters);
        }
        
        var valueTaskTime = stopwatch.ElapsedMilliseconds;
        
        Console.WriteLine($"동기 버전: {syncTime}ms");
        Console.WriteLine($"ValueTask 버전: {valueTaskTime}ms");
        Console.WriteLine($"비교: {(double)syncTime / valueTaskTime:F1}x");
        
        // ValueTask 버전이 더 빠르거나 비슷해야 함
        valueTaskTime.Should().BeLessThanOrEqualTo((long)(syncTime * 1.2), "ValueTask가 크게 느리지 않아야 함");
    }

    [Fact]
    public async Task ConcurrentDictionary_Optimization_Test()
    {
        // Arrange
        const int iterations = 100000;
        const int concurrency = 10;
        var options = new AthenaCacheOptions { Namespace = "ConcurrencyTest" };
        
        // 최적화된 키 생성기
        var optimizedGenerator = new DefaultCacheKeyGenerator(options);
        
        // 병렬 처리 테스트
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task>();
        
        for (int t = 0; t < concurrency; t++)
        {
            var taskIndex = t;
            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < iterations / concurrency; i++)
                {
                    var parameters = new Dictionary<string, object?>
                    {
                        { "threadId", taskIndex },
                        { "iteration", i },
                        { "data", $"test-data-{i}" }
                    };
                    
                    var key = await optimizedGenerator.GenerateKeyAsync("TestController", "TestAction", parameters);
                }
            }));
        }
        
        await Task.WhenAll(tasks);
        var elapsedTime = stopwatch.ElapsedMilliseconds;
        
        Console.WriteLine($"병렬 키 생성 ({concurrency} 스레드, {iterations} 회): {elapsedTime}ms");
        Console.WriteLine($"평균 키 생성 시간: {(double)elapsedTime / iterations:F4}ms");
        
        // 성능 목표: CI 환경을 고려하여 현실적인 성능 기준 적용
        var avgTime = (double)elapsedTime / iterations;
        avgTime.Should().BeLessThan(0.05, "병렬 환경에서도 키 생성이 합리적인 시간 내에 완료되어야 함");
    }

    [Fact]
    public void PerformanceMonitor_Overhead_Test()
    {
        // Arrange
        const int iterations = 100000;
        using var monitor = new CachePerformanceMonitor();
        
        // 모니터링 없이 실행
        var stopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < iterations; i++)
        {
            // 간단한 작업 시뮬레이션
            Thread.SpinWait(100);
        }
        
        var withoutMonitoringTime = stopwatch.ElapsedMilliseconds;
        
        // 모니터링과 함께 실행
        stopwatch.Restart();
        
        for (int i = 0; i < iterations; i++)
        {
            using var measurement = monitor.StartMeasurement("test_operation");
            Thread.SpinWait(100);
        }
        
        var withMonitoringTime = stopwatch.ElapsedMilliseconds;
        
        Console.WriteLine($"모니터링 없음: {withoutMonitoringTime}ms");
        Console.WriteLine($"모니터링 포함: {withMonitoringTime}ms");
        Console.WriteLine($"오버헤드: {((double)withMonitoringTime / withoutMonitoringTime - 1) * 100:F1}%");
        
        // 모니터링 오버헤드가 20% 미만이어야 함
        var overhead = (double)withMonitoringTime / withoutMonitoringTime;
        overhead.Should().BeLessThan(1.2, "성능 모니터링 오버헤드가 20% 미만이어야 함");
    }
}

/// <summary>
/// 테스트용 서비스 프로바이더
/// </summary>
internal class TestServiceProvider(ObjectPoolProvider poolProvider) : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(ObjectPoolProvider))
            return poolProvider;
            
        return null;
    }
}