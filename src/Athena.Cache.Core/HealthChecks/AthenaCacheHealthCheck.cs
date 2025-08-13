using Microsoft.Extensions.Diagnostics.HealthChecks;
using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Observability;
using Athena.Cache.Core.Memory;
using Athena.Cache.Core.Security;
using MsHealthCheck = Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Athena.Cache.Core.HealthChecks;

/// <summary>
/// Athena Cache 시스템의 종합 헬스 체크
/// </summary>
public class AthenaCacheHealthCheck(
    IAthenaCache cache,
    CacheHealthMonitor healthMonitor,
    ILogger<AthenaCacheHealthCheck> logger,
    AthenaCacheHealthCheckOptions? options = null,
    MemoryPressureManager? memoryManager = null,
    SecureCacheManager? secureCache = null)
    : MsHealthCheck.IHealthCheck
{
    private readonly IAthenaCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly CacheHealthMonitor _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
    private readonly ILogger<AthenaCacheHealthCheck> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly AthenaCacheHealthCheckOptions _options = options ?? new AthenaCacheHealthCheckOptions();

    public async Task<MsHealthCheck.HealthCheckResult> CheckHealthAsync(
        MsHealthCheck.HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        var healthData = new Dictionary<string, object>();
        var issues = new List<string>();
        var overallStatus = MsHealthCheck.HealthStatus.Healthy;

        try
        {
            // 1. 기본 캐시 연결 테스트
            var cacheStatus = await CheckBasicCacheOperationsAsync(cancellationToken);
            healthData["cache_operations"] = cacheStatus;
            if (!cacheStatus.IsHealthy)
            {
                issues.Add("Cache operations failing");
                overallStatus = MsHealthCheck.HealthStatus.Degraded;
            }

            // 2. 성능 메트릭 검사
            var performanceStatus = await CheckPerformanceMetricsAsync();
            healthData["performance"] = performanceStatus;
            if (!performanceStatus.IsHealthy)
            {
                issues.Add($"Performance issues: {performanceStatus.Issue}");
                if (performanceStatus.IsCritical)
                {
                    overallStatus = MsHealthCheck.HealthStatus.Unhealthy;
                }
                else if (overallStatus == MsHealthCheck.HealthStatus.Healthy)
                {
                    overallStatus = MsHealthCheck.HealthStatus.Degraded;
                }
            }

            // 3. 메모리 상태 검사
            if (memoryManager != null)
            {
                var memoryStatus = CheckMemoryStatus();
                healthData["memory"] = memoryStatus;
                if (!memoryStatus.IsHealthy)
                {
                    issues.Add($"Memory pressure: {memoryStatus.Issue}");
                    if (memoryStatus.IsCritical)
                    {
                        overallStatus = MsHealthCheck.HealthStatus.Unhealthy;
                    }
                    else if (overallStatus == MsHealthCheck.HealthStatus.Healthy)
                    {
                        overallStatus = MsHealthCheck.HealthStatus.Degraded;
                    }
                }
            }

            // 4. 보안 상태 검사
            if (secureCache != null)
            {
                var securityStatus = CheckSecurityStatus();
                healthData["security"] = securityStatus;
                if (!securityStatus.IsHealthy)
                {
                    issues.Add($"Security concerns: {securityStatus.Issue}");
                    if (overallStatus == HealthStatus.Healthy)
                    {
                        overallStatus = MsHealthCheck.HealthStatus.Degraded;
                    }
                }
            }

            // 5. 캐시 통계 정보
            var statistics = await _cache.GetStatisticsAsync(cancellationToken);
            healthData["statistics"] = new
            {
                total_keys = statistics.TotalKeys,
                hit_count = statistics.HitCount,
                miss_count = statistics.MissCount,
                hit_ratio = statistics.HitRatio,
                memory_usage_mb = Math.Round(statistics.MemoryUsage / 1024.0 / 1024.0, 2),
                uptime = statistics.Uptime
            };

            // 6. 시스템 정보
            healthData["system_info"] = new
            {
                gc_memory_mb = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 2),
                gc_gen0 = GC.CollectionCount(0),
                gc_gen1 = GC.CollectionCount(1),
                gc_gen2 = GC.CollectionCount(2),
                timestamp = DateTime.UtcNow
            };

            var description = issues.Count == 0 
                ? "All cache systems are functioning normally" 
                : $"Issues detected: {string.Join(", ", issues)}";

            return new MsHealthCheck.HealthCheckResult(overallStatus, description, data: healthData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed with exception");
            
            healthData["error"] = new
            {
                message = ex.Message,
                type = ex.GetType().Name,
                timestamp = DateTime.UtcNow
            };

            return new MsHealthCheck.HealthCheckResult(
                MsHealthCheck.HealthStatus.Unhealthy, 
                $"Health check failed: {ex.Message}", 
                ex, 
                healthData);
        }
    }

    /// <summary>
    /// 기본 캐시 작업 테스트
    /// </summary>
    private async Task<CacheHealthStatus> CheckBasicCacheOperationsAsync(CancellationToken cancellationToken)
    {
        const string testKey = "__athena_health_check__";
        const string testValue = "health_check_test_value";

        try
        {
            var startTime = DateTime.UtcNow;

            // 테스트 데이터 저장
            await _cache.SetAsync(testKey, testValue, TimeSpan.FromMinutes(1), cancellationToken);

            // 테스트 데이터 조회
            var retrievedValue = await _cache.GetAsync<string>(testKey, cancellationToken);
            
            // 테스트 데이터 존재 확인
            var exists = await _cache.ExistsAsync(testKey, cancellationToken);

            // 테스트 데이터 삭제
            await _cache.RemoveAsync(testKey, cancellationToken);

            var duration = DateTime.UtcNow - startTime;

            // 검증
            var isHealthy = retrievedValue == testValue && exists;
            var responseTime = duration.TotalMilliseconds;

            return new CacheHealthStatus
            {
                IsHealthy = isHealthy,
                ResponseTime = responseTime,
                Details = new Dictionary<string, object>
                {
                    ["set_success"] = true,
                    ["get_success"] = retrievedValue == testValue,
                    ["exists_success"] = exists,
                    ["remove_success"] = true,
                    ["response_time_ms"] = responseTime
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache operations health check failed");
            
            return new CacheHealthStatus
            {
                IsHealthy = false,
                Issue = $"Cache operation failed: {ex.Message}",
                Details = new Dictionary<string, object>
                {
                    ["error"] = ex.Message,
                    ["error_type"] = ex.GetType().Name
                }
            };
        }
    }

    /// <summary>
    /// 성능 메트릭 검사
    /// </summary>
    private async Task<CacheHealthStatus> CheckPerformanceMetricsAsync()
    {
        try
        {
            var overallHealth = await _healthMonitor.GetOverallHealthAsync();
            var currentSnapshot = _healthMonitor.GetCurrentSnapshot();

            var issues = new List<string>();
            var isCritical = false;

            // Hit Rate 검사
            if (currentSnapshot.HitRatio < _options.MinHitRatio)
            {
                issues.Add($"Low hit ratio: {currentSnapshot.HitRatio:P1}");
                if (currentSnapshot.HitRatio < _options.CriticalHitRatio)
                {
                    isCritical = true;
                }
            }

            // 응답 시간 검사
            if (currentSnapshot.AverageOperationDuration > _options.MaxResponseTime)
            {
                issues.Add($"High response time: {currentSnapshot.AverageOperationDuration.TotalMilliseconds:F1}ms");
                if (currentSnapshot.AverageOperationDuration > _options.CriticalResponseTime)
                {
                    isCritical = true;
                }
            }

            // 에러율 검사
            var totalRequests = currentSnapshot.TotalHits + currentSnapshot.TotalMisses;
            var errorRate = totalRequests > 0 
                ? (double)currentSnapshot.TotalErrors / totalRequests 
                : 0;
                
            if (errorRate > _options.MaxErrorRate)
            {
                issues.Add($"High error rate: {errorRate:P1}");
                if (errorRate > _options.CriticalErrorRate)
                {
                    isCritical = true;
                }
            }

            return new CacheHealthStatus
            {
                IsHealthy = issues.Count == 0,
                IsCritical = isCritical,
                Issue = issues.Count > 0 ? string.Join(", ", issues) : null,
                Details = new Dictionary<string, object>
                {
                    ["hit_ratio"] = currentSnapshot.HitRatio,
                    ["response_time_ms"] = currentSnapshot.AverageOperationDuration.TotalMilliseconds,
                    ["error_rate"] = errorRate,
                    ["total_requests"] = totalRequests,
                    ["total_errors"] = currentSnapshot.TotalErrors,
                    ["memory_usage_mb"] = Math.Round(currentSnapshot.MemoryUsageBytes / 1024.0 / 1024.0, 2)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Performance metrics check failed");
            
            return new CacheHealthStatus
            {
                IsHealthy = false,
                IsCritical = true,
                Issue = $"Performance check failed: {ex.Message}",
                Details = new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                }
            };
        }
    }

    /// <summary>
    /// 메모리 상태 검사
    /// </summary>
    private CacheHealthStatus CheckMemoryStatus()
    {
        try
        {
            var memoryStatus = memoryManager!.GetMemoryStatus();

            var issues = new List<string>();
            var isCritical = false;

            // 메모리 사용량 검사
            var memoryUsageMB = memoryStatus.TotalMemoryBytes / 1024.0 / 1024.0;
            if (memoryUsageMB > _options.MaxMemoryUsageMB)
            {
                issues.Add($"High memory usage: {memoryUsageMB:F1}MB");
                if (memoryUsageMB > _options.CriticalMemoryUsageMB)
                {
                    isCritical = true;
                }
            }

            // 메모리 압박 수준 검사
            if (memoryStatus.PressureLevel >= Memory.MemoryPressureLevel.High)
            {
                issues.Add($"Memory pressure: {memoryStatus.PressureLevel}");
                if (memoryStatus.PressureLevel >= Memory.MemoryPressureLevel.Critical)
                {
                    isCritical = true;
                }
            }

            // GC 빈도 검사 (Gen2 GC가 너무 자주 발생하면 문제)
            if (memoryStatus.GcStatistics.Gen2Frequency > _options.MaxGen2GcFrequency)
            {
                issues.Add($"Frequent Gen2 GC: {memoryStatus.GcStatistics.Gen2Frequency:F1}/s");
                isCritical = true;
            }

            return new CacheHealthStatus
            {
                IsHealthy = issues.Count == 0,
                IsCritical = isCritical,
                Issue = issues.Count > 0 ? string.Join(", ", issues) : null,
                Details = new Dictionary<string, object>
                {
                    ["memory_usage_mb"] = memoryUsageMB,
                    ["pressure_level"] = memoryStatus.PressureLevel.ToString(),
                    ["gc_gen0_frequency"] = memoryStatus.GcStatistics.Gen0Frequency,
                    ["gc_gen1_frequency"] = memoryStatus.GcStatistics.Gen1Frequency,
                    ["gc_gen2_frequency"] = memoryStatus.GcStatistics.Gen2Frequency,
                    ["last_cleanup"] = memoryStatus.LastCleanupTime,
                    ["cache_stats"] = new
                    {
                        string_cache = memoryStatus.CacheStats.StringCacheSize,
                        total_cache = memoryStatus.CacheStats.TotalCacheSize
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory status check failed");
            
            return new CacheHealthStatus
            {
                IsHealthy = false,
                IsCritical = true,
                Issue = $"Memory check failed: {ex.Message}",
                Details = new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                }
            };
        }
    }

    /// <summary>
    /// 보안 상태 검사
    /// </summary>
    private CacheHealthStatus CheckSecurityStatus()
    {
        try
        {
            var securityStats = secureCache!.GetSecurityStats();

            var issues = new List<string>();

            // 차단율 검사
            if (securityStats.BlockRate > _options.MaxSecurityBlockRate)
            {
                issues.Add($"High security block rate: {securityStats.BlockRate:F1}%");
            }

            // 민감한 데이터 시도 검사
            if (securityStats.SensitiveCacheAttempts > _options.MaxSensitiveDataAttempts)
            {
                issues.Add($"Many sensitive data attempts: {securityStats.SensitiveCacheAttempts}");
            }

            return new CacheHealthStatus
            {
                IsHealthy = issues.Count == 0,
                Issue = issues.Count > 0 ? string.Join(", ", issues) : null,
                Details = new Dictionary<string, object>
                {
                    ["total_operations"] = securityStats.TotalOperations,
                    ["blocked_operations"] = securityStats.BlockedOperations,
                    ["block_rate_percent"] = securityStats.BlockRate,
                    ["sensitive_attempts"] = securityStats.SensitiveCacheAttempts,
                    ["key_generator"] = new
                    {
                        strategy = securityStats.KeyGeneratorStats.Strategy.ToString(),
                        hash_algorithm = securityStats.KeyGeneratorStats.HashAlgorithm.ToString(),
                        strict_mode = securityStats.KeyGeneratorStats.StrictMode
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Security status check failed");
            
            return new CacheHealthStatus
            {
                IsHealthy = false,
                Issue = $"Security check failed: {ex.Message}",
                Details = new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                }
            };
        }
    }
}

/// <summary>
/// Athena Cache Health Check 옵션
/// </summary>
public class AthenaCacheHealthCheckOptions
{
    /// <summary>
    /// 최소 허용 캐시 적중률
    /// </summary>
    public double MinHitRatio { get; set; } = 0.7; // 70%

    /// <summary>
    /// 크리티컬 캐시 적중률 임계값
    /// </summary>
    public double CriticalHitRatio { get; set; } = 0.5; // 50%

    /// <summary>
    /// 최대 허용 응답 시간
    /// </summary>
    public TimeSpan MaxResponseTime { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 크리티컬 응답 시간 임계값
    /// </summary>
    public TimeSpan CriticalResponseTime { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 최대 허용 에러율
    /// </summary>
    public double MaxErrorRate { get; set; } = 0.01; // 1%

    /// <summary>
    /// 크리티컬 에러율 임계값
    /// </summary>
    public double CriticalErrorRate { get; set; } = 0.05; // 5%

    /// <summary>
    /// 최대 허용 메모리 사용량 (MB)
    /// </summary>
    public double MaxMemoryUsageMB { get; set; } = 512;

    /// <summary>
    /// 크리티컬 메모리 사용량 임계값 (MB)
    /// </summary>
    public double CriticalMemoryUsageMB { get; set; } = 1024;

    /// <summary>
    /// 최대 허용 Gen2 GC 빈도 (초당)
    /// </summary>
    public double MaxGen2GcFrequency { get; set; } = 0.1; // 10초에 1번

    /// <summary>
    /// 최대 허용 보안 차단율 (%)
    /// </summary>
    public double MaxSecurityBlockRate { get; set; } = 5.0; // 5%

    /// <summary>
    /// 최대 허용 민감한 데이터 시도 횟수
    /// </summary>
    public long MaxSensitiveDataAttempts { get; set; } = 100;
}

/// <summary>
/// 캐시 헬스 상태
/// </summary>
public class CacheHealthStatus
{
    public bool IsHealthy { get; set; }
    public bool IsCritical { get; set; }
    public string? Issue { get; set; }
    public double ResponseTime { get; set; }
    public Dictionary<string, object> Details { get; set; } = new();
}
