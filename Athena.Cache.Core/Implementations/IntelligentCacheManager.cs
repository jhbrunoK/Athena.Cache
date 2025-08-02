using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using System.Collections.Concurrent;

namespace Athena.Cache.Core.Implementations;

/// <summary>
/// 지능형 캐시 관리 구현체
/// Hot Key Detection, Adaptive TTL, LRU/LFU 정책 등 고급 캐싱 패턴 제공
/// </summary>
public class IntelligentCacheManager : IIntelligentCacheManager, IDisposable
{
    private readonly AthenaCacheOptions _options;
    private readonly ILogger<IntelligentCacheManager> _logger;
    
    // Hot Key Detection
    private readonly ConcurrentDictionary<string, KeyAccessMetrics> _keyMetrics = new();
    private readonly Timer? _hotKeyDetectionTimer;
    private volatile bool _isHotKeyDetectionActive = false;
    
    // Adaptive TTL
    private readonly ConcurrentDictionary<string, TtlMetrics> _ttlMetrics = new();
    
    // Cache Priority Calculation
    private readonly TimeSpan _metricRetentionPeriod = TimeSpan.FromHours(24);
    private readonly int _hotKeyTopCount = 100;
    private readonly double _hotKeyThreshold = 10.0; // 접근/분 기준
    
    private volatile bool _disposed = false;

    public IntelligentCacheManager(
        AthenaCacheOptions options,
        ILogger<IntelligentCacheManager> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Hot Key Detection 타이머 (1분마다 실행)
        _hotKeyDetectionTimer = new Timer(ProcessHotKeyDetection, null, Timeout.Infinite, Timeout.Infinite);
        
        _logger.LogInformation("IntelligentCacheManager initialized");
    }

    #region Hot Key Detection

