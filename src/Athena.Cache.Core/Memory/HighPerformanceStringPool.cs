using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

namespace Athena.Cache.Core.Memory;

/// <summary>
/// 고성능 문자열 풀링 시스템
/// 문자열 할당을 최소화하고 재사용성을 극대화
/// </summary>
public static class HighPerformanceStringPool
{
    // 길이별 문자열 풀 (더 효율적인 관리)
    private static readonly ConcurrentDictionary<int, ConcurrentQueue<StringBuilder>> _stringBuilderPools = new();
    private static readonly ConcurrentDictionary<string, WeakReference<string>> _internPool = new();
    
    // 자주 사용되는 문자열 템플릿들
    private static readonly ConcurrentDictionary<string, string> _templateCache = new();
    
    private const int MaxPoolSize = 100;
    private const int MaxStringLength = 4096;
    private const int MaxInternPoolSize = 1000;
    
    /// <summary>
    /// StringBuilder를 풀에서 대여 (길이별로 최적화)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder RentStringBuilder(int capacity = 256)
    {
        capacity = Math.Min(capacity, MaxStringLength);
        var pool = _stringBuilderPools.GetOrAdd(capacity, _ => new ConcurrentQueue<StringBuilder>());
        
        if (pool.TryDequeue(out var sb))
        {
            sb.Clear();
            return sb;
        }
        
        return new StringBuilder(capacity);
    }
    
    /// <summary>
    /// StringBuilder를 풀에 반환
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnStringBuilder(StringBuilder sb, int originalCapacity = 256)
    {
        if (sb == null || sb.Capacity > MaxStringLength) return;
        
        originalCapacity = Math.Min(originalCapacity, MaxStringLength);
        var pool = _stringBuilderPools.GetOrAdd(originalCapacity, _ => new ConcurrentQueue<StringBuilder>());
        
        // 풀 크기 제한
        if (GetPoolSize(pool) < MaxPoolSize)
        {
            sb.Clear();
            pool.Enqueue(sb);
        }
    }
    
    /// <summary>
    /// 약한 참조 기반 문자열 인터닝 (메모리 누수 방지)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string InternWeakly(string str)
    {
        if (string.IsNullOrEmpty(str) || str.Length > 200) 
            return str; // 너무 긴 문자열은 인터닝하지 않음
        
        if (_internPool.TryGetValue(str, out var weakRef) && 
            weakRef.TryGetTarget(out var cachedStr))
        {
            return cachedStr;
        }
        
        // 풀 크기 제한
        if (_internPool.Count >= MaxInternPoolSize)
        {
            CleanupInternPool();
        }
        
        _internPool[str] = new WeakReference<string>(str);
        return str;
    }
    
    /// <summary>
    /// 템플릿 기반 문자열 생성 (포맷 문자열 캐싱)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string FormatWithTemplate(string template, params object[] args)
    {
        var cacheKey = $"{template}:{args.Length}";
        
        if (!_templateCache.TryGetValue(cacheKey, out var cachedTemplate))
        {
            cachedTemplate = template;
            if (_templateCache.Count < MaxInternPoolSize)
            {
                _templateCache[cacheKey] = cachedTemplate;
            }
        }
        
        return string.Format(cachedTemplate, args);
    }
    
    /// <summary>
    /// 고성능 문자열 결합 (Span 기반)
    /// </summary>
    public static string ConcatenateEfficiently(ReadOnlySpan<string> strings, char separator = '\0')
    {
        if (strings.IsEmpty) return string.Empty;
        if (strings.Length == 1) return strings[0];
        
        // 총 길이 계산
        var totalLength = 0;
        for (int i = 0; i < strings.Length; i++)
        {
            totalLength += strings[i]?.Length ?? 0;
            if (separator != '\0' && i < strings.Length - 1)
                totalLength++; // 구분자 길이
        }
        
        // 효율적인 문자열 생성
        return string.Create(totalLength, (strings: strings.ToArray(), separator), 
            static (span, state) =>
            {
                var position = 0;
                for (int i = 0; i < state.strings.Length; i++)
                {
                    var str = state.strings[i];
                    if (!string.IsNullOrEmpty(str))
                    {
                        str.AsSpan().CopyTo(span.Slice(position));
                        position += str.Length;
                    }
                    
                    if (state.separator != '\0' && i < state.strings.Length - 1)
                    {
                        span[position++] = state.separator;
                    }
                }
            });
    }
    
    /// <summary>
    /// 메모리 압박 시 인터닝 풀 정리
    /// </summary>
    private static void CleanupInternPool()
    {
        var keysToRemove = new List<string>();
        
        foreach (var kvp in _internPool)
        {
            if (!kvp.Value.TryGetTarget(out _))
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            _internPool.TryRemove(key, out _);
        }
    }
    
    /// <summary>
    /// StringBuilder 풀 크기 확인 (대략적)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetPoolSize(ConcurrentQueue<StringBuilder> pool)
    {
        // ConcurrentQueue는 Count 속성이 비싸므로 대략적으로 추정
        var tempList = new List<StringBuilder>();
        var count = 0;
        
        while (pool.TryDequeue(out var item) && count < MaxPoolSize + 10)
        {
            tempList.Add(item);
            count++;
        }
        
        // 다시 큐에 넣기
        foreach (var item in tempList)
        {
            pool.Enqueue(item);
        }
        
        return count;
    }
    
    /// <summary>
    /// 풀 통계 정보
    /// </summary>
    public static PoolStats GetPoolStats()
    {
        return new PoolStats
        {
            StringBuilderPoolCount = _stringBuilderPools.Count,
            InternPoolCount = _internPool.Count,
            TemplateCacheCount = _templateCache.Count,
            EstimatedMemoryUsage = EstimateMemoryUsage()
        };
    }
    
    /// <summary>
    /// 예상 메모리 사용량 계산
    /// </summary>
    private static long EstimateMemoryUsage()
    {
        long total = 0;
        
        // StringBuilder 풀 메모리
        total += _stringBuilderPools.Count * 1024; // 대략적 추정
        
        // 인터닝 풀 메모리  
        total += _internPool.Count * 100; // 평균 문자열 길이 추정
        
        // 템플릿 캐시 메모리
        total += _templateCache.Count * 50; // 평균 템플릿 길이 추정
        
        return total;
    }
    
    /// <summary>
    /// 전체 풀 정리 (메모리 압박 시 사용)
    /// </summary>
    public static void ClearAllPools()
    {
        _stringBuilderPools.Clear();
        _internPool.Clear();
        _templateCache.Clear();
    }
}

/// <summary>
/// 문자열 풀 통계 정보
/// </summary>
public readonly struct PoolStats
{
    public int StringBuilderPoolCount { get; init; }
    public int InternPoolCount { get; init; }
    public int TemplateCacheCount { get; init; }
    public long EstimatedMemoryUsage { get; init; }
    
    public override string ToString()
    {
        return $"StringPool Stats - StringBuilder: {StringBuilderPoolCount}, " +
               $"Intern: {InternPoolCount}, Template: {TemplateCacheCount}, " +
               $"Memory: {LazyCache.FormatByteSize(EstimatedMemoryUsage)}";
    }
}