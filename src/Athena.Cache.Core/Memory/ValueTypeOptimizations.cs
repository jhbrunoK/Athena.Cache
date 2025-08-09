using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;

namespace Athena.Cache.Core.Memory;

/// <summary>
/// 값 타입 기반 고성능 최적화
/// 박싱/언박싱을 제거하고 메모리 레이아웃을 최적화
/// </summary>

/// <summary>
/// 캐시 메트릭을 위한 값 타입 구조체 (박싱 없음)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public readonly struct CacheMetrics
{
    public readonly long HitCount;
    public readonly long MissCount;
    public readonly long ErrorCount;
    public readonly long TotalOperations;
    public readonly double HitRatio;
    public readonly TimeSpan AverageResponseTime;
    public readonly long MemoryUsageBytes;
    
    public CacheMetrics(long hitCount, long missCount, long errorCount, 
                       TimeSpan averageResponseTime, long memoryUsageBytes)
    {
        HitCount = hitCount;
        MissCount = missCount;
        ErrorCount = errorCount;
        TotalOperations = hitCount + missCount + errorCount;
        HitRatio = TotalOperations > 0 ? (double)hitCount / TotalOperations : 0.0;
        AverageResponseTime = averageResponseTime;
        MemoryUsageBytes = memoryUsageBytes;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetFormattedHitRatio() => LazyCache.FormatPercentage(HitRatio);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetFormattedMemoryUsage() => LazyCache.FormatByteSize(MemoryUsageBytes);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsHealthy() => HitRatio >= 0.7 && ErrorCount < (TotalOperations * 0.01);
}

/// <summary>
/// 캐시 키 정보를 위한 값 타입 구조체
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct CacheKeyInfo
{
    public readonly int KeyHashCode;
    public readonly int KeyLength;
    public readonly DateTime CreatedAt;
    public readonly DateTime LastAccessedAt;
    public readonly int AccessCount;
    
    public CacheKeyInfo(string key, DateTime createdAt, DateTime lastAccessedAt, int accessCount)
    {
        KeyHashCode = key?.GetHashCode() ?? 0;
        KeyLength = key?.Length ?? 0;
        CreatedAt = createdAt;
        LastAccessedAt = lastAccessedAt;
        AccessCount = accessCount;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TimeSpan GetAge() => DateTime.UtcNow - CreatedAt;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TimeSpan GetTimeSinceLastAccess() => DateTime.UtcNow - LastAccessedAt;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsHot() => AccessCount > 10 && GetTimeSinceLastAccess().TotalMinutes < 5;
}

/// <summary>
/// 메모리 할당 없는 키-값 쌍 구조체
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public readonly struct KeyValueMetric<TKey, TValue> 
    where TKey : unmanaged 
    where TValue : unmanaged
{
    public readonly TKey Key;
    public readonly TValue Value;
    public readonly DateTime Timestamp;
    
    public KeyValueMetric(TKey key, TValue value)
    {
        Key = key;
        Value = value;
        Timestamp = DateTime.UtcNow;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsExpired(TimeSpan expiration) => DateTime.UtcNow - Timestamp > expiration;
}

/// <summary>
/// 고성능 통계 계산기 (값 타입 기반)
/// </summary>
public static class ValueTypeStatistics
{
    /// <summary>
    /// 배열의 평균을 계산 (제네릭 + 값 타입)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CalculateAverage<T>(ReadOnlySpan<T> values) where T : struct, INumber<T>
    {
        if (values.IsEmpty) return 0.0;
        
        T sum = T.Zero;
        foreach (var value in values)
        {
            sum += value;
        }
        
        return double.CreateChecked(sum) / values.Length;
    }
    
    /// <summary>
    /// 배열의 최대값을 찾기 (제네릭 + 값 타입)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T FindMaximum<T>(ReadOnlySpan<T> values) where T : struct, IComparable<T>
    {
        if (values.IsEmpty) return default;
        
        var max = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i].CompareTo(max) > 0)
                max = values[i];
        }
        
        return max;
    }
    
    /// <summary>
    /// 배열의 최소값을 찾기 (제네릭 + 값 타입)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T FindMinimum<T>(ReadOnlySpan<T> values) where T : struct, IComparable<T>
    {
        if (values.IsEmpty) return default;
        
        var min = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i].CompareTo(min) < 0)
                min = values[i];
        }
        
        return min;
    }
    
    /// <summary>
    /// 백분위수 계산 (메모리 할당 없음)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CalculatePercentile<T>(ReadOnlySpan<T> sortedValues, double percentile) 
        where T : struct, INumber<T>
    {
        if (sortedValues.IsEmpty) return 0.0;
        if (percentile <= 0) return double.CreateChecked(sortedValues[0]);
        if (percentile >= 1) return double.CreateChecked(sortedValues[^1]);
        
        var index = percentile * (sortedValues.Length - 1);
        var lowerIndex = (int)Math.Floor(index);
        var upperIndex = (int)Math.Ceiling(index);
        
        if (lowerIndex == upperIndex)
            return double.CreateChecked(sortedValues[lowerIndex]);
        
        var lowerValue = double.CreateChecked(sortedValues[lowerIndex]);
        var upperValue = double.CreateChecked(sortedValues[upperIndex]);
        var weight = index - lowerIndex;
        
        return lowerValue + (upperValue - lowerValue) * weight;
    }
}

/// <summary>
/// 고성능 비트 연산 유틸리티
/// </summary>
public static class BitOptimizations
{
    /// <summary>
    /// 빠른 2의 거듭제곱 체크
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOfTwo(uint value) => value != 0 && (value & (value - 1)) == 0;
    
    /// <summary>
    /// 다음 2의 거듭제곱 찾기 (캐시 크기 최적화용)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint NextPowerOfTwo(uint value)
    {
        if (value == 0) return 1;
        if (IsPowerOfTwo(value)) return value;
        
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        
        return value + 1;
    }
    
    /// <summary>
    /// 빠른 해시 결합 (FNV-like)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint CombineHashes(uint hash1, uint hash2)
    {
        return hash1 ^ (hash2 + 0x9e3779b9 + (hash1 << 6) + (hash1 >> 2));
    }
    
    /// <summary>
    /// 빠른 나머지 계산 (2의 거듭제곱 모듈로용)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint FastModulo(uint value, uint powerOfTwoModulus)
    {
        return value & (powerOfTwoModulus - 1);
    }
}

/// <summary>
/// 메모리 정렬 최적화된 버퍼 (안전한 버전)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 64)] // 캐시 라인 정렬
public struct AlignedBuffer
{
    private readonly byte[] _buffer;
    
    public AlignedBuffer()
    {
        _buffer = new byte[4096]; // 4KB 페이지 크기에 맞춤
    }
    
    public Span<byte> AsSpan() => _buffer.AsSpan();
    
    public ReadOnlySpan<byte> AsReadOnlySpan() => _buffer.AsSpan();
    
    public int Length => _buffer.Length;
}

/// <summary>
/// CPU 캐시 친화적인 해시맵 (값 타입 전용)
/// </summary>
public struct ValueTypeHashMap<TKey, TValue> 
    where TKey : unmanaged, IEquatable<TKey>
    where TValue : unmanaged
{
    private KeyValueMetric<TKey, TValue>[] _buckets;
    private uint _size;
    private uint _mask;
    
    public ValueTypeHashMap(int capacity = 16)
    {
        var size = BitOptimizations.NextPowerOfTwo((uint)Math.Max(capacity, 16));
        _buckets = new KeyValueMetric<TKey, TValue>[size];
        _size = size;
        _mask = size - 1;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(TKey key, out TValue value)
    {
        var hash = (uint)key.GetHashCode();
        var index = BitOptimizations.FastModulo(hash, _size);
        
        ref var bucket = ref _buckets[index];
        if (bucket.Key.Equals(key))
        {
            value = bucket.Value;
            return true;
        }
        
        value = default;
        return false;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(TKey key, TValue value)
    {
        var hash = (uint)key.GetHashCode();
        var index = BitOptimizations.FastModulo(hash, _size);
        
        _buckets[index] = new KeyValueMetric<TKey, TValue>(key, value);
    }
}