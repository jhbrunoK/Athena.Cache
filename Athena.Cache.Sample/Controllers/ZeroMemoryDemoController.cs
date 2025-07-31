using Athena.Cache.Core.Analytics;
using Athena.Cache.Core.Memory;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Athena.Cache.Sample.Controllers;

/// <summary>
/// 제로 메모리 할당 최적화 데모 컨트롤러
/// 실제 성능 향상을 확인할 수 있는 API들을 제공
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ZeroMemoryDemoController : ControllerBase
{
    private readonly MemoryPressureManager _memoryManager;
    private readonly ILogger<ZeroMemoryDemoController> _logger;

    public ZeroMemoryDemoController(
        MemoryPressureManager memoryManager,
        ILogger<ZeroMemoryDemoController> logger)
    {
        _memoryManager = memoryManager;
        _logger = logger;
    }

    /// <summary>
    /// 메모리 상태 및 캐시 통계 조회
    /// </summary>
    [HttpGet("memory-status")]
    public ActionResult<object> GetMemoryStatus()
    {
        var memoryStatus = _memoryManager.GetMemoryStatus();
        var cacheStats = LazyCache.GetCacheStats();
        var poolStats = HighPerformanceStringPool.GetPoolStats();

        return Ok(new
        {
            timestamp = DateTime.UtcNow,
            memory = new
            {
                totalBytes = memoryStatus.TotalMemoryBytes,
                formattedSize = LazyCache.FormatByteSize(memoryStatus.TotalMemoryBytes),
                pressureLevel = memoryStatus.PressureLevel.ToString(),
                lastCleanup = memoryStatus.LastCleanupTime
            },
            gc = new
            {
                gen0Collections = memoryStatus.GcStatistics.Gen0Collections,
                gen1Collections = memoryStatus.GcStatistics.Gen1Collections,
                gen2Collections = memoryStatus.GcStatistics.Gen2Collections,
                gen0Frequency = memoryStatus.GcStatistics.Gen0Frequency,
                gen1Frequency = memoryStatus.GcStatistics.Gen1Frequency,
                gen2Frequency = memoryStatus.GcStatistics.Gen2Frequency
            },
            cache = new
            {
                totalCacheSize = cacheStats.TotalCacheSize,
                stringCache = cacheStats.StringCacheSize,
                intCache = cacheStats.IntCacheSize,
                percentageCache = cacheStats.PercentageCacheSize,
                byteSizeCache = cacheStats.ByteSizeCacheSize
            },
            stringPool = new
            {
                poolCount = poolStats.StringBuilderPoolCount,
                internCount = poolStats.InternPoolCount,
                templateCount = poolStats.TemplateCacheCount,
                estimatedMemory = poolStats.EstimatedMemoryUsage,
                formattedMemory = LazyCache.FormatByteSize(poolStats.EstimatedMemoryUsage)
            }
        });
    }

    /// <summary>
    /// 기존 방식 vs 최적화된 방식 성능 비교 테스트
    /// </summary>
    [HttpPost("performance-comparison")]
    public ActionResult<object> PerformanceComparison([FromQuery] int iterations = 10000)
    {
        var results = new Dictionary<string, object>();

        // 1. 문자열 포맷팅 비교
        results["stringFormatting"] = CompareStringFormatting(iterations);

        // 2. 컬렉션 생성 비교
        results["collectionCreation"] = CompareCollectionCreation(iterations);

        // 3. 통계 계산 비교
        results["statisticsCalculation"] = CompareStatisticsCalculation(iterations);

        // 4. 메모리 사용량 비교
        results["memoryUsage"] = CompareMemoryUsage(iterations);

        return Ok(results);
    }

    /// <summary>
    /// 메모리 압박 시뮬레이션 및 자동 정리 테스트
    /// </summary>
    [HttpPost("memory-pressure-test")]
    public ActionResult<object> MemoryPressureTest()
    {
        var initialMemory = GC.GetTotalMemory(false);
        var initialStats = _memoryManager.GetMemoryStatus();

        // 대량의 메모리 할당으로 의도적 압박 생성
        var memoryHogs = new List<byte[]>();
        for (int i = 0; i < 100; i++)
        {
            memoryHogs.Add(new byte[1024 * 1024]); // 1MB씩 할당
        }

        var peakMemory = GC.GetTotalMemory(false);

        // 강제 정리 수행
        _memoryManager.ForceCleanup(MemoryPressureLevel.High);

        // 할당된 메모리 해제
        memoryHogs.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(true);
        var finalStats = _memoryManager.GetMemoryStatus();

        return Ok(new
        {
            test = "Memory pressure simulation",
            initialMemory = LazyCache.FormatByteSize(initialMemory),
            peakMemory = LazyCache.FormatByteSize(peakMemory),
            finalMemory = LazyCache.FormatByteSize(finalMemory),
            memoryFreed = LazyCache.FormatByteSize(peakMemory - finalMemory),
            initialPressure = initialStats.PressureLevel.ToString(),
            finalPressure = finalStats.PressureLevel.ToString(),
            gcImprovement = new
            {
                gen0Reduced = Math.Max(0, initialStats.GcStatistics.Gen0Frequency - finalStats.GcStatistics.Gen0Frequency),
                gen1Reduced = Math.Max(0, initialStats.GcStatistics.Gen1Frequency - finalStats.GcStatistics.Gen1Frequency),
                gen2Reduced = Math.Max(0, initialStats.GcStatistics.Gen2Frequency - finalStats.GcStatistics.Gen2Frequency)
            }
        });
    }

    /// <summary>
    /// 캐시 정리 수행
    /// </summary>
    [HttpPost("clear-caches")]
    public ActionResult<object> ClearCaches()
    {
        var beforeStats = LazyCache.GetCacheStats();
        var beforePoolStats = HighPerformanceStringPool.GetPoolStats();

        LazyCache.ClearCaches();
        HighPerformanceStringPool.ClearAllPools();

        var afterStats = LazyCache.GetCacheStats();
        var afterPoolStats = HighPerformanceStringPool.GetPoolStats();

        return Ok(new
        {
            message = "All caches and pools cleared",
            before = new
            {
                cacheSize = beforeStats.TotalCacheSize,
                poolMemory = beforePoolStats.EstimatedMemoryUsage
            },
            after = new
            {
                cacheSize = afterStats.TotalCacheSize,
                poolMemory = afterPoolStats.EstimatedMemoryUsage
            },
            freed = new
            {
                cacheItems = beforeStats.TotalCacheSize - afterStats.TotalCacheSize,
                memoryBytes = beforePoolStats.EstimatedMemoryUsage - afterPoolStats.EstimatedMemoryUsage
            }
        });
    }

    private object CompareStringFormatting(int iterations)
    {
        var stopwatch = Stopwatch.StartNew();

        // 기존 방식
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var ratio = i / (double)iterations;
            var result = $"{ratio:P1}"; // 표준 포맷팅
        }
        var oldWayTime = stopwatch.ElapsedMilliseconds;

        // 최적화된 방식
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var ratio = i / (double)iterations;
            var result = LazyCache.FormatPercentage(ratio); // 캐싱된 포맷팅
        }
        var newWayTime = stopwatch.ElapsedMilliseconds;

        return new
        {
            test = "String formatting",
            iterations,
            oldWayMs = oldWayTime,
            newWayMs = newWayTime,
            improvement = oldWayTime > 0 ? $"{(double)(oldWayTime - newWayTime) / oldWayTime * 100:F1}%" : "N/A"
        };
    }

    private object CompareCollectionCreation(int iterations)
    {
        var stopwatch = Stopwatch.StartNew();

        // 기존 방식
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var list = new List<string>(); // 매번 새로 생성
            list.Add("test");
            list.Clear();
        }
        var oldWayTime = stopwatch.ElapsedMilliseconds;

        // 최적화된 방식
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var list = CollectionPools.RentStringList(); // 풀에서 재사용
            try
            {
                list.Add("test");
            }
            finally
            {
                CollectionPools.Return(list);
            }
        }
        var newWayTime = stopwatch.ElapsedMilliseconds;

        return new
        {
            test = "Collection creation",
            iterations,
            oldWayMs = oldWayTime,
            newWayMs = newWayTime,
            improvement = oldWayTime > 0 ? $"{(double)(oldWayTime - newWayTime) / oldWayTime * 100:F1}%" : "N/A"
        };
    }

    private object CompareStatisticsCalculation(int iterations)
    {
        var data = Enumerable.Range(1, 1000).Select(x => (double)x).ToArray();
        var stopwatch = Stopwatch.StartNew();

        // 기존 방식 (LINQ)
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var average = data.Average(); // LINQ - 새 열거자 생성
        }
        var oldWayTime = stopwatch.ElapsedMilliseconds;

        // 최적화된 방식 (값 타입)
        stopwatch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            var average = ValueTypeStatistics.CalculateAverage<double>(data); // 값 타입 처리
        }
        var newWayTime = stopwatch.ElapsedMilliseconds;

        return new
        {
            test = "Statistics calculation",
            iterations,
            oldWayMs = oldWayTime,
            newWayMs = newWayTime,
            improvement = oldWayTime > 0 ? $"{(double)(oldWayTime - newWayTime) / oldWayTime * 100:F1}%" : "N/A"
        };
    }

    private object CompareMemoryUsage(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // 기존 방식 메모리 사용량
        var initialMemory = GC.GetTotalMemory(false);
        for (int i = 0; i < iterations; i++)
        {
            var list = new List<string>();
            list.Add($"Item {i}");
            var result = string.Join(",", list);
        }
        var oldWayMemory = GC.GetTotalMemory(true) - initialMemory;

        // 최적화된 방식 메모리 사용량
        initialMemory = GC.GetTotalMemory(false);
        for (int i = 0; i < iterations; i++)
        {
            var list = CollectionPools.RentStringList();
            try
            {
                list.Add(LazyCache.InternString($"Item {i}"));
                var sb = HighPerformanceStringPool.RentStringBuilder();
                try
                {
                    foreach (var item in list)
                    {
                        if (sb.Length > 0) sb.Append(',');
                        sb.Append(item);
                    }
                    var result = sb.ToString();
                }
                finally
                {
                    HighPerformanceStringPool.ReturnStringBuilder(sb);
                }
            }
            finally
            {
                CollectionPools.Return(list);
            }
        }
        var newWayMemory = GC.GetTotalMemory(true) - initialMemory;

        return new
        {
            test = "Memory usage",
            iterations,
            oldWayBytes = Math.Max(0, oldWayMemory),
            newWayBytes = Math.Max(0, newWayMemory),
            oldWayFormatted = LazyCache.FormatByteSize(Math.Max(0, oldWayMemory)),
            newWayFormatted = LazyCache.FormatByteSize(Math.Max(0, newWayMemory)),
            memoryReduction = oldWayMemory > 0 && newWayMemory >= 0 ? 
                $"{(double)(oldWayMemory - newWayMemory) / oldWayMemory * 100:F1}%" : "N/A"
        };
    }
}