    public async Task StartHotKeyDetectionAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(IntelligentCacheManager));
        
        if (_isHotKeyDetectionActive) return;

        _isHotKeyDetectionActive = true;
        _hotKeyDetectionTimer?.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        
        _logger.LogInformation("Hot Key Detection started");
        await Task.CompletedTask;
    }

    public async Task StopHotKeyDetectionAsync()
    {
        if (_disposed) return;
        
        _isHotKeyDetectionActive = false;
        _hotKeyDetectionTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        
        _logger.LogInformation("Hot Key Detection stopped");
        await Task.CompletedTask;
    }

    public async Task<IEnumerable<HotKeyInfo>> GetHotKeysAsync(int topCount = 10, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(IntelligentCacheManager));

        var cutoffTime = DateTime.UtcNow.Subtract(_metricRetentionPeriod);
        
        var hotKeys = _keyMetrics
            .Where(kvp => kvp.Value.LastAccess > cutoffTime)
            .Select(kvp => new HotKeyInfo
            {
                Key = kvp.Key,
                AccessCount = kvp.Value.AccessCount,
                AccessRate = CalculateAccessRate(kvp.Value),
                FirstAccess = kvp.Value.FirstAccess,
                LastAccess = kvp.Value.LastAccess,
                AverageInterval = CalculateAverageInterval(kvp.Value),
                Priority = CalculateKeyPriorityInternal(kvp.Value)
            })
            .OrderByDescending(info => info.AccessRate)
            .Take(Math.Min(topCount, _hotKeyTopCount))
            .ToList();

        _logger.LogDebug("Retrieved {Count} hot keys", hotKeys.Count);
        return await Task.FromResult(hotKeys);
    }

    public async Task RecordCacheAccessAsync(string cacheKey, CacheAccessType accessType, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(IntelligentCacheManager));
        if (string.IsNullOrEmpty(cacheKey)) return;

        var metrics = _keyMetrics.AddOrUpdate(cacheKey, 
            _ => new KeyAccessMetrics
            {
                FirstAccess = DateTime.UtcNow,
                LastAccess = DateTime.UtcNow,
                AccessCount = 1,
                AccessHistory = new ConcurrentQueue<DateTime>()
            },
            (_, existing) =>
            {
                existing.LastAccess = DateTime.UtcNow;
                existing.AccessCount++;
                existing.AccessHistory.Enqueue(DateTime.UtcNow);
                
                // 최근 1시간 데이터만 유지 (메모리 효율성) - 간단한 방식으로 변경
                if (existing.AccessHistory.Count > 1000) // 최대 1000개까지만 유지
                {
                    var itemsToRemove = existing.AccessHistory.Count - 1000;
                    for (int i = 0; i < itemsToRemove; i++)
                    {
                        existing.AccessHistory.TryDequeue(out DateTime _);
                    }
                }
                
                return existing;
            });

        // TTL 메트릭도 업데이트
        if (accessType == CacheAccessType.Hit || accessType == CacheAccessType.Miss)
        {
            UpdateTtlMetrics(cacheKey, accessType);
        }

        await Task.CompletedTask;
    }

    #endregion

    #region Adaptive TTL

    public async Task<TimeSpan> CalculateAdaptiveTtlAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(IntelligentCacheManager));
        if (string.IsNullOrEmpty(cacheKey))
            return TimeSpan.FromMinutes(_options.DefaultExpirationMinutes);

        // 기본 TTL
        var baseTtl = TimeSpan.FromMinutes(_options.DefaultExpirationMinutes);
        
        // 키 메트릭 조회
        if (!_keyMetrics.TryGetValue(cacheKey, out var keyMetrics))
            return baseTtl;

        // TTL 메트릭 조회
        if (!_ttlMetrics.TryGetValue(cacheKey, out var ttlMetrics))
            return baseTtl;

        // 적응형 TTL 계산 알고리즘
        var accessRate = CalculateAccessRate(keyMetrics);
        var hitRate = ttlMetrics.TotalAccess > 0 ? (double)ttlMetrics.HitCount / ttlMetrics.TotalAccess : 0.0;
        
        // 가중치 기반 TTL 조정
        var accessWeight = Math.Min(accessRate / _hotKeyThreshold, 2.0); // 최대 2배
        var hitRateWeight = Math.Max(hitRate, 0.5); // 최소 50% 가중치
        
        var adjustedTtl = TimeSpan.FromTicks((long)(baseTtl.Ticks * accessWeight * hitRateWeight));
        
        // 최소/최대 TTL 제한
        var minTtl = TimeSpan.FromMinutes(5);
        var maxTtl = TimeSpan.FromHours(24);
        
        if (adjustedTtl < minTtl) adjustedTtl = minTtl;
        if (adjustedTtl > maxTtl) adjustedTtl = maxTtl;

        _logger.LogDebug("Calculated adaptive TTL for {Key}: {TTL} (AccessRate: {AccessRate}, HitRate: {HitRate})",
            cacheKey, adjustedTtl, accessRate, hitRate);

        return await Task.FromResult(adjustedTtl);
    }

    #endregion

    #region Cache Priority & Eviction

    public async Task<double> CalculateKeyPriorityAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(IntelligentCacheManager));
        if (string.IsNullOrEmpty(cacheKey)) return 0.0;

        if (_keyMetrics.TryGetValue(cacheKey, out var metrics))
        {
            return await Task.FromResult(CalculateKeyPriorityInternal(metrics));
        }

        return await Task.FromResult(0.0);
    }

    public async Task EvictCacheByPolicyAsync(CacheEvictionPolicy policy, int maxItems, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(IntelligentCacheManager));

        var keysToEvict = policy switch
        {
            CacheEvictionPolicy.LRU => GetLruKeys(maxItems),
            CacheEvictionPolicy.LFU => GetLfuKeys(maxItems),
            CacheEvictionPolicy.TTL => GetExpiredKeys(maxItems),
            CacheEvictionPolicy.Random => GetRandomKeys(maxItems),
            CacheEvictionPolicy.FIFO => GetFifoKeys(maxItems),
            _ => []
        };

        var evictedCount = 0;
        foreach (var key in keysToEvict)
        {
            _keyMetrics.TryRemove(key, out _);
            _ttlMetrics.TryRemove(key, out _);
            evictedCount++;
        }

        _logger.LogInformation("Evicted {Count} keys using {Policy} policy", evictedCount, policy);
        await Task.CompletedTask;
    }

    #endregion

    #region Cache Warming

    public async Task WarmCacheAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(IntelligentCacheManager));

        var keyList = keys.ToList();
        if (keyList.Count == 0) return;

        _logger.LogInformation("Starting cache warming for {Count} keys", keyList.Count);
        
        // 병렬로 캐시 워밍 실행 (구현은 실제 캐시 제공자에 따라 달라짐)
        var warmingTasks = keyList.Select(async key =>
        {
            try
            {
                // 여기서는 메트릭만 초기화 (실제 데이터 로딩은 캐시 제공자가 담당)
                await RecordCacheAccessAsync(key, CacheAccessType.Set, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm cache for key: {Key}", key);
            }
        });

        await Task.WhenAll(warmingTasks);
        _logger.LogInformation("Cache warming completed");
    }

    #endregion

    #region Private Methods

    private void ProcessHotKeyDetection(object? state)
    {
        if (!_isHotKeyDetectionActive || _disposed) return;

        try
        {
            var cutoffTime = DateTime.UtcNow.Subtract(_metricRetentionPeriod);
            var expiredKeys = _keyMetrics
                .Where(kvp => kvp.Value.LastAccess < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            // 만료된 메트릭 정리
            foreach (var key in expiredKeys)
            {
                _keyMetrics.TryRemove(key, out _);
                _ttlMetrics.TryRemove(key, out _);
            }

            // Hot Key 감지 및 로깅
            var hotKeys = _keyMetrics
                .Where(kvp => CalculateAccessRate(kvp.Value) >= _hotKeyThreshold)
                .OrderByDescending(kvp => CalculateAccessRate(kvp.Value))
                .Take(10)
                .ToList();

            if (hotKeys.Any())
            {
                _logger.LogInformation("Detected {Count} hot keys: {Keys}",
                    hotKeys.Count,
                    string.Join(", ", hotKeys.Select(kvp => $"{kvp.Key}({CalculateAccessRate(kvp.Value):F1}/min)")));
            }

            _logger.LogDebug("Hot key detection completed. Cleaned {ExpiredCount} expired metrics, detected {HotCount} hot keys",
                expiredKeys.Count, hotKeys.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during hot key detection processing");
        }
    }

    private double CalculateAccessRate(KeyAccessMetrics metrics)
    {
        var duration = DateTime.UtcNow - metrics.FirstAccess;
        if (duration.TotalMinutes < 1) return metrics.AccessCount; // 1분 미만이면 총 액세스 수 반환
        
        return metrics.AccessCount / duration.TotalMinutes;
    }

    private TimeSpan CalculateAverageInterval(KeyAccessMetrics metrics)
    {
        if (metrics.AccessCount <= 1) return TimeSpan.Zero;
        
        var totalDuration = metrics.LastAccess - metrics.FirstAccess;
        return TimeSpan.FromTicks(totalDuration.Ticks / (metrics.AccessCount - 1));
    }

    private double CalculateKeyPriorityInternal(KeyAccessMetrics metrics)
    {
        var accessRate = CalculateAccessRate(metrics);
        var recency = (DateTime.UtcNow - metrics.LastAccess).TotalMinutes;
        var recencyScore = Math.Max(0, 60 - recency) / 60.0; // 1시간 내 접근에 높은 점수
        
        return accessRate * 0.7 + recencyScore * 0.3; // 접근 빈도 70%, 최근성 30%
    }

    private void UpdateTtlMetrics(string cacheKey, CacheAccessType accessType)
    {
        _ttlMetrics.AddOrUpdate(cacheKey,
            _ => new TtlMetrics
            {
                HitCount = accessType == CacheAccessType.Hit ? 1 : 0,
                MissCount = accessType == CacheAccessType.Miss ? 1 : 0,
                TotalAccess = 1
            },
            (_, existing) =>
            {
                if (accessType == CacheAccessType.Hit)
                    existing.HitCount++;
                else if (accessType == CacheAccessType.Miss)
                    existing.MissCount++;
                
                existing.TotalAccess++;
                return existing;
            });
    }

    private IEnumerable<string> GetLruKeys(int maxItems)
    {
        return _keyMetrics
            .OrderBy(kvp => kvp.Value.LastAccess)
            .Take(maxItems)
            .Select(kvp => kvp.Key);
    }

    private IEnumerable<string> GetLfuKeys(int maxItems)
    {
        return _keyMetrics
            .OrderBy(kvp => kvp.Value.AccessCount)
            .Take(maxItems)
            .Select(kvp => kvp.Key);
    }

    private IEnumerable<string> GetExpiredKeys(int maxItems)
    {
        var now = DateTime.UtcNow;
        return _keyMetrics
            .Where(kvp => now - kvp.Value.LastAccess > TimeSpan.FromMinutes(_options.DefaultExpirationMinutes))
            .Take(maxItems)
            .Select(kvp => kvp.Key);
    }

    private IEnumerable<string> GetRandomKeys(int maxItems)
    {
        var random = new Random();
        return _keyMetrics.Keys
            .OrderBy(_ => random.Next())
            .Take(maxItems);
    }

    private IEnumerable<string> GetFifoKeys(int maxItems)
    {
        return _keyMetrics
            .OrderBy(kvp => kvp.Value.FirstAccess)
            .Take(maxItems)
            .Select(kvp => kvp.Key);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        _hotKeyDetectionTimer?.Dispose();
        _isHotKeyDetectionActive = false;
        
        _keyMetrics.Clear();
        _ttlMetrics.Clear();
        
        _disposed = true;
        _logger.LogInformation("IntelligentCacheManager disposed");
    }

    #endregion
}

/// <summary>
/// 키 접근 메트릭
/// </summary>
internal class KeyAccessMetrics
{
    public DateTime FirstAccess { get; set; }
    public DateTime LastAccess { get; set; }
    public long AccessCount { get; set; }
    public ConcurrentQueue<DateTime> AccessHistory { get; set; } = new();
}

/// <summary>
/// TTL 관련 메트릭
/// </summary>
internal class TtlMetrics
{
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public long TotalAccess { get; set; }
}