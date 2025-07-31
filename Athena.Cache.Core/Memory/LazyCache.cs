using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Athena.Cache.Core.Memory;

/// <summary>
/// 지연 초기화 및 결과 캐싱을 위한 고성능 유틸리티
/// 반복적으로 계산되는 값들의 메모리 할당을 제거
/// </summary>
public static class LazyCache
{
    // 문자열 상수 캐싱 (자주 사용되는 문자열들)
    private static readonly ConcurrentDictionary<string, string> _stringCache = new();
    
    // 숫자 → 문자열 변환 캐싱 (자주 사용되는 숫자들)
    private static readonly ConcurrentDictionary<int, string> _intStringCache = new();
    private static readonly ConcurrentDictionary<long, string> _longStringCache = new();
    
    // 백분율 포맷 캐싱 (자주 사용되는 비율들)
    private static readonly ConcurrentDictionary<double, string> _percentageCache = new();
    
    // 바이트 크기 포맷 캐싱
    private static readonly ConcurrentDictionary<long, string> _byteSizeCache = new();
    
    // 캐시 크기 제한 (메모리 누수 방지)
    private const int MaxCacheSize = 1000;
    
    static LazyCache()
    {
        // 자주 사용되는 값들 미리 캐싱
        PrePopulateCommonValues();
    }
    
    /// <summary>
    /// 자주 사용되는 값들을 미리 캐싱하여 초기 성능 향상
    /// </summary>
    private static void PrePopulateCommonValues()
    {
        // 자주 사용되는 정수들 (0-100)
        for (int i = 0; i <= 100; i++)
        {
            _intStringCache[i] = i.ToString();
        }
        
        // 자주 사용되는 백분율들 (0%, 10%, 20%, ..., 100%)
        for (int i = 0; i <= 100; i += 10)
        {
            var ratio = i / 100.0;
            _percentageCache[ratio] = MemoryUtils.FormatPercentage(ratio);
        }
        
        // 자주 사용되는 바이트 크기들
        var commonSizes = new long[] 
        { 
            0, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144, 524288,
            1024 * 1024, 2 * 1024 * 1024, 4 * 1024 * 1024, 8 * 1024 * 1024,
            16 * 1024 * 1024, 32 * 1024 * 1024, 64 * 1024 * 1024, 128 * 1024 * 1024,
            256 * 1024 * 1024, 512 * 1024 * 1024, 1024 * 1024 * 1024L
        };
        
        foreach (var size in commonSizes)
        {
            _byteSizeCache[size] = MemoryUtils.FormatByteSize(size);
        }
        
        // 자주 사용되는 문자열들
        var commonStrings = new[]
        {
            "unknown", "pending", "completed", "in_progress", "failed", "success",
            "high", "medium", "low", "critical", "GET", "POST", "PUT", "DELETE",
            "application/json", "text/plain", "text/html", "HIT", "MISS",
            "cache_get", "cache_set", "cache_hit", "cache_miss", "middleware"
        };
        
        foreach (var str in commonStrings)
        {
            _stringCache[str] = str; // 인터닝 효과
        }
    }
    
    /// <summary>
    /// 정수를 문자열로 변환 (캐싱됨)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string IntToString(int value)
    {
        if (_intStringCache.TryGetValue(value, out var cached))
            return cached;
            
        if (_intStringCache.Count >= MaxCacheSize)
            return MemoryUtils.IntToString(value); // 캐시 없이 직접 변환
            
        var result = MemoryUtils.IntToString(value);
        _intStringCache.TryAdd(value, result);
        return result;
    }
    
    /// <summary>
    /// Long을 문자열로 변환 (캐싱됨)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string LongToString(long value)
    {
        if (_longStringCache.TryGetValue(value, out var cached))
            return cached;
            
        if (_longStringCache.Count >= MaxCacheSize)
            return MemoryUtils.LongToString(value);
            
        var result = MemoryUtils.LongToString(value);
        _longStringCache.TryAdd(value, result);
        return result;
    }
    
    /// <summary>
    /// 백분율 포맷팅 (캐싱됨)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string FormatPercentage(double ratio, int decimalPlaces = 1)
    {
        // 소수점 1자리까지만 캐싱 (메모리 효율성)
        if (decimalPlaces == 1 && _percentageCache.TryGetValue(ratio, out var cached))
            return cached;
            
        var result = MemoryUtils.FormatPercentage(ratio, decimalPlaces);
        
        if (decimalPlaces == 1 && _percentageCache.Count < MaxCacheSize)
            _percentageCache.TryAdd(ratio, result);
            
        return result;
    }
    
    /// <summary>
    /// 바이트 크기 포맷팅 (캐싱됨)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string FormatByteSize(long bytes)
    {
        if (_byteSizeCache.TryGetValue(bytes, out var cached))
            return cached;
            
        if (_byteSizeCache.Count >= MaxCacheSize)
            return MemoryUtils.FormatByteSize(bytes);
            
        var result = MemoryUtils.FormatByteSize(bytes);
        _byteSizeCache.TryAdd(bytes, result);
        return result;
    }
    
    /// <summary>
    /// 문자열 인터닝 (자주 사용되는 문자열 캐싱)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string InternString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
            
        if (_stringCache.TryGetValue(value, out var cached))
            return cached;
            
        if (_stringCache.Count >= MaxCacheSize)
            return value;
            
        _stringCache.TryAdd(value, value);
        return value;
    }
    
    /// <summary>
    /// 캐시 통계 정보
    /// </summary>
    public static CacheStats GetCacheStats()
    {
        return new CacheStats
        {
            StringCacheSize = _stringCache.Count,
            IntCacheSize = _intStringCache.Count,
            LongCacheSize = _longStringCache.Count,
            PercentageCacheSize = _percentageCache.Count,
            ByteSizeCacheSize = _byteSizeCache.Count,
            TotalCacheSize = _stringCache.Count + _intStringCache.Count + 
                           _longStringCache.Count + _percentageCache.Count + 
                           _byteSizeCache.Count
        };
    }
    
    /// <summary>
    /// 캐시 정리 (메모리 압박 시 사용)
    /// </summary>
    public static void ClearCaches()
    {
        _stringCache.Clear();
        _intStringCache.Clear();
        _longStringCache.Clear();
        _percentageCache.Clear();
        _byteSizeCache.Clear();
        
        // 다시 기본값들 채우기
        PrePopulateCommonValues();
    }
}

/// <summary>
/// 캐시 통계 정보
/// </summary>
public readonly struct CacheStats
{
    public int StringCacheSize { get; init; }
    public int IntCacheSize { get; init; }
    public int LongCacheSize { get; init; }
    public int PercentageCacheSize { get; init; }
    public int ByteSizeCacheSize { get; init; }
    public int TotalCacheSize { get; init; }
    
    public override string ToString()
    {
        return $"LazyCache Stats - Total: {TotalCacheSize}, " +
               $"String: {StringCacheSize}, Int: {IntCacheSize}, " +
               $"Long: {LongCacheSize}, Percentage: {PercentageCacheSize}, " +
               $"ByteSize: {ByteSizeCacheSize}";
    }
}