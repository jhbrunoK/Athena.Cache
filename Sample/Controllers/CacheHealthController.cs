using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Athena.Cache.Core.HealthChecks;
using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Observability;
using Athena.Cache.Core.Security;
using Athena.Cache.Core.Memory;
using MsHealthCheck = Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sample.Controllers;

/// <summary>
/// Athena Cache Health Check API 컨트롤러
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CacheHealthController : ControllerBase
{
    private readonly MsHealthCheck.HealthCheckService _healthCheckService;
    private readonly IAthenaCache _cache;
    private readonly CacheHealthMonitor? _healthMonitor;
    private readonly SecureCacheManager? _secureCache;
    private readonly MemoryPressureManager? _memoryManager;
    private readonly ILogger<CacheHealthController> _logger;

    public CacheHealthController(
        MsHealthCheck.HealthCheckService healthCheckService,
        IAthenaCache cache,
        ILogger<CacheHealthController> logger,
        CacheHealthMonitor? healthMonitor = null,
        SecureCacheManager? secureCache = null,
        MemoryPressureManager? memoryManager = null)
    {
        _healthCheckService = healthCheckService;
        _cache = cache;
        _logger = logger;
        _healthMonitor = healthMonitor;
        _secureCache = secureCache;
        _memoryManager = memoryManager;
    }

    /// <summary>
    /// 전체 캐시 시스템 Health Check
    /// </summary>
    /// <returns>전체 Health Check 결과</returns>
    [HttpGet]
    public async Task<IActionResult> GetHealth()
    {
        try
        {
            var healthReport = await _healthCheckService.CheckHealthAsync();
            
            var response = new
            {
                status = healthReport.Status.ToString(),
                total_duration = healthReport.TotalDuration.TotalMilliseconds,
                results = healthReport.Entries.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        status = kvp.Value.Status.ToString(),
                        description = kvp.Value.Description,
                        duration = kvp.Value.Duration.TotalMilliseconds,
                        data = kvp.Value.Data,
                        exception = kvp.Value.Exception?.Message
                    })
            };

            var statusCode = healthReport.Status switch
            {
                MsHealthCheck.HealthStatus.Healthy => 200,
                MsHealthCheck.HealthStatus.Degraded => 200,
                MsHealthCheck.HealthStatus.Unhealthy => 503,
                _ => 500
            };

            return StatusCode(statusCode, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
            return StatusCode(500, new { error = "Health check failed", message = ex.Message });
        }
    }

    /// <summary>
    /// 특정 Health Check 결과 조회
    /// </summary>
    /// <param name="checkName">Health Check 이름</param>
    /// <returns>특정 Health Check 결과</returns>
    [HttpGet("{checkName}")]
    public async Task<IActionResult> GetSpecificHealth(string checkName)
    {
        try
        {
            var healthReport = await _healthCheckService.CheckHealthAsync();
            
            if (!healthReport.Entries.TryGetValue(checkName, out var entry))
            {
                return NotFound(new { error = $"Health check '{checkName}' not found" });
            }

            var response = new
            {
                name = checkName,
                status = entry.Status.ToString(),
                description = entry.Description,
                duration = entry.Duration.TotalMilliseconds,
                data = entry.Data,
                exception = entry.Exception?.Message
            };

            var statusCode = entry.Status switch
            {
                MsHealthCheck.HealthStatus.Healthy => 200,
                MsHealthCheck.HealthStatus.Degraded => 200,
                MsHealthCheck.HealthStatus.Unhealthy => 503,
                _ => 500
            };

            return StatusCode(statusCode, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during specific health check for {CheckName}", checkName);
            return StatusCode(500, new { error = "Health check failed", message = ex.Message });
        }
    }

    /// <summary>
    /// 캐시 성능 메트릭 조회
    /// </summary>
    /// <returns>성능 메트릭</returns>
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics()
    {
        try
        {
            var statistics = await _cache.GetStatisticsAsync();
            
            var response = new
            {
                cache_statistics = new
                {
                    total_keys = statistics.TotalKeys,
                    hit_count = statistics.HitCount,
                    miss_count = statistics.MissCount,
                    hit_ratio = statistics.HitRatio,
                    memory_usage_mb = Math.Round(statistics.MemoryUsage / 1024.0 / 1024.0, 2),
                    uptime = statistics.Uptime
                },
                performance_snapshot = _healthMonitor?.GetCurrentSnapshot(),
                security_stats = _secureCache?.GetSecurityStats(),
                memory_status = _memoryManager?.GetMemoryStatus(),
                system_info = new
                {
                    gc_memory_mb = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 2),
                    gc_gen0 = GC.CollectionCount(0),
                    gc_gen1 = GC.CollectionCount(1),
                    gc_gen2 = GC.CollectionCount(2),
                    timestamp = DateTime.UtcNow
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache metrics");
            return StatusCode(500, new { error = "Failed to retrieve metrics", message = ex.Message });
        }
    }

    /// <summary>
    /// 캐시 시스템 진단 정보 조회
    /// </summary>
    /// <returns>진단 정보</returns>
    [HttpGet("diagnostics")]
    public async Task<IActionResult> GetDiagnostics()
    {
        try
        {
            // 개별 Health Check 수행
            var healthCheckOptions = new AthenaCacheHealthCheckOptions();
            var healthCheck = new AthenaCacheHealthCheck(
                _cache,
                _healthMonitor ?? throw new InvalidOperationException("CacheHealthMonitor not available"),
                _logger.LoggerFactory.CreateLogger<AthenaCacheHealthCheck>(),
                healthCheckOptions,
                _memoryManager,
                _secureCache);

            var healthResult = await healthCheck.CheckHealthAsync(new MsHealthCheck.HealthCheckContext());

            var response = new
            {
                overall_status = healthResult.Status.ToString(),
                description = healthResult.Description,
                health_data = healthResult.Data,
                diagnostics = new
                {
                    components_available = new
                    {
                        cache = _cache != null,
                        health_monitor = _healthMonitor != null,
                        secure_cache = _secureCache != null,
                        memory_manager = _memoryManager != null
                    },
                    last_check = DateTime.UtcNow,
                    environment = new
                    {
                        machine_name = Environment.MachineName,
                        processor_count = Environment.ProcessorCount,
                        working_set_mb = Math.Round(Environment.WorkingSet / 1024.0 / 1024.0, 2)
                    }
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache diagnostics");
            return StatusCode(500, new { error = "Diagnostics failed", message = ex.Message });
        }
    }

    /// <summary>
    /// 캐시 시스템 준비 상태 확인 (Readiness Probe)
    /// </summary>
    /// <returns>준비 상태</returns>
    [HttpGet("ready")]
    public async Task<IActionResult> GetReadiness()
    {
        try
        {
            // 기본 캐시 작업 테스트
            const string testKey = "__readiness_check__";
            const string testValue = "ready";

            await _cache.SetAsync(testKey, testValue, TimeSpan.FromMinutes(1));
            var retrievedValue = await _cache.GetAsync<string>(testKey);
            await _cache.RemoveAsync(testKey);

            var isReady = retrievedValue == testValue;

            var response = new
            {
                ready = isReady,
                timestamp = DateTime.UtcNow,
                message = isReady ? "Cache system is ready" : "Cache system is not ready"
            };

            return isReady ? Ok(response) : StatusCode(503, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during readiness check");
            return StatusCode(503, new 
            { 
                ready = false, 
                timestamp = DateTime.UtcNow,
                message = "Cache system is not ready",
                error = ex.Message 
            });
        }
    }

    /// <summary>
    /// 캐시 시스템 생존 상태 확인 (Liveness Probe)
    /// </summary>
    /// <returns>생존 상태</returns>
    [HttpGet("live")]
    public IActionResult GetLiveness()
    {
        try
        {
            // 기본적인 시스템 상태만 확인 (실제 캐시 작업은 하지 않음)
            var alive = _cache != null;

            var response = new
            {
                alive = alive,
                timestamp = DateTime.UtcNow,
                message = alive ? "Cache system is alive" : "Cache system is not alive"
            };

            return alive ? Ok(response) : StatusCode(503, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during liveness check");
            return StatusCode(503, new 
            { 
                alive = false, 
                timestamp = DateTime.UtcNow,
                message = "Cache system is not alive",
                error = ex.Message 
            });
        }
    }

    /// <summary>
    /// Health Check 설정 정보 조회
    /// </summary>
    /// <returns>설정 정보</returns>
    [HttpGet("config")]
    public IActionResult GetHealthCheckConfig()
    {
        try
        {
            var options = new AthenaCacheHealthCheckOptions();
            
            var response = new
            {
                thresholds = new
                {
                    min_hit_ratio = options.MinHitRatio,
                    critical_hit_ratio = options.CriticalHitRatio,
                    max_response_time_ms = options.MaxResponseTime.TotalMilliseconds,
                    critical_response_time_ms = options.CriticalResponseTime.TotalMilliseconds,
                    max_error_rate = options.MaxErrorRate,
                    critical_error_rate = options.CriticalErrorRate,
                    max_memory_usage_mb = options.MaxMemoryUsageMB,
                    critical_memory_usage_mb = options.CriticalMemoryUsageMB,
                    max_gen2_gc_frequency = options.MaxGen2GcFrequency,
                    max_security_block_rate = options.MaxSecurityBlockRate,
                    max_sensitive_data_attempts = options.MaxSensitiveDataAttempts
                },
                components = new
                {
                    cache_available = _cache != null,
                    health_monitor_available = _healthMonitor != null,
                    secure_cache_available = _secureCache != null,
                    memory_manager_available = _memoryManager != null
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving health check configuration");
            return StatusCode(500, new { error = "Failed to retrieve config", message = ex.Message });
        }
    }
}