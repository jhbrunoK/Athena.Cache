using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Observability;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Athena.Cache.Core.Memory;

namespace Athena.Cache.Core.Analytics;

/// <summary>
/// 캐시 성능 분석 및 최적화 제안 엔진
/// </summary>
public class CacheOptimizationAnalyzer : IDisposable
{
    private readonly IIntelligentCacheManager? _intelligentCacheManager;
    private readonly CacheHealthMonitor _healthMonitor;
    private readonly ILogger<CacheOptimizationAnalyzer> _logger;
    
    private readonly Timer _analysisTimer;
    private readonly ConcurrentQueue<OptimizationRecommendation> _recommendations = new();
    private readonly ConcurrentDictionary<string, PatternAnalysis> _patterns = new();
    
    private volatile bool _disposed = false;

    public CacheOptimizationAnalyzer(
        CacheHealthMonitor healthMonitor,
        ILogger<CacheOptimizationAnalyzer> logger,
        IIntelligentCacheManager? intelligentCacheManager = null)
    {
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _intelligentCacheManager = intelligentCacheManager;

        // 10분마다 분석 실행
        _analysisTimer = new Timer(PerformAnalysis, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));
        
        _logger.LogInformation("CacheOptimizationAnalyzer initialized");
    }

    #region Analysis Methods

    public async Task<OptimizationReport> GenerateOptimizationReportAsync()
    {
        var currentSnapshot = _healthMonitor.GetCurrentSnapshot();
        
        // 성능 기록을 List로 변환하지 않고 직접 사용
        var performanceHistory = _healthMonitor.GetPerformanceHistory(60);
        
        // 컬렉션 풀에서 대여
        var recommendations = CollectionPools.RentOptimizationRecommendationList();
        
        try
        {
            // 1. Hit Rate 분석
            await AnalyzeHitRateAsync(currentSnapshot, performanceHistory, recommendations);
            
            // 2. Memory Usage 분석  
            AnalyzeMemoryUsage(currentSnapshot, performanceHistory, recommendations);
            
            // 3. Hot Key 분석
            if (_intelligentCacheManager != null)
            {
                await AnalyzeHotKeysAsync(recommendations);
            }
            
            // 4. Performance Trend 분석
            AnalyzePerformanceTrends(performanceHistory, recommendations);
            
            // 5. Error Pattern 분석
            AnalyzeErrorPatterns(performanceHistory, recommendations);

            return new OptimizationReport
            {
                GeneratedAt = DateTime.UtcNow,
                CurrentSnapshot = currentSnapshot,
                Recommendations = recommendations.OrderByDescending(r => r.Priority).ToArray(),
                Summary = GenerateReportSummary(recommendations, currentSnapshot)
            };
        }
        finally
        {
            // 풀로 반환
            CollectionPools.Return(recommendations);
        }
    }

    private async Task AnalyzeHitRateAsync(
        CachePerformanceSnapshot snapshot, 
        IEnumerable<CachePerformanceSnapshot> history,
        List<OptimizationRecommendation> recommendations)
    {
        // Hit Rate가 낮은 경우
        if (snapshot.HitRatio < 0.7)
        {
            var historyList = history.ToList();
            var trend = CalculateHitRateTrend(historyList);
            
            recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.HitRateImprovement,
                Priority = OptimizationPriority.High,
                Title = HighPerformanceStringPool.InternWeakly("Low Cache Hit Rate Detected"),
                Description = $"Current hit rate is {LazyCache.FormatPercentage(snapshot.HitRatio)}, which is below optimal threshold of 70%",
                Impact = EstimateHitRateImpact(snapshot.HitRatio),
                Suggestions =
                [
                    "Consider increasing cache TTL for frequently accessed data",
                    "Review cache key patterns for better cache locality",
                    "Implement cache warming for predictable access patterns",
                    "Analyze and optimize cache eviction policies"
                ],
                Metrics = CreateMetrics(("current_hit_rate", snapshot.HitRatio), ("trend", trend), ("target_hit_rate", 0.8))
            });
        }
        
        // Hit Rate가 급격히 감소하는 경우 - LINQ 없이 최적화
        var historyArray = history.ToArray(); // 한 번만 배열 변환
        if (historyArray.Length >= 10)
        {
            var recentAvg = CalculateAverageHitRatio(historyArray, historyArray.Length - 5, 5);
            var historicalAvg = CalculateAverageHitRatio(historyArray, 0, historyArray.Length - 5);
            
            if (recentAvg < historicalAvg * 0.8) // 20% 이상 감소
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.PerformanceDegradation,
                    Priority = OptimizationPriority.Critical,
                    Title = HighPerformanceStringPool.InternWeakly("Significant Hit Rate Degradation"),
                    Description = $"Hit rate dropped from {LazyCache.FormatPercentage(historicalAvg)} to {LazyCache.FormatPercentage(recentAvg)}",
                    Impact = $"Estimated {MemoryUtils.DoubleToFixedString((historicalAvg - recentAvg) * 100, 0)}% performance impact",
                    Suggestions =
                    [
                        "Investigate recent changes in data access patterns",
                        "Check for memory pressure causing premature evictions",
                        "Review recent application deployments or configuration changes",
                        "Consider emergency cache warming to restore performance"
                    ]
                });
            }
        }
    }

    private void AnalyzeMemoryUsage(
        CachePerformanceSnapshot snapshot, 
        IEnumerable<CachePerformanceSnapshot> history,
        List<OptimizationRecommendation> recommendations)
    {
        
        // Memory usage가 높은 경우
        var memorySizeStr = LazyCache.FormatByteSize(snapshot.MemoryUsageBytes);
        if (snapshot.MemoryUsageBytes > 1_073_741_824) // 1GB 이상
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.MemoryOptimization,
                Priority = OptimizationPriority.Medium,
                Title = HighPerformanceStringPool.InternWeakly("High Memory Usage"),
                Description = $"Cache is using {memorySizeStr} of memory",
                Impact = "High memory usage may impact system performance",
                Suggestions =
                [
                    "Implement more aggressive cache eviction policies",
                    "Reduce TTL for less critical cached data",
                    "Consider compressing cached values",
                    "Review cache size limits and adjust accordingly"
                ],
                Metrics = CreateMetrics(("memory_usage_mb", snapshot.MemoryUsageBytes), ("item_count", snapshot.ItemCount))
            });
        }
        
        // Memory 사용량이 급증하는 경우
        var historyArray = history.ToArray();
        if (historyArray.Length >= 5)
        {
            var memoryTrend = CalculateMemoryTrend(historyArray.ToList());
            if (memoryTrend > 0.2) // 20% 이상 증가 트렌드
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.MemoryLeak,
                    Priority = OptimizationPriority.High,
                    Title = HighPerformanceStringPool.InternWeakly("Memory Usage Increasing Trend"),
                    Description = "Memory usage is consistently increasing",
                    Impact = "Potential memory leak or cache configuration issue",
                    Suggestions =
                    [
                        "Monitor for memory leaks in cached objects",
                        "Review cache expiration policies",
                        "Implement periodic cache cleanup",
                        "Consider implementing cache size limits"
                    ]
                });
            }
        }
    }

    private async Task AnalyzeHotKeysAsync(List<OptimizationRecommendation> recommendations)
    {
        if (_intelligentCacheManager == null) return;
        
        try
        {
            var hotKeys = await _intelligentCacheManager.GetHotKeysAsync(20);
            var hotKeyArray = hotKeys.ToArray();  // 한 번만 배열 변환
            
            if (hotKeyArray.Length > 10) // 많은 Hot Key 감지
            {
                // Top 5 키들의 평균 계산 (LINQ 없이)
                var topCount = Math.Min(5, hotKeyArray.Length);
                double totalAccessRate = 0.0;
                for (int i = 0; i < topCount; i++)
                {
                    totalAccessRate += hotKeyArray[i].AccessRate;
                }
                var avgAccessRate = totalAccessRate / topCount;
                
                // Top keys 배열 생성 (익명 객체 없이)
                var topHotKeysArray = new object[topCount];
                for (int i = 0; i < topCount; i++)
                {
                    topHotKeysArray[i] = $"{hotKeyArray[i].Key}({hotKeyArray[i].AccessRate:F1})";
                }
                
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.HotKeyOptimization,
                    Priority = OptimizationPriority.Medium,
                    Title = HighPerformanceStringPool.InternWeakly("Multiple Hot Keys Detected"),
                    Description = $"Detected {LazyCache.IntToString(hotKeyArray.Length)} hot keys with average access rate of {MemoryUtils.DoubleToFixedString(avgAccessRate, 1)}/min",
                    Impact = "Hot keys may cause cache contention and uneven load distribution",
                    Suggestions =
                    [
                        "Consider implementing cache warming for hot keys",
                        "Extend TTL for frequently accessed hot keys",
                        "Implement key sharding for extremely hot keys",
                        "Monitor for thundering herd patterns"
                    ],
                    Metrics = CreateMetrics(
                        ("hot_keys_count", hotKeyArray.Length), 
                        ("top_hot_keys", topHotKeysArray), 
                        ("average_access_rate", avgAccessRate))
                });
            }
            
            // 극도로 핫한 키 감지 (LINQ 없이)
            var extremelyHotKeysList = new List<HotKeyInfo>();
            for (int i = 0; i < hotKeyArray.Length; i++)
            {
                if (hotKeyArray[i].AccessRate > 100) // 100회/분 이상
                {
                    extremelyHotKeysList.Add(hotKeyArray[i]);
                }
            }
            
            if (extremelyHotKeysList.Count > 0)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.PerformanceBottleneck,
                    Priority = OptimizationPriority.High,  
                    Title = HighPerformanceStringPool.InternWeakly("Extremely Hot Keys Detected"),
                    Description = $"Found {LazyCache.IntToString(extremelyHotKeysList.Count)} keys with >100 accesses/minute",
                    Impact = "Extremely hot keys can become performance bottlenecks",
                    Suggestions =
                    [
                        "Implement local caching layer for extremely hot keys",
                        "Consider read replicas or CDN for hot data",
                        "Review data access patterns for optimization opportunities",
                        "Implement circuit breaker pattern for hot key protection"
                    ],
                    Metrics = CreateMetrics(("extreme_hot_keys", 
                        CreateHotKeyDescriptions(extremelyHotKeysList)))
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to analyze hot keys");
        }
    }

    private void AnalyzePerformanceTrends(
        IEnumerable<CachePerformanceSnapshot> history,
        List<OptimizationRecommendation> recommendations)
    {
        var historyArray = history.ToArray();
        if (historyArray.Length < 10) return;

        // 성능 저하 트렌드 분석 (LINQ 없이)
        var recentPerformance = CalculateAverageHitRatio(historyArray, historyArray.Length - 5, 5);
        var historicalPerformance = CalculateAverageHitRatio(historyArray, 0, historyArray.Length - 5);
        
        if (recentPerformance < historicalPerformance * 0.9) // 10% 이상 성능 저하
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.PerformanceDegradation,
                Priority = OptimizationPriority.High,
                Title = "Performance Degradation Trend",
                Description = "Cache performance has been declining over time",
                Impact = $"Performance declined by {MemoryUtils.DoubleToFixedString(((historicalPerformance - recentPerformance) / historicalPerformance * 100), 1)}%",
                Suggestions =
                [
                    "Investigate root cause of performance decline",
                    "Review recent configuration changes",
                    "Check for resource constraints (CPU, memory, network)",
                    "Consider performance tuning or hardware upgrades"
                ]
            });
        }

    }

    private void AnalyzeErrorPatterns(
        IEnumerable<CachePerformanceSnapshot> history,
        List<OptimizationRecommendation> recommendations)
    {
        var historyArray = history.ToArray();
        if (historyArray.Length < 5) return;

        var recentErrors = CalculateAverageErrors(historyArray, historyArray.Length - 5, 5);
        var historicalErrors = CalculateAverageErrors(historyArray, 0, historyArray.Length - 5);
        
        if (recentErrors > historicalErrors * 2 && recentErrors > 10) // 에러가 2배 이상 증가
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.ErrorReduction,
                Priority = OptimizationPriority.Critical,
                Title = "Increasing Error Rate",
                Description = "Cache error rate has significantly increased",
                Impact = "High error rates can impact application reliability",
                Suggestions =
                [
                    "Investigate error logs for root cause analysis",
                    "Check cache backend connectivity and health",
                    "Review error handling and retry policies",
                    "Consider implementing circuit breaker patterns"
                ],
                Metrics = CreateMetrics(("recent_error_rate", recentErrors), ("historical_error_rate", historicalErrors))
            });
        }
    }

    #endregion

    #region Helper Methods

    private double CalculateHitRateTrend(List<CachePerformanceSnapshot> history)
    {
        if (history.Count < 5) return 0;
        
        // 최근 5개 평균 계산 (LINQ 없이)
        double recentSum = 0.0;
        int recentStart = history.Count - 5;
        for (int i = recentStart; i < history.Count; i++)
        {
            recentSum += history[i].HitRatio;
        }
        var recent = recentSum / 5;
        
        // 이전 데이터 평균 계산 (LINQ 없이)
        double historicalSum = 0.0;
        int historicalCount = history.Count - 5;
        for (int i = 0; i < historicalCount; i++)
        {
            historicalSum += history[i].HitRatio;
        }
        var historical = historicalCount > 0 ? historicalSum / historicalCount : 0.0;
        
        return recent - historical;
    }

    private double CalculateMemoryTrend(List<CachePerformanceSnapshot> history)
    {
        if (history.Count < 5) return 0;
        
        // 최근 5개 평균 계산 (LINQ 없이)
        double recentSum = 0.0;
        int recentStart = history.Count - 5;
        for (int i = recentStart; i < history.Count; i++)
        {
            recentSum += history[i].MemoryUsageBytes;
        }
        var recent = recentSum / 5;
        
        // 이전 데이터 평균 계산 (LINQ 없이)
        double historicalSum = 0.0;
        int historicalCount = history.Count - 5;
        for (int i = 0; i < historicalCount; i++)
        {
            historicalSum += history[i].MemoryUsageBytes;
        }
        var historical = historicalCount > 0 ? historicalSum / historicalCount : 1.0;
        
        return (recent - historical) / Math.Max(historical, 1);
    }

    private string EstimateHitRateImpact(double currentHitRate)
    {
        var optimalHitRate = 0.8;
        var improvementPotential = optimalHitRate - currentHitRate;
        var estimatedSpeedupPercent = improvementPotential * 100;
        
        return $"Improving hit rate could reduce response time by ~{MemoryUtils.DoubleToFixedString(estimatedSpeedupPercent, 0)}%";
    }

    private string GenerateReportSummary(List<OptimizationRecommendation> recommendations, CachePerformanceSnapshot snapshot)
    {
        // 우선순위별 카운트 (LINQ 없이)
        int critical = 0, high = 0, medium = 0;
        for (int i = 0; i < recommendations.Count; i++)
        {
            switch (recommendations[i].Priority)
            {
                case OptimizationPriority.Critical:
                    critical++;
                    break;
                case OptimizationPriority.High:
                    high++;
                    break;
                case OptimizationPriority.Medium:
                    medium++;
                    break;
            }
        }
        
        return $"Hit Rate: {LazyCache.FormatPercentage(snapshot.HitRatio)} | Memory: {LazyCache.FormatByteSize(snapshot.MemoryUsageBytes)} | " +
               $"Recommendations: {critical} Critical, {high} High, {medium} Medium";
    }

    private void PerformAnalysis(object? state)
    {
        if (_disposed) return;

        Task.Run(async () =>
        {
            try
            {
                var report = await GenerateOptimizationReportAsync();
                
                // 중요한 권고사항이 있으면 로그 (LINQ 없이)
                var criticalRecommendations = FilterCriticalRecommendations(report.Recommendations);
                if (criticalRecommendations.Length > 0)
                {
                    _logger.LogWarning("Critical cache optimization recommendations: {Count} issues detected", 
                        criticalRecommendations.Length);
                    
                    for (int i = 0; i < criticalRecommendations.Length; i++)
                    {
                        var rec = criticalRecommendations[i];
                        _logger.LogWarning("Critical: {Title} - {Description}", rec.Title, rec.Description);
                    }
                }
                
                // 최근 권고사항 저장 (최대 100개)
                var firstRecommendation = report.Recommendations.Length > 0 
                    ? report.Recommendations[0] 
                    : new OptimizationRecommendation();
                _recommendations.Enqueue(firstRecommendation);
                while (_recommendations.Count > 100)
                {
                    _recommendations.TryDequeue(out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cache optimization analysis");
            }
        });
    }

    /// <summary>
    /// 메트릭 Dictionary를 효율적으로 생성하는 헬퍼 메서드
    /// </summary>
    private static Dictionary<string, object> CreateMetrics(params (string key, object value)[] metrics)
    {
        var dict = new Dictionary<string, object>(metrics.Length);
        foreach (var (key, value) in metrics)
        {
            dict[key] = value;
        }
        return dict;
    }

    /// <summary>
    /// 배열의 특정 범위에서 HitRatio 평균을 계산 (값 타입 최적화)
    /// </summary>
    private static double CalculateAverageHitRatio(CachePerformanceSnapshot[] array, int startIndex, int count)
    {
        if (count == 0) return 0.0;
        
        var actualCount = Math.Min(count, array.Length - startIndex);
        if (actualCount <= 0) return 0.0;
        
        var span = array.AsSpan(startIndex, actualCount);
        var values = new double[actualCount];
        
        for (int i = 0; i < actualCount; i++)
        {
            values[i] = span[i].HitRatio;
        }
        
        return ValueTypeStatistics.CalculateAverage<double>(values);
    }

    /// <summary>
    /// 배열의 특정 범위에서 에러 수 평균을 계산 (값 타입 최적화)
    /// </summary>
    private static double CalculateAverageErrors(CachePerformanceSnapshot[] array, int startIndex, int count)
    {
        if (count == 0) return 0.0;
        
        var actualCount = Math.Min(count, array.Length - startIndex);
        if (actualCount <= 0) return 0.0;
        
        var span = array.AsSpan(startIndex, actualCount);
        var values = new long[actualCount];
        
        for (int i = 0; i < actualCount; i++)
        {
            values[i] = span[i].TotalErrors;
        }
        
        return ValueTypeStatistics.CalculateAverage<long>(values);
    }

    /// <summary>
    /// HotKeyInfo 리스트를 설명 문자열 배열로 변환 (고성능 문자열 풀 사용)
    /// </summary>
    private static string[] CreateHotKeyDescriptions(List<HotKeyInfo> hotKeys)
    {
        var descriptions = new string[hotKeys.Count];
        for (int i = 0; i < hotKeys.Count; i++)
        {
            // StringBuilder 풀 사용
            var sb = HighPerformanceStringPool.RentStringBuilder(64);
            try
            {
                sb.Append(hotKeys[i].Key);
                sb.Append('(');
                sb.Append(MemoryUtils.DoubleToFixedString(hotKeys[i].AccessRate, 1));
                sb.Append(')');
                descriptions[i] = sb.ToString();
            }
            finally
            {
                HighPerformanceStringPool.ReturnStringBuilder(sb, 64);
            }
        }
        return descriptions;
    }

    /// <summary>
    /// Critical 우선순위 권고사항만 필터링 (LINQ 없이)
    /// </summary>
    private static OptimizationRecommendation[] FilterCriticalRecommendations(OptimizationRecommendation[] recommendations)
    {
        var criticalList = new List<OptimizationRecommendation>();
        for (int i = 0; i < recommendations.Length; i++)
        {
            if (recommendations[i].Priority == OptimizationPriority.Critical)
            {
                criticalList.Add(recommendations[i]);
            }
        }
        return criticalList.ToArray();
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        _analysisTimer?.Dispose();
        _disposed = true;
        
        _logger.LogInformation("CacheOptimizationAnalyzer disposed");
    }
}

#region Models

public class OptimizationReport
{
    public DateTime GeneratedAt { get; init; }
    public CachePerformanceSnapshot CurrentSnapshot { get; init; } = new();
    public OptimizationRecommendation[] Recommendations { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}

public class OptimizationRecommendation
{
    public OptimizationType Type { get; init; }
    public OptimizationPriority Priority { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Impact { get; init; } = string.Empty;
    public string[] Suggestions { get; init; } = [];
    public Dictionary<string, object>? Metrics { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public enum OptimizationType
{
    HitRateImprovement,
    MemoryOptimization,
    MemoryLeak,
    HotKeyOptimization,
    PerformanceBottleneck,
    PerformanceDegradation,
    ErrorReduction
}

public enum OptimizationPriority
{
    Low,
    Medium,
    High,
    Critical
}

public class PatternAnalysis
{
    public string Pattern { get; init; } = string.Empty;
    public long AccessCount { get; init; }
    public DateTime FirstSeen { get; init; }
    public DateTime LastSeen { get; init; }
    public double Frequency { get; init; }
}

#endregion