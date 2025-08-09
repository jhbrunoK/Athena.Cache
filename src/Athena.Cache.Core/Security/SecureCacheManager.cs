using Microsoft.Extensions.Logging;
using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Memory;
using Athena.Cache.Core.Models;

namespace Athena.Cache.Core.Security;

/// <summary>
/// 보안이 강화된 캐시 매니저
/// 민감한 데이터 감지 및 안전한 캐시 작업 제공
/// </summary>
public class SecureCacheManager : IAthenaCache
{
    private readonly IAthenaCache _innerCache;
    private readonly SecureCacheKeyGenerator _keyGenerator;
    private readonly ILogger<SecureCacheManager> _logger;
    private readonly SecureCacheOptions _options;

    // 보안 관련 통계
    private long _blockedOperations = 0;
    private long _sensitiveCacheAttempts = 0;
    private long _totalOperations = 0;

    public SecureCacheManager(
        IAthenaCache innerCache,
        SecureCacheKeyGenerator keyGenerator,
        ILogger<SecureCacheManager> logger,
        SecureCacheOptions? options = null)
    {
        _innerCache = innerCache ?? throw new ArgumentNullException(nameof(innerCache));
        _keyGenerator = keyGenerator ?? throw new ArgumentNullException(nameof(keyGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new SecureCacheOptions();
    }

    /// <summary>
    /// 보안 검증을 통과한 값만 캐시에서 조회합니다
    /// </summary>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalOperations);

        try
        {
            // 키 검증
            if (!ValidateKey(key))
            {
                Interlocked.Increment(ref _blockedOperations);
                _logger.LogWarning("Blocked cache GET operation due to unsafe key");
                return default;
            }

            var result = await _innerCache.GetAsync<T>(key, cancellationToken);

            // 조회된 값에 대한 보안 검증 (옵션)
            if (result != null && _options.ValidateOnGet)
            {
                var detection = SensitiveDataDetector.DetectSensitiveData(result);
                if (!detection.IsSafe)
                {
                    _logger.LogWarning("Retrieved cached value contains sensitive data: {SensitiveTypes}. " +
                        "Consider reviewing cache storage policies.", 
                        string.Join(", ", detection.DetectedTypes));

                    if (_options.BlockSensitiveRetrieval)
                    {
                        Interlocked.Increment(ref _blockedOperations);
                        return default;
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secure cache GET operation");
            
            if (_options.FailSafeMode)
            {
                return default; // 오류 시 null 반환
            }
            
            throw;
        }
    }

    /// <summary>
    /// 민감한 데이터를 감지하고 안전한 값만 캐시에 저장합니다
    /// </summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalOperations);

        try
        {
            // 키 검증
            if (!ValidateKey(key))
            {
                Interlocked.Increment(ref _blockedOperations);
                _logger.LogWarning("Blocked cache SET operation due to unsafe key");
                return;
            }

            // 값에 대한 민감한 데이터 검증
            if (value != null)
            {
                var detection = SensitiveDataDetector.DetectSensitiveData(value);
                if (!detection.IsSafe)
                {
                    Interlocked.Increment(ref _sensitiveCacheAttempts);
                    
                    _logger.LogWarning("Attempt to cache sensitive data detected: {SensitiveTypes}. " +
                        "Value will not be cached for security reasons.",
                        string.Join(", ", detection.DetectedTypes));

                    if (_options.StrictMode)
                    {
                        Interlocked.Increment(ref _blockedOperations);
                        
                        if (_options.ThrowOnSensitiveData)
                        {
                            throw new SecurityException($"Sensitive data detected in cache value: {detection.Summary}");
                        }
                        
                        return; // 민감한 데이터는 캐시하지 않음
                    }

                    // Non-strict 모드에서는 마스킹된 값 저장 (옵션)
                    if (_options.MaskSensitiveData && value is string stringValue)
                    {
                        var maskedValue = SensitiveDataDetector.MaskSensitiveData(stringValue);
                        if (maskedValue is T maskedTypedValue)
                        {
                            value = maskedTypedValue;
                            _logger.LogInformation("Storing masked version of sensitive data");
                        }
                    }
                }
            }

            // TTL 검증 및 조정
            var adjustedTtl = ValidateAndAdjustTtl(expiration);

            await _innerCache.SetAsync(key, value, adjustedTtl, cancellationToken);

            // 보안 캐시 작업 로깅
            if (_options.EnableSecurityLogging)
            {
                LogSecureCacheOperation("SET", key, value, adjustedTtl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secure cache SET operation");
            
            if (!_options.FailSafeMode)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 캐시에서 안전하게 키를 제거합니다
    /// </summary>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalOperations);

        try
        {
            // 키 검증
            if (!ValidateKey(key))
            {
                Interlocked.Increment(ref _blockedOperations);
                _logger.LogWarning("Blocked cache REMOVE operation due to unsafe key");
                return;
            }

            await _innerCache.RemoveAsync(key, cancellationToken);

            if (_options.EnableSecurityLogging)
            {
                LogSecureCacheOperation<object>("REMOVE", key, default, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secure cache REMOVE operation");
            
            if (!_options.FailSafeMode)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 패턴에 맞는 캐시 키들을 안전하게 삭제합니다
    /// </summary>
    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalOperations);

        try
        {
            // 패턴 검증
            if (!ValidateKey(pattern))
            {
                Interlocked.Increment(ref _blockedOperations);
                _logger.LogWarning("Blocked cache REMOVE BY PATTERN operation due to unsafe pattern");
                return;
            }

            await _innerCache.RemoveByPatternAsync(pattern, cancellationToken);

            if (_options.EnableSecurityLogging)
            {
                LogSecureCacheOperation<object>("REMOVE_PATTERN", pattern, default, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secure cache REMOVE BY PATTERN operation");
            
            if (!_options.FailSafeMode)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 키 존재 여부를 안전하게 확인합니다
    /// </summary>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalOperations);

        try
        {
            // 키 검증
            if (!ValidateKey(key))
            {
                Interlocked.Increment(ref _blockedOperations);
                _logger.LogWarning("Blocked cache EXISTS operation due to unsafe key");
                return false;
            }

            return await _innerCache.ExistsAsync(key, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secure cache EXISTS operation");
            
            if (_options.FailSafeMode)
            {
                return false;
            }
            
            throw;
        }
    }

    /// <summary>
    /// 여러 키를 안전하게 배치 조회합니다
    /// </summary>
    public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalOperations);

        try
        {
            // 모든 키 검증
            var validKeys = new List<string>();
            foreach (var key in keys)
            {
                if (ValidateKey(key))
                {
                    validKeys.Add(key);
                }
                else
                {
                    Interlocked.Increment(ref _blockedOperations);
                    _logger.LogWarning("Skipping unsafe key in batch GET operation");
                }
            }

            if (validKeys.Count == 0)
            {
                return new Dictionary<string, T?>();
            }

            return await _innerCache.GetManyAsync<T>(validKeys, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secure cache GET MANY operation");
            
            if (_options.FailSafeMode)
            {
                return new Dictionary<string, T?>();
            }
            
            throw;
        }
    }

    /// <summary>
    /// 여러 키-값 쌍을 안전하게 배치 저장합니다
    /// </summary>
    public async Task SetManyAsync<T>(Dictionary<string, T> keyValuePairs, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalOperations);

        try
        {
            // 안전한 키-값 쌍만 필터링
            var safeKeyValuePairs = new Dictionary<string, T>();
            
            foreach (var kvp in keyValuePairs)
            {
                if (!ValidateKey(kvp.Key))
                {
                    Interlocked.Increment(ref _blockedOperations);
                    _logger.LogWarning("Skipping unsafe key in batch SET operation");
                    continue;
                }

                // 값 검증
                if (kvp.Value != null)
                {
                    var detection = SensitiveDataDetector.DetectSensitiveData(kvp.Value);
                    if (!detection.IsSafe && _options.StrictMode)
                    {
                        Interlocked.Increment(ref _sensitiveCacheAttempts);
                        _logger.LogWarning("Skipping sensitive data in batch SET operation");
                        continue;
                    }
                }

                safeKeyValuePairs[kvp.Key] = kvp.Value;
            }

            if (safeKeyValuePairs.Count > 0)
            {
                var adjustedTtl = ValidateAndAdjustTtl(expiration);
                await _innerCache.SetManyAsync(safeKeyValuePairs, adjustedTtl, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secure cache SET MANY operation");
            
            if (!_options.FailSafeMode)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 여러 키를 안전하게 배치 삭제합니다
    /// </summary>
    public async Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalOperations);

        try
        {
            // 안전한 키만 필터링
            var safeKeys = new List<string>();
            foreach (var key in keys)
            {
                if (ValidateKey(key))
                {
                    safeKeys.Add(key);
                }
                else
                {
                    Interlocked.Increment(ref _blockedOperations);
                    _logger.LogWarning("Skipping unsafe key in batch REMOVE operation");
                }
            }

            if (safeKeys.Count > 0)
            {
                await _innerCache.RemoveManyAsync(safeKeys, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secure cache REMOVE MANY operation");
            
            if (!_options.FailSafeMode)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 캐시 통계 정보를 안전하게 조회합니다
    /// </summary>
    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _innerCache.GetStatisticsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secure cache STATISTICS operation");
            
            if (_options.FailSafeMode)
            {
                return new CacheStatistics(); // 기본 통계 반환
            }
            
            throw;
        }
    }

    /// <summary>
    /// 키를 검증합니다
    /// </summary>
    private bool ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            _logger.LogWarning("Null or empty key provided to secure cache operation");
            return false;
        }

        return _keyGenerator.ValidateKey(key);
    }

    /// <summary>
    /// TTL을 검증하고 조정합니다
    /// </summary>
    private TimeSpan? ValidateAndAdjustTtl(TimeSpan? ttl)
    {
        if (ttl == null)
        {
            return ttl;
        }

        // 최대 TTL 제한
        if (ttl > _options.MaxTtl)
        {
            _logger.LogWarning("TTL {RequestedTtl} exceeds maximum allowed {MaxTtl}, adjusting", 
                ttl, _options.MaxTtl);
            return _options.MaxTtl;
        }

        // 최소 TTL 제한
        if (ttl < _options.MinTtl)
        {
            _logger.LogWarning("TTL {RequestedTtl} is below minimum allowed {MinTtl}, adjusting", 
                ttl, _options.MinTtl);
            return _options.MinTtl;
        }

        return ttl;
    }

    /// <summary>
    /// 보안 캐시 작업을 로깅합니다
    /// </summary>
    private void LogSecureCacheOperation<T>(string operation, string key, T? value, TimeSpan? ttl)
    {
        // StringBuilder 풀 사용으로 메모리 할당 최소화
        var sb = HighPerformanceStringPool.RentStringBuilder(256);
        try
        {
            sb.Append("SecureCache ");
            sb.Append(operation);
            sb.Append(" - Key: ");
            sb.Append(SensitiveDataDetector.MaskSensitiveData(key));

            if (ttl.HasValue)
            {
                sb.Append(", TTL: ");
                sb.Append(LazyCache.LongToString((long)ttl.Value.TotalSeconds));
                sb.Append("s");
            }

            _logger.LogDebug(sb.ToString());
        }
        finally
        {
            HighPerformanceStringPool.ReturnStringBuilder(sb, 256);
        }
    }

    /// <summary>
    /// 보안 통계 정보를 반환합니다
    /// </summary>
    public SecureCacheStats GetSecurityStats()
    {
        return new SecureCacheStats
        {
            TotalOperations = Interlocked.Read(ref _totalOperations),
            BlockedOperations = Interlocked.Read(ref _blockedOperations),
            SensitiveCacheAttempts = Interlocked.Read(ref _sensitiveCacheAttempts),
            BlockRate = Interlocked.Read(ref _totalOperations) > 0 
                ? (double)Interlocked.Read(ref _blockedOperations) / Interlocked.Read(ref _totalOperations) * 100
                : 0,
            KeyGeneratorStats = _keyGenerator.GetStats()
        };
    }

    /// <summary>
    /// 보안 설정을 업데이트합니다
    /// </summary>
    public void UpdateSecurityOptions(Action<SecureCacheOptions> configure)
    {
        configure(_options);
        _logger.LogInformation("Security options updated for SecureCacheManager");
    }
}

/// <summary>
/// 보안 캐시 옵션
/// </summary>
public class SecureCacheOptions
{
    /// <summary>
    /// 엄격 모드 (민감한 데이터 감지 시 캐시 거부)
    /// </summary>
    public bool StrictMode { get; set; } = true;

    /// <summary>
    /// 민감한 데이터 감지 시 예외 발생
    /// </summary>
    public bool ThrowOnSensitiveData { get; set; } = false;

    /// <summary>
    /// 민감한 데이터를 마스킹하여 저장
    /// </summary>
    public bool MaskSensitiveData { get; set; } = false;

    /// <summary>
    /// 조회 시에도 민감한 데이터 검증
    /// </summary>
    public bool ValidateOnGet { get; set; } = false;

    /// <summary>
    /// 민감한 데이터 조회 차단
    /// </summary>
    public bool BlockSensitiveRetrieval { get; set; } = false;

    /// <summary>
    /// 보안 로깅 활성화
    /// </summary>
    public bool EnableSecurityLogging { get; set; } = true;

    /// <summary>
    /// 안전 모드 (오류 시 예외 대신 기본값 반환)
    /// </summary>
    public bool FailSafeMode { get; set; } = true;

    /// <summary>
    /// 최대 TTL
    /// </summary>
    public TimeSpan MaxTtl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 최소 TTL
    /// </summary>
    public TimeSpan MinTtl { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// 보안 캐시 통계
/// </summary>
public class SecureCacheStats
{
    public long TotalOperations { get; init; }
    public long BlockedOperations { get; init; }
    public long SensitiveCacheAttempts { get; init; }
    public double BlockRate { get; init; }
    public CacheKeyGenerationStats KeyGeneratorStats { get; init; } = new();

    public override string ToString()
    {
        return $"SecureCache Stats - Total: {LazyCache.LongToString(TotalOperations)}, " +
               $"Blocked: {LazyCache.LongToString(BlockedOperations)} ({BlockRate:F1}%), " +
               $"Sensitive Attempts: {LazyCache.LongToString(SensitiveCacheAttempts)}";
    }
}