using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Implementations;
using Athena.Cache.Redis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using System.Diagnostics;

namespace Athena.Cache.Tests.Performance;

/// <summary>
/// Phase 5 엔터프라이즈 기능 성능 테스트
/// </summary>
public class Phase5EnterpriseTests
{
    [Fact]
    public async Task DistributedCacheInvalidation_Performance_Test()
    {
        // Arrange
        const int iterations = 1000;
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDatabase = new Mock<IDatabase>();
        var mockSubscriber = new Mock<ISubscriber>();
        var mockLocalInvalidator = new Mock<ICacheInvalidator>();
        var options = new AthenaCacheOptions { Namespace = "PerfTest" };
        var logger = new Mock<ILogger<DistributedCacheInvalidator>>();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);
        mockRedis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(mockSubscriber.Object);
        mockRedis.Setup(r => r.IsConnected).Returns(true);

        var distributedInvalidator = new DistributedCacheInvalidator(
            mockRedis.Object, mockLocalInvalidator.Object, options, logger.Object);

        // Act - 분산 무효화 성능 측정
        var stopwatch = Stopwatch.StartNew();
        
        var tasks = new List<Task>();
        for (int i = 0; i < iterations; i++)
        {
            tasks.Add(distributedInvalidator.BroadcastInvalidationAsync($"Table_{i % 10}"));
        }
        
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var avgTime = (double)stopwatch.ElapsedMilliseconds / iterations;
        Console.WriteLine($"분산 무효화 평균 시간: {avgTime:F4}ms");
        Console.WriteLine($"초당 무효화 처리량: {1000.0 / avgTime:F0} ops/sec");
        
        // 성능 목표: 평균 1ms 미만
        avgTime.Should().BeLessThan(1.0, "분산 무효화가 1ms 미만이어야 함");
        
        // Redis 호출 확인
        mockSubscriber.Verify(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()), 
            Times.Exactly(iterations));
    }

    [Fact]
    public async Task IntelligentCacheManager_HotKeyDetection_Performance()
    {
        // Arrange
        const int iterations = 10000;
        const int uniqueKeys = 100;
        var options = new AthenaCacheOptions { DefaultExpirationMinutes = 30 };
        var logger = new Mock<ILogger<IntelligentCacheManager>>();
        var cacheManager = new IntelligentCacheManager(options, logger.Object);

        await cacheManager.StartHotKeyDetectionAsync();

        // Act - Hot Key 감지를 위한 액세스 시뮬레이션
        var stopwatch = Stopwatch.StartNew();
        var random = new Random(42); // 결정적 시드

        var tasks = new List<Task>();
        for (int i = 0; i < iterations; i++)
        {
            var keyIndex = random.NextDouble() < 0.2 ? i % 10 : i % uniqueKeys; // 20%는 Hot Key (0-9)
            var cacheKey = $"key_{keyIndex}";
            var accessType = random.NextDouble() < 0.8 ? CacheAccessType.Hit : CacheAccessType.Miss;
            
            tasks.Add(cacheManager.RecordCacheAccessAsync(cacheKey, accessType));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Hot Key 조회 성능 측정
        var hotKeyStopwatch = Stopwatch.StartNew();
        var hotKeys = await cacheManager.GetHotKeysAsync(10);
        hotKeyStopwatch.Stop();

        // Assert
        var recordTime = (double)stopwatch.ElapsedMilliseconds / iterations;
        Console.WriteLine($"캐시 액세스 기록 평균 시간: {recordTime:F4}ms");
        Console.WriteLine($"Hot Key 조회 시간: {hotKeyStopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"감지된 Hot Key 수: {hotKeys.Count()}");

        // 성능 목표
        recordTime.Should().BeLessThan(0.01, "캐시 액세스 기록이 0.01ms 미만이어야 함");
        hotKeyStopwatch.ElapsedMilliseconds.Should().BeLessThan(50, "Hot Key 조회가 50ms 미만이어야 함");
        hotKeys.Should().NotBeEmpty("Hot Key가 감지되어야 함");

        await cacheManager.StopHotKeyDetectionAsync();
        cacheManager.Dispose();
    }

    [Fact]
    public async Task AdaptiveTTL_Calculation_Performance()
    {
        // Arrange
        const int iterations = 5000;
        var options = new AthenaCacheOptions { DefaultExpirationMinutes = 30 };
        var logger = new Mock<ILogger<IntelligentCacheManager>>();
        var cacheManager = new IntelligentCacheManager(options, logger.Object);

        // 다양한 액세스 패턴으로 키 준비
        var testKeys = new[] { "hot_key", "warm_key", "cold_key", "new_key" };
        
        // Hot Key 시뮬레이션 (높은 액세스 빈도)
        for (int i = 0; i < 100; i++)
        {
            await cacheManager.RecordCacheAccessAsync("hot_key", CacheAccessType.Hit);
        }
        
        // Warm Key 시뮬레이션 (중간 액세스 빈도)
        for (int i = 0; i < 20; i++)
        {
            await cacheManager.RecordCacheAccessAsync("warm_key", CacheAccessType.Hit);
        }
        
        // Cold Key 시뮬레이션 (낮은 액세스 빈도)
        for (int i = 0; i < 3; i++)
        {
            await cacheManager.RecordCacheAccessAsync("cold_key", CacheAccessType.Miss);
        }

        // Act - Adaptive TTL 계산 성능 측정
        var stopwatch = Stopwatch.StartNew();
        
        var ttlTasks = new List<Task<TimeSpan>>();
        for (int i = 0; i < iterations; i++)
        {
            var key = testKeys[i % testKeys.Length];
            ttlTasks.Add(cacheManager.CalculateAdaptiveTtlAsync(key));
        }
        
        var results = await Task.WhenAll(ttlTasks);
        stopwatch.Stop();

        // Assert
        var avgTime = (double)stopwatch.ElapsedMilliseconds / iterations;
        Console.WriteLine($"Adaptive TTL 계산 평균 시간: {avgTime:F4}ms");
        
        // TTL 결과 분석
        var hotKeyTtl = results.Where((_, i) => testKeys[i % testKeys.Length] == "hot_key").First();
        var coldKeyTtl = results.Where((_, i) => testKeys[i % testKeys.Length] == "cold_key").First();
        var newKeyTtl = results.Where((_, i) => testKeys[i % testKeys.Length] == "new_key").First();
        
        Console.WriteLine($"Hot Key TTL: {hotKeyTtl.TotalMinutes:F1}분");
        Console.WriteLine($"Cold Key TTL: {coldKeyTtl.TotalMinutes:F1}분");
        Console.WriteLine($"New Key TTL: {newKeyTtl.TotalMinutes:F1}분");

        // 성능 및 로직 검증
        avgTime.Should().BeLessThan(0.1, "Adaptive TTL 계산이 0.1ms 미만이어야 함");
        hotKeyTtl.Should().BeGreaterThan(newKeyTtl, "Hot Key가 더 긴 TTL을 가져야 함");
        
        cacheManager.Dispose();
    }

    [Fact]
    public async Task CacheEviction_Policy_Performance()
    {
        // Arrange
        const int keyCount = 1000;
        const int evictionCount = 100;
        var options = new AthenaCacheOptions { DefaultExpirationMinutes = 30 };
        var logger = new Mock<ILogger<IntelligentCacheManager>>();
        var cacheManager = new IntelligentCacheManager(options, logger.Object);

        // 테스트 키들 생성 및 액세스 패턴 시뮬레이션
        var random = new Random(42);
        for (int i = 0; i < keyCount; i++)
        {
            var key = $"eviction_test_key_{i}";
            var accessCount = random.Next(1, 50);
            
            for (int j = 0; j < accessCount; j++)
            {
                await cacheManager.RecordCacheAccessAsync(key, CacheAccessType.Hit);
                await Task.Delay(1); // 시간 간격 시뮬레이션
            }
        }

        // Act - 각 교체 정책별 성능 측정
        var policies = new[] 
        { 
            CacheEvictionPolicy.LRU, 
            CacheEvictionPolicy.LFU, 
            CacheEvictionPolicy.Random,
            CacheEvictionPolicy.FIFO
        };

        foreach (var policy in policies)
        {
            var stopwatch = Stopwatch.StartNew();
            await cacheManager.EvictCacheByPolicyAsync(policy, evictionCount);
            stopwatch.Stop();

            Console.WriteLine($"{policy} 정책 교체 시간: {stopwatch.ElapsedMilliseconds}ms");
            
            // 성능 목표: 100ms 미만
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(100, $"{policy} 정책이 100ms 미만이어야 함");
        }

        cacheManager.Dispose();
    }

    [Fact]
    public async Task CacheWarming_Performance_Test()
    {
        // Arrange
        const int warmingKeyCount = 500;
        var options = new AthenaCacheOptions { DefaultExpirationMinutes = 30 };
        var logger = new Mock<ILogger<IntelligentCacheManager>>();
        var cacheManager = new IntelligentCacheManager(options, logger.Object);

        var keysToWarm = Enumerable.Range(0, warmingKeyCount)
            .Select(i => $"warm_key_{i}")
            .ToList();

        // Act - 캐시 워밍 성능 측정
        var stopwatch = Stopwatch.StartNew();
        await cacheManager.WarmCacheAsync(keysToWarm);
        stopwatch.Stop();

        // Assert
        var avgTime = (double)stopwatch.ElapsedMilliseconds / warmingKeyCount;
        Console.WriteLine($"캐시 워밍 총 시간: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"키당 평균 워밍 시간: {avgTime:F4}ms");
        Console.WriteLine($"초당 워밍 처리량: {warmingKeyCount * 1000.0 / stopwatch.ElapsedMilliseconds:F0} keys/sec");

        // 성능 목표
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "캐시 워밍이 5초 미만이어야 함");
        avgTime.Should().BeLessThan(10.0, "키당 워밍 시간이 10ms 미만이어야 함");

        cacheManager.Dispose();
    }

    [Fact]
    public async Task ConcurrentAccess_Performance_Test()
    {
        // Arrange
        const int concurrentUsers = 50;
        const int operationsPerUser = 100;
        var options = new AthenaCacheOptions { DefaultExpirationMinutes = 30 };
        var logger = new Mock<ILogger<IntelligentCacheManager>>();
        var cacheManager = new IntelligentCacheManager(options, logger.Object);

        await cacheManager.StartHotKeyDetectionAsync();

        // Act - 동시 사용자 시뮬레이션
        var stopwatch = Stopwatch.StartNew();
        
        var userTasks = Enumerable.Range(0, concurrentUsers).Select(async userId =>
        {
            for (int i = 0; i < operationsPerUser; i++)
            {
                var key = $"user_{userId}_key_{i % 10}"; // 각 사용자당 10개 키 순환
                var accessType = i % 4 == 0 ? CacheAccessType.Miss : CacheAccessType.Hit;
                
                await cacheManager.RecordCacheAccessAsync(key, accessType);
                
                if (i % 20 == 0) // 가끔 TTL 계산
                {
                    await cacheManager.CalculateAdaptiveTtlAsync(key);
                }
                
                if (i % 30 == 0) // 가끔 우선순위 계산
                {
                    await cacheManager.CalculateKeyPriorityAsync(key);
                }
            }
        });

        await Task.WhenAll(userTasks);
        stopwatch.Stop();

        // Hot Key 조회로 최종 검증
        var hotKeys = await cacheManager.GetHotKeysAsync(20);

        // Assert
        var totalOps = concurrentUsers * operationsPerUser;
        var opsPerSecond = totalOps * 1000.0 / stopwatch.ElapsedMilliseconds;
        
        Console.WriteLine($"동시 접근 테스트 완료 시간: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"총 연산 수: {totalOps}");
        Console.WriteLine($"초당 처리량: {opsPerSecond:F0} ops/sec");
        Console.WriteLine($"검출된 Hot Key 수: {hotKeys.Count()}");

        // 성능 목표
        opsPerSecond.Should().BeGreaterThan(10000, "초당 10,000 연산 이상 처리해야 함");
        hotKeys.Should().NotBeEmpty("동시 접근으로 Hot Key가 감지되어야 함");

        await cacheManager.StopHotKeyDetectionAsync();
        cacheManager.Dispose();
    }
}