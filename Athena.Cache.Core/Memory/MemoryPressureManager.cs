using System.Runtime;
using Microsoft.Extensions.Logging;

namespace Athena.Cache.Core.Memory;

/// <summary>
/// 메모리 압박 감지 및 자동 정리 관리자
/// GC 압박을 모니터링하고 필요시 캐시와 풀을 정리
/// </summary>
public class MemoryPressureManager : IDisposable
{
    private readonly ILogger<MemoryPressureManager> _logger;
    private readonly Timer _monitorTimer;
    private readonly object _cleanupLock = new();
    
    private long _lastGcTotalMemory;
    private int _lastGen0Collections;
    private int _lastGen1Collections;
    private int _lastGen2Collections;
    private DateTime _lastCleanupTime = DateTime.MinValue;
    
    // 임계값 설정
    private const long MemoryPressureThreshold = 512 * 1024 * 1024; // 512MB
    private const double GcFrequencyThreshold = 5.0; // 5초당 GC 횟수
    private const int MinCleanupIntervalMinutes = 5; // 최소 정리 간격
    
    private volatile bool _disposed = false;
    
    public MemoryPressureManager(ILogger<MemoryPressureManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // 초기 상태 기록
        UpdateGcStats();
        
        // 30초마다 메모리 상태 체크
        _monitorTimer = new Timer(CheckMemoryPressure, null, 
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        _logger.LogInformation("MemoryPressureManager started");
    }
    
    /// <summary>
    /// 메모리 압박 상태 체크 및 정리 수행
    /// </summary>
    private void CheckMemoryPressure(object? state)
    {
        if (_disposed) return;
        
        try
        {
            var currentMemory = GC.GetTotalMemory(false);
            var pressureLevel = AnalyzeMemoryPressure(currentMemory);
            
            if (pressureLevel > MemoryPressureLevel.Normal)
            {
                _logger.LogWarning("Memory pressure detected: {Level}, Current memory: {Memory}", 
                    pressureLevel, LazyCache.FormatByteSize(currentMemory));
                
                PerformCleanup(pressureLevel);
            }
            else
            {
                _logger.LogDebug("Memory status normal: {Memory}", 
                    LazyCache.FormatByteSize(currentMemory));
            }
            
            UpdateGcStats();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during memory pressure check");
        }
    }
    
    /// <summary>
    /// 메모리 압박 수준 분석
    /// </summary>
    private MemoryPressureLevel AnalyzeMemoryPressure(long currentMemory)
    {
        var memoryIncrease = currentMemory - _lastGcTotalMemory;
        var gcStats = GetCurrentGcStats();
        
        // 심각한 메모리 압박 (1GB 이상 또는 빈번한 Gen2 GC)
        if (currentMemory > 1024 * 1024 * 1024 || gcStats.Gen2Frequency > 2.0)
        {
            return MemoryPressureLevel.Critical;
        }
        
        // 높은 메모리 압박 (512MB 이상 또는 빈번한 Gen1 GC)
        if (currentMemory > MemoryPressureThreshold || gcStats.Gen1Frequency > GcFrequencyThreshold)
        {
            return MemoryPressureLevel.High;
        }
        
        // 보통 메모리 압박 (빈번한 Gen0 GC या 메모리 증가)
        if (gcStats.Gen0Frequency > GcFrequencyThreshold * 2 || memoryIncrease > 100 * 1024 * 1024)
        {
            return MemoryPressureLevel.Medium;
        }
        
        return MemoryPressureLevel.Normal;
    }
    
    /// <summary>
    /// 압박 수준에 따른 정리 수행
    /// </summary>
    private void PerformCleanup(MemoryPressureLevel level)
    {
        lock (_cleanupLock)
        {
            // 최소 간격 체크
            if (DateTime.UtcNow - _lastCleanupTime < TimeSpan.FromMinutes(MinCleanupIntervalMinutes))
            {
                return;
            }
            
            _logger.LogInformation("Performing memory cleanup, level: {Level}", level);
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var initialMemory = GC.GetTotalMemory(false);
            
            try
            {
                switch (level)
                {
                    case MemoryPressureLevel.Medium:
                        // 가벼운 정리
                        PerformLightCleanup();
                        break;
                        
                    case MemoryPressureLevel.High:
                        // 중간 정리
                        PerformMediumCleanup();
                        break;
                        
                    case MemoryPressureLevel.Critical:
                        // 강력한 정리
                        PerformAggressiveCleanup();
                        break;
                }
                
                var finalMemory = GC.GetTotalMemory(true); // 강제 GC
                var memoryFreed = initialMemory - finalMemory;
                
                _logger.LogInformation("Memory cleanup completed in {Duration}ms, freed: {Memory}", 
                    stopwatch.ElapsedMilliseconds, LazyCache.FormatByteSize(memoryFreed));
                
                _lastCleanupTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during memory cleanup");
            }
            finally
            {
                stopwatch.Stop();
            }
        }
    }
    
    /// <summary>
    /// 가벼운 정리 (자주 사용되지 않는 캐시만)
    /// </summary>
    private void PerformLightCleanup()
    {
        // LazyCache의 일부 정리
        var cacheStats = LazyCache.GetCacheStats();
        if (cacheStats.TotalCacheSize > 500)
        {
            // 큰 캐시들만 일부 정리
            // TODO: LazyCache에 부분 정리 메서드 추가 필요
        }
        
        _logger.LogDebug("Light cleanup performed");
    }
    
    /// <summary>
    /// 중간 수준 정리
    /// </summary>
    private void PerformMediumCleanup()
    {
        // 문자열 풀 정리
        HighPerformanceStringPool.ClearAllPools();
        
        // LazyCache 일부 정리
        LazyCache.ClearCaches();
        
        // Gen0, Gen1 GC 유도
        GC.Collect(1, GCCollectionMode.Optimized);
        
        _logger.LogDebug("Medium cleanup performed");
    }
    
    /// <summary>
    /// 공격적 정리 (메모리 위기 상황)
    /// </summary>
    private void PerformAggressiveCleanup()
    {
        // 모든 캐시와 풀 정리
        HighPerformanceStringPool.ClearAllPools();
        LazyCache.ClearCaches();
        
        // 컬렉션 풀 정리 (필요시)
        // CollectionPools.ClearAll(); // TODO: 메서드 추가 필요
        
        // 강제 GC 실행 (모든 세대)
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Large Object Heap 압축
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        
        _logger.LogWarning("Aggressive cleanup performed - all caches cleared and full GC executed");
    }
    
    /// <summary>
    /// 현재 GC 통계 정보 가져오기
    /// </summary>
    private GcStatistics GetCurrentGcStats()
    {
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        
        var timeSinceLastCheck = DateTime.UtcNow - _lastCleanupTime;
        var intervalSeconds = Math.Max(timeSinceLastCheck.TotalSeconds, 1);
        
        return new GcStatistics
        {
            Gen0Collections = gen0,
            Gen1Collections = gen1,
            Gen2Collections = gen2,
            Gen0Frequency = (gen0 - _lastGen0Collections) / intervalSeconds,
            Gen1Frequency = (gen1 - _lastGen1Collections) / intervalSeconds,
            Gen2Frequency = (gen2 - _lastGen2Collections) / intervalSeconds
        };
    }
    
    /// <summary>
    /// GC 통계 업데이트
    /// </summary>
    private void UpdateGcStats()
    {
        _lastGcTotalMemory = GC.GetTotalMemory(false);
        _lastGen0Collections = GC.CollectionCount(0);
        _lastGen1Collections = GC.CollectionCount(1);
        _lastGen2Collections = GC.CollectionCount(2);
    }
    
    /// <summary>
    /// 현재 메모리 상태 정보 가져오기
    /// </summary>
    public MemoryStatus GetMemoryStatus()
    {
        var currentMemory = GC.GetTotalMemory(false);
        var gcStats = GetCurrentGcStats();
        var pressureLevel = AnalyzeMemoryPressure(currentMemory);
        
        return new MemoryStatus
        {
            TotalMemoryBytes = currentMemory,
            PressureLevel = pressureLevel,
            LastCleanupTime = _lastCleanupTime,
            GcStatistics = gcStats,
            CacheStats = LazyCache.GetCacheStats(),
            StringPoolStats = HighPerformanceStringPool.GetPoolStats()
        };
    }
    
    /// <summary>
    /// 수동으로 정리 실행
    /// </summary>
    public void ForceCleanup(MemoryPressureLevel level = MemoryPressureLevel.Medium)
    {
        _logger.LogInformation("Manual cleanup requested, level: {Level}", level);
        PerformCleanup(level);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _monitorTimer?.Dispose();
        _disposed = true;
        
        _logger.LogInformation("MemoryPressureManager disposed");
    }
}

/// <summary>
/// 메모리 압박 수준
/// </summary>
public enum MemoryPressureLevel
{
    Normal,
    Medium,
    High,
    Critical
}

/// <summary>
/// GC 통계 정보
/// </summary>
public readonly struct GcStatistics
{
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public double Gen0Frequency { get; init; }
    public double Gen1Frequency { get; init; }
    public double Gen2Frequency { get; init; }
    
    public override string ToString()
    {
        return $"GC Stats - Gen0: {Gen0Collections}({Gen0Frequency:F1}/s), " +
               $"Gen1: {Gen1Collections}({Gen1Frequency:F1}/s), " +
               $"Gen2: {Gen2Collections}({Gen2Frequency:F1}/s)";
    }
}

/// <summary>
/// 전체 메모리 상태 정보
/// </summary>
public readonly struct MemoryStatus
{
    public long TotalMemoryBytes { get; init; }
    public MemoryPressureLevel PressureLevel { get; init; }
    public DateTime LastCleanupTime { get; init; }
    public GcStatistics GcStatistics { get; init; }
    public CacheStats CacheStats { get; init; }
    public PoolStats StringPoolStats { get; init; }
    
    public override string ToString()
    {
        return $"Memory Status - Total: {LazyCache.FormatByteSize(TotalMemoryBytes)}, " +
               $"Pressure: {PressureLevel}, Last Cleanup: {LastCleanupTime:HH:mm:ss}";
    }
}