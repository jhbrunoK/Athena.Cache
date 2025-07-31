using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Observability;
using System.Collections.Concurrent;

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
        var performanceHistory = _healthMonitor.GetPerformanceHistory(60).ToList();
        
        var recommendations = new List<OptimizationRecommendation>();
        
        // 1. Hit Rate 분석
        recommendations.AddRange(await AnalyzeHitRateAsync(currentSnapshot, performanceHistory));
        
        // 2. Memory Usage 분석
        recommendations.AddRange(AnalyzeMemoryUsage(currentSnapshot, performanceHistory));
        
        // 3. Hot Key 분석
        if (_intelligentCacheManager != null)
        {
            recommendations.AddRange(await AnalyzeHotKeysAsync());
        }
        
        // 4. Performance Trend 분석
        recommendations.AddRange(AnalyzePerformanceTrends(performanceHistory));
        
        // 5. Error Pattern 분석
        recommendations.AddRange(AnalyzeErrorPatterns(performanceHistory));

        return new OptimizationReport
        {
            GeneratedAt = DateTime.UtcNow,
            CurrentSnapshot = currentSnapshot,
            Recommendations = recommendations.OrderByDescending(r => r.Priority).ToArray(),
            Summary = GenerateReportSummary(recommendations, currentSnapshot)
        };
    }

    private async Task<IEnumerable<OptimizationRecommendation>> AnalyzeHitRateAsync(
        CachePerformanceSnapshot snapshot, 
        List<CachePerformanceSnapshot> history)
    {
        var recommendations = new List<OptimizationRecommendation>();
        
        // Hit Rate가 낮은 경우
        if (snapshot.HitRatio < 0.7)
        {
            var trend = CalculateHitRateTrend(history);
            
            recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.HitRateImprovement,
                Priority = OptimizationPriority.High,
                Title = "Low Cache Hit Rate Detected",
                Description = $"Current hit rate is {snapshot.HitRatio:P1}, which is below optimal threshold of 70%",
                Impact = EstimateHitRateImpact(snapshot.HitRatio),
                Suggestions =
                [
                    "Consider increasing cache TTL for frequently accessed data",
                    "Review cache key patterns for better cache locality",
                    "Implement cache warming for predictable access patterns",
                    "Analyze and optimize cache eviction policies"
                ],
                Metrics = new Dictionary<string, object>
                {
                    { "current_hit_rate", snapshot.HitRatio },
                    { "trend", trend },
                    { "target_hit_rate", 0.8 }
                }
            });
        }
        
        // Hit Rate가 급격히 감소하는 경우
        if (history.Count >= 10)
        {
            var recentAvg = history.Skip(history.Count - 5).Average(h => h.HitRatio);
            var historicalAvg = history.Take(history.Count - 5).Average(h => h.HitRatio);
            
            if (recentAvg < historicalAvg * 0.8) // 20% 이상 감소
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.PerformanceDegradation,
                    Priority = OptimizationPriority.Critical,
                    Title = "Significant Hit Rate Degradation",
                    Description = $"Hit rate dropped from {historicalAvg:P1} to {recentAvg:P1}",
                    Impact = $"Estimated {(historicalAvg - recentAvg) * 100:F0}% performance impact",
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

        return recommendations;
    }

    private IEnumerable<OptimizationRecommendation> AnalyzeMemoryUsage(
        CachePerformanceSnapshot snapshot, 
        List<CachePerformanceSnapshot> history)
    {
        var recommendations = new List<OptimizationRecommendation>();
        
        // Memory usage가 높은 경우
        var memoryMB = snapshot.MemoryUsageBytes / (1024.0 * 1024.0);
        if (memoryMB > 1000) // 1GB 이상
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.MemoryOptimization,
                Priority = OptimizationPriority.Medium,
                Title = "High Memory Usage",
                Description = $"Cache is using {memoryMB:F0}MB of memory",
                Impact = "High memory usage may impact system performance",
                Suggestions =
                [
                    "Implement more aggressive cache eviction policies",
                    "Reduce TTL for less critical cached data",
                    "Consider compressing cached values",
                    "Review cache size limits and adjust accordingly"
                ],
                Metrics = new Dictionary<string, object>
                {
                    { "memory_usage_mb", memoryMB },
                    { "item_count", snapshot.ItemCount }
                }
            });
        }
        
        // Memory 사용량이 급증하는 경우
        if (history.Count >= 5)
        {
            var memoryTrend = CalculateMemoryTrend(history);
            if (memoryTrend > 0.2) // 20% 이상 증가 트렌드
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.MemoryLeak,
                    Priority = OptimizationPriority.High,
                    Title = "Memory Usage Increasing Trend",
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

        return recommendations;
    }

    private async Task<IEnumerable<OptimizationRecommendation>> AnalyzeHotKeysAsync()
    {
        if (_intelligentCacheManager == null) return [];
        
        var recommendations = new List<OptimizationRecommendation>();
        
        try
        {
            var hotKeys = await _intelligentCacheManager.GetHotKeysAsync(20);
            var hotKeyList = hotKeys.ToList();
            
            if (hotKeyList.Count > 10) // 많은 Hot Key 감지
            {
                var topHotKeys = hotKeyList.Take(5).ToList();
                var avgAccessRate = topHotKeys.Average(k => k.AccessRate);
                
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.HotKeyOptimization,
                    Priority = OptimizationPriority.Medium,
                    Title = "Multiple Hot Keys Detected",
                    Description = $"Detected {hotKeyList.Count} hot keys with average access rate of {avgAccessRate:F1}/min",
                    Impact = "Hot keys may cause cache contention and uneven load distribution",
                    Suggestions =
                    [
                        "Consider implementing cache warming for hot keys",
                        "Extend TTL for frequently accessed hot keys",
                        "Implement key sharding for extremely hot keys",
                        "Monitor for thundering herd patterns"
                    ],
                    Metrics = new Dictionary<string, object>
                    {
                        { "hot_keys_count", hotKeyList.Count },
                        { "top_hot_keys", topHotKeys.Select(k => new { k.Key, k.AccessRate }).ToArray() },
                        { "average_access_rate", avgAccessRate }
                    }
                });
            }
            
            // 극도로 핫한 키 감지
            var extremelyHotKeys = hotKeyList.Where(k => k.AccessRate > 100).ToList(); // 100회/분 이상
            if (extremelyHotKeys.Any())
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.PerformanceBottleneck,
                    Priority = OptimizationPriority.High,  
                    Title = "Extremely Hot Keys Detected",
                    Description = $"Found {extremelyHotKeys.Count} keys with >100 accesses/minute",
                    Impact = "Extremely hot keys can become performance bottlenecks",
                    Suggestions =
                    [
                        "Implement local caching layer for extremely hot keys",
                        "Consider read replicas or CDN for hot data",
                        "Review data access patterns for optimization opportunities",
                        "Implement circuit breaker pattern for hot key protection"
                    ],
                    Metrics = new Dictionary<string, object>
                    {
                        { "extreme_hot_keys", extremelyHotKeys.Select(k => new { k.Key, k.AccessRate }).ToArray() }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to analyze hot keys");
        }

        return recommendations;
    }

    private IEnumerable<OptimizationRecommendation> AnalyzePerformanceTrends(
        List<CachePerformanceSnapshot> history)
    {
        var recommendations = new List<OptimizationRecommendation>();
        
        if (history.Count < 10) return recommendations;

        // 성능 저하 트렌드 분석
        var recentPerformance = history.Skip(history.Count - 5).Average(h => h.HitRatio);
        var historicalPerformance = history.Take(history.Count - 5).Average(h => h.HitRatio);
        
        if (recentPerformance < historicalPerformance * 0.9) // 10% 이상 성능 저하
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.PerformanceDegradation,
                Priority = OptimizationPriority.High,
                Title = "Performance Degradation Trend",
                Description = "Cache performance has been declining over time",
                Impact = $"Performance declined by {((historicalPerformance - recentPerformance) / historicalPerformance * 100):F1}%",
                Suggestions =
                [
                    "Investigate root cause of performance decline",
                    "Review recent configuration changes",
                    "Check for resource constraints (CPU, memory, network)",
                    "Consider performance tuning or hardware upgrades"
                ]
            });
        }

        return recommendations;
    }

    private IEnumerable<OptimizationRecommendation> AnalyzeErrorPatterns(
        List<CachePerformanceSnapshot> history)
    {
        var recommendations = new List<OptimizationRecommendation>();
        
        if (history.Count < 5) return recommendations;

        var recentErrors = history.Skip(history.Count - 5).Average(h => h.TotalErrors);
        var historicalErrors = history.Take(history.Count - 5).Average(h => h.TotalErrors);
        
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
                Metrics = new Dictionary<string, object>
                {
                    { "recent_error_rate", recentErrors },
                    { "historical_error_rate", historicalErrors }
                }
            });
        }

        return recommendations;
    }

    #endregion

    #region Helper Methods

    private double CalculateHitRateTrend(List<CachePerformanceSnapshot> history)
    {
        if (history.Count < 5) return 0;
        
        var recent = history.Skip(history.Count - 5).Average(h => h.HitRatio);
        var historical = history.Take(history.Count - 5).Average(h => h.HitRatio);
        
        return recent - historical;
    }

    private double CalculateMemoryTrend(List<CachePerformanceSnapshot> history)
    {
        if (history.Count < 5) return 0;
        
        var recent = history.Skip(history.Count - 5).Average(h => h.MemoryUsageBytes);
        var historical = history.Take(history.Count - 5).Average(h => h.MemoryUsageBytes);
        
        return (recent - historical) / Math.Max(historical, 1);
    }

    private string EstimateHitRateImpact(double currentHitRate)
    {
        var optimalHitRate = 0.8;
        var improvementPotential = optimalHitRate - currentHitRate;
        var estimatedSpeedupPercent = improvementPotential * 100;
        
        return $"Improving hit rate could reduce response time by ~{estimatedSpeedupPercent:F0}%";
    }

    private string GenerateReportSummary(List<OptimizationRecommendation> recommendations, CachePerformanceSnapshot snapshot)
    {
        var critical = recommendations.Count(r => r.Priority == OptimizationPriority.Critical);
        var high = recommendations.Count(r => r.Priority == OptimizationPriority.High);
        var medium = recommendations.Count(r => r.Priority == OptimizationPriority.Medium);
        
        return $"Hit Rate: {snapshot.HitRatio:P1} | Memory: {snapshot.MemoryUsageBytes / (1024.0 * 1024.0):F0}MB | " +
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
                
                // 중요한 권고사항이 있으면 로그
                var criticalRecommendations = report.Recommendations.Where(r => r.Priority == OptimizationPriority.Critical).ToArray();
                if (criticalRecommendations.Any())
                {
                    _logger.LogWarning("Critical cache optimization recommendations: {Count} issues detected", 
                        criticalRecommendations.Length);
                    
                    foreach (var rec in criticalRecommendations)
                    {
                        _logger.LogWarning("Critical: {Title} - {Description}", rec.Title, rec.Description);
                    }
                }
                
                // 최근 권고사항 저장 (최대 100개)
                _recommendations.Enqueue(report.Recommendations.FirstOrDefault() ?? new OptimizationRecommendation());
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