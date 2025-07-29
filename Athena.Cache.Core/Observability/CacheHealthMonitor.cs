using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Athena.Cache.Core.Observability;

/// <summary>
/// 캐시 상태 모니터링 및 헬스 체크 관리자
/// </summary>
public class CacheHealthMonitor : IDisposable
{
    private readonly IAthenaCache _cache;
    private readonly IIntelligentCacheManager? _intelligentCacheManager;
    private readonly IDistributedCacheInvalidator? _distributedInvalidator;
    private readonly AthenaCacheOptions _options;
    private readonly AthenaCacheMetrics _metrics;
    private readonly ILogger<CacheHealthMonitor> _logger;
    
    private readonly Timer _healthCheckTimer;
    private readonly ConcurrentDictionary<string, HealthCheckResult> _healthResults = new();
    private readonly ConcurrentQueue<CachePerformanceSnapshot> _performanceHistory = new();
    
    private volatile bool _disposed = false;
    private long _totalHits = 0;
    private long _totalMisses = 0;
    private long _totalInvalidations = 0;
    private long _totalErrors = 0;

    public CacheHealthMonitor(
        IAthenaCache cache,
        AthenaCacheOptions options,
        AthenaCacheMetrics metrics,
        ILogger<CacheHealthMonitor> logger,
        IIntelligentCacheManager? intelligentCacheManager = null,
        IDistributedCacheInvalidator? distributedInvalidator = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _intelligentCacheManager = intelligentCacheManager;
        _distributedInvalidator = distributedInvalidator;

        // 메트릭 콜백 설정
        SetupMetricCallbacks();
        
        // 1분마다 헬스 체크 실행
        _healthCheckTimer = new Timer(PerformHealthChecks, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        
        _logger.LogInformation("CacheHealthMonitor initialized");
    }

    #region Health Check Methods

    public async Task<OverallHealthStatus> GetOverallHealthAsync()
    {
        var healthChecks = new[]
        {
            await CheckCacheConnectivityAsync(),
            await CheckCachePerformanceAsync(),
            await CheckMemoryUsageAsync(),
            await CheckDistributedConnectionAsync(),
            await CheckIntelligentCacheAsync()
        };

        var overallStatus = healthChecks.All(h => h.Status == HealthStatus.Healthy) 
            ? HealthStatus.Healthy
            : healthChecks.Any(h => h.Status == HealthStatus.Critical)
                ? HealthStatus.Critical 
                : HealthStatus.Warning;

        return new OverallHealthStatus
        {
            Status = overallStatus,
            LastChecked = DateTime.UtcNow,
            HealthChecks = healthChecks,
            Summary = GenerateHealthSummary(healthChecks)
        };
    }

    private async Task<HealthCheckResult> CheckCacheConnectivityAsync()
    {
        try
        {
            var testKey = $"__health_check_{Guid.NewGuid():N}";
            var testValue = "health_check_value";
            
            using var activity = AthenaCacheMetrics.StartActivity("health_check.connectivity");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // 연결 상태 테스트
            await _cache.SetAsync(testKey, testValue, TimeSpan.FromMinutes(1));
            var retrievedValue = await _cache.GetAsync<string>(testKey);
            await _cache.RemoveAsync(testKey);
            
            stopwatch.Stop();
            
            var isHealthy = testValue.Equals(retrievedValue);
            var latency = stopwatch.ElapsedMilliseconds;
            
            return new HealthCheckResult
            {
                Name = "Cache Connectivity",
                Status = isHealthy && latency < 1000 ? HealthStatus.Healthy 
                       : latency < 5000 ? HealthStatus.Warning 
                       : HealthStatus.Critical,
                Message = $"Connectivity check completed in {latency}ms",
                Details = new Dictionary<string, object>
                {
                    { "latency_ms", latency },
                    { "connectivity_test_passed", isHealthy }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache connectivity health check failed");
            return new HealthCheckResult
            {
                Name = "Cache Connectivity",
                Status = HealthStatus.Critical,
                Message = $"Connectivity failed: {ex.Message}",
                Exception = ex
            };
        }
    }

    private async Task<HealthCheckResult> CheckCachePerformanceAsync()
    {
        try
        {
            var hitRatio = CalculateHitRatio();
            var avgOperationTime = await MeasureAverageOperationTimeAsync();
            
            var status = hitRatio >= 0.8 && avgOperationTime < 50 ? HealthStatus.Healthy
                       : hitRatio >= 0.6 && avgOperationTime < 100 ? HealthStatus.Warning
                       : HealthStatus.Critical;

            return new HealthCheckResult
            {
                Name = "Cache Performance",
                Status = status,
                Message = $"Hit Ratio: {hitRatio:P1}, Avg Operation: {avgOperationTime:F1}ms",
                Details = new Dictionary<string, object>
                {
                    { "hit_ratio", hitRatio },
                    { "average_operation_ms", avgOperationTime },
                    { "total_hits", _totalHits },
                    { "total_misses", _totalMisses }
                }
            };
        }
        catch (Exception ex)
        {
            return new HealthCheckResult
            {
                Name = "Cache Performance",
                Status = HealthStatus.Warning,
                Message = $"Performance check failed: {ex.Message}",
                Exception = ex
            };
        }
    }

    private async Task<HealthCheckResult> CheckMemoryUsageAsync()
    {
        try
        {
            var memoryUsage = GC.GetTotalMemory(false);
            var maxMemory = GC.GetTotalMemory(true); // Force GC to get more accurate reading
            
            // 통계 정보 수집
            var stats = await _cache.GetStatisticsAsync();
            
            var memoryMB = memoryUsage / (1024.0 * 1024.0);
            var maxMemoryMB = maxMemory / (1024.0 * 1024.0);
            var memoryUsageRatio = memoryUsage / (double)Math.Max(maxMemory, 1);
            
            var status = memoryUsageRatio < 0.8 ? HealthStatus.Healthy
                       : memoryUsageRatio < 0.9 ? HealthStatus.Warning
                       : HealthStatus.Critical;

            return new HealthCheckResult
            {
                Name = "Memory Usage",
                Status = status,
                Message = $"Memory: {memoryMB:F1}MB, Items: {stats?.ItemCount ?? 0}",
                Details = new Dictionary<string, object>
                {
                    { "memory_usage_mb", memoryMB },
                    { "memory_usage_ratio", memoryUsageRatio },
                    { "cache_item_count", stats?.ItemCount ?? 0 },
                    { "last_cleanup", stats?.LastCleanup ?? DateTime.MinValue }
                }
            };
        }
        catch (Exception ex)
        {
            return new HealthCheckResult
            {
                Name = "Memory Usage", 
                Status = HealthStatus.Warning,
                Message = $"Memory check failed: {ex.Message}",
                Exception = ex
            };
        }
    }

    private async Task<HealthCheckResult> CheckDistributedConnectionAsync()
    {
        if (_distributedInvalidator == null)
        {
            return new HealthCheckResult
            {
                Name = "Distributed Connection",
                Status = HealthStatus.Healthy,
                Message = "Distributed invalidation not configured (optional)"
            };
        }

        try
        {
            var isConnected = _distributedInvalidator.IsConnected;
            var instanceId = _distributedInvalidator.InstanceId;
            
            return new HealthCheckResult
            {
                Name = "Distributed Connection",
                Status = isConnected ? HealthStatus.Healthy : HealthStatus.Critical,
                Message = $"Instance {instanceId}: {(isConnected ? "Connected" : "Disconnected")}",
                Details = new Dictionary<string, object>
                {
                    { "is_connected", isConnected },
                    { "instance_id", instanceId }
                }
            };
        }
        catch (Exception ex)
        {
            return new HealthCheckResult
            {
                Name = "Distributed Connection",
                Status = HealthStatus.Critical,
                Message = $"Distributed connection check failed: {ex.Message}",
                Exception = ex
            };
        }
    }

    private async Task<HealthCheckResult> CheckIntelligentCacheAsync()
    {
        if (_intelligentCacheManager == null)
        {
            return new HealthCheckResult
            {
                Name = "Intelligent Cache",
                Status = HealthStatus.Healthy,
                Message = "Intelligent caching not configured (optional)"
            };
        }

        try
        {
            var hotKeys = await _intelligentCacheManager.GetHotKeysAsync(5);
            var hotKeyCount = hotKeys.Count();

            return new HealthCheckResult
            {
                Name = "Intelligent Cache",
                Status = HealthStatus.Healthy,
                Message = $"Detected {hotKeyCount} hot keys",
                Details = new Dictionary<string, object>
                {
                    { "hot_keys_count", hotKeyCount },
                    { "top_hot_keys", hotKeys.Take(3).Select(k => k.Key).ToArray() }
                }
            };
        }
        catch (Exception ex)
        {
            return new HealthCheckResult
            {
                Name = "Intelligent Cache",
                Status = HealthStatus.Warning,
                Message = $"Intelligent cache check failed: {ex.Message}",
                Exception = ex
            };
        }
    }

    #endregion

    #region Performance Tracking

    public void RecordCacheHit() 
    {
        Interlocked.Increment(ref _totalHits);
        _metrics.RecordCacheHit();
    }

    public void RecordCacheMiss() 
    {
        Interlocked.Increment(ref _totalMisses);
        _metrics.RecordCacheMiss();
    }

    public void RecordInvalidation(string tableName)
    {
        Interlocked.Increment(ref _totalInvalidations);
        _metrics.RecordCacheInvalidation(tableName);
    }

    public void RecordError(string operation, Exception exception)
    {
        Interlocked.Increment(ref _totalErrors);
        _metrics.RecordCacheError(operation, exception.GetType().Name, exception.Message);
    }

    public CachePerformanceSnapshot GetCurrentSnapshot()
    {
        return new CachePerformanceSnapshot
        {
            TotalHits = _totalHits,
            TotalMisses = _totalMisses,
            HitRatio = CalculateHitRatio(),
            TotalInvalidations = _totalInvalidations,
            TotalErrors = _totalErrors,
            MemoryUsageBytes = GC.GetTotalMemory(false),
            ItemCount = 0, // Will be set by callback
            HotKeysCount = 0, // Will be set by callback
            AverageOperationDuration = TimeSpan.Zero // Will be calculated
        };
    }

    public IEnumerable<CachePerformanceSnapshot> GetPerformanceHistory(int maxItems = 60)
    {
        return _performanceHistory.TakeLast(maxItems);
    }

    #endregion

    #region Private Methods

    private void SetupMetricCallbacks()
    {
        _metrics.SetMemoryUsageCallback(() => GC.GetTotalMemory(false));
        _metrics.SetHitRatioCallback(() => CalculateHitRatio());
        
        if (_intelligentCacheManager != null)
        {
            _metrics.SetHotKeysCountCallback(() => 
            {
                try
                {
                    // 동기적으로 실행하되, 실패하면 0 반환
                    var task = _intelligentCacheManager.GetHotKeysAsync(10);
                    if (task.IsCompleted)
                    {
                        return task.Result.Count();
                    }
                    return 0; // 비동기 작업이 완료되지 않으면 0 반환
                }
                catch
                {
                    return 0;
                }
            });
        }
    }

    private double CalculateHitRatio()
    {
        var total = _totalHits + _totalMisses;
        return total > 0 ? (double)_totalHits / total : 0.0;
    }

    private async Task<double> MeasureAverageOperationTimeAsync()
    {
        var testKey = $"__perf_test_{Guid.NewGuid():N}";
        var iterations = 10;
        var totalTime = 0.0;

        for (int i = 0; i < iterations; i++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await _cache.SetAsync($"{testKey}_{i}", "test_value", TimeSpan.FromMinutes(1));
            await _cache.GetAsync<string>($"{testKey}_{i}");
            await _cache.RemoveAsync($"{testKey}_{i}");
            stopwatch.Stop();
            
            totalTime += stopwatch.ElapsedMilliseconds;
        }

        return totalTime / iterations;
    }

    private string GenerateHealthSummary(HealthCheckResult[] healthChecks)
    {
        var healthy = healthChecks.Count(h => h.Status == HealthStatus.Healthy);
        var warning = healthChecks.Count(h => h.Status == HealthStatus.Warning);
        var critical = healthChecks.Count(h => h.Status == HealthStatus.Critical);
        
        return $"{healthy} Healthy, {warning} Warning, {critical} Critical";
    }

    private void PerformHealthChecks(object? state)
    {
        if (_disposed) return;

        Task.Run(async () =>
        {
            try
            {
                var overallHealth = await GetOverallHealthAsync();
                
                // 성능 스냅샷 저장
                var snapshot = GetCurrentSnapshot();
                _performanceHistory.Enqueue(snapshot);
                
                // 최대 60개 (1시간) 유지
                while (_performanceHistory.Count > 60)
                {
                    _performanceHistory.TryDequeue(out _);
                }

                if (overallHealth.Status == HealthStatus.Critical)
                {
                    _logger.LogError("Critical cache health issues detected: {Summary}", overallHealth.Summary);
                }
                else if (overallHealth.Status == HealthStatus.Warning)
                {
                    _logger.LogWarning("Cache health warnings detected: {Summary}", overallHealth.Summary);
                }
                else
                {
                    _logger.LogDebug("Cache health check completed: {Summary}", overallHealth.Summary);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during health check execution");
            }
        });
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        _healthCheckTimer?.Dispose();
        _metrics?.Dispose();
        _disposed = true;
        
        _logger.LogInformation("CacheHealthMonitor disposed");
    }
}

#region Health Check Models

public class OverallHealthStatus
{
    public HealthStatus Status { get; init; }
    public DateTime LastChecked { get; init; }
    public HealthCheckResult[] HealthChecks { get; init; } = Array.Empty<HealthCheckResult>();
    public string Summary { get; init; } = string.Empty;
}

public class HealthCheckResult
{
    public string Name { get; init; } = string.Empty;
    public HealthStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, object>? Details { get; init; }
    public Exception? Exception { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}

public enum HealthStatus
{
    Healthy,
    Warning, 
    Critical
}

#endregion