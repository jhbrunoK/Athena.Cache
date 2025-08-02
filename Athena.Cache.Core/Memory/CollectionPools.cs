using Microsoft.Extensions.ObjectPool;
using System.Collections.Concurrent;
using System.Text;
using Athena.Cache.Core.Analytics;

namespace Athena.Cache.Core.Memory;

/// <summary>
/// 컬렉션 재사용을 위한 ObjectPool 정책들
/// 메모리 할당을 최소화하여 GC 압박을 줄임
/// </summary>
public static class CollectionPools
{
    // List<T> 풀들
    private static readonly ObjectPool<List<OptimizationRecommendation>> _optimizationRecommendationPool;
    private static readonly ObjectPool<List<string>> _stringPool;
    
    // Dictionary 풀들
    private static readonly ObjectPool<Dictionary<string, object>> _stringObjectDictionaryPool;
    private static readonly ObjectPool<Dictionary<string, double>> _stringDoublePool;
    
    // StringBuilder 풀
    private static readonly ObjectPool<StringBuilder> _stringBuilderPool;

    static CollectionPools()
    {
        var provider = new DefaultObjectPoolProvider();
        
        _optimizationRecommendationPool = provider.Create(new ListPoolPolicy<OptimizationRecommendation>());
        _stringPool = provider.Create(new ListPoolPolicy<string>());
        
        _stringObjectDictionaryPool = provider.Create(new DictionaryPoolPolicy<string, object>());
        _stringDoublePool = provider.Create(new DictionaryPoolPolicy<string, double>());
        
        _stringBuilderPool = provider.Create(new StringBuilderPooledObjectPolicy());
    }

    // List<T> 대여/반환
    public static List<OptimizationRecommendation> RentOptimizationRecommendationList() => _optimizationRecommendationPool.Get();
    public static void Return(List<OptimizationRecommendation> list) => _optimizationRecommendationPool.Return(list);
    
    public static List<string> RentStringList() => _stringPool.Get();
    public static void Return(List<string> list) => _stringPool.Return(list);
    

    // Dictionary 대여/반환
    public static Dictionary<string, object> RentStringObjectDictionary() => _stringObjectDictionaryPool.Get();
    public static void Return(Dictionary<string, object> dict) => _stringObjectDictionaryPool.Return(dict);
    
    public static Dictionary<string, double> RentStringDoubleDictionary() => _stringDoublePool.Get();
    public static void Return(Dictionary<string, double> dict) => _stringDoublePool.Return(dict);
    
    // StringBuilder 대여/반환
    public static StringBuilder RentStringBuilder() => _stringBuilderPool.Get();
    public static void Return(StringBuilder sb) => _stringBuilderPool.Return(sb);
}

/// <summary>
/// List<T> 객체 풀 정책
/// </summary>
public class ListPoolPolicy<T> : PooledObjectPolicy<List<T>>
{
    private const int MaxCapacity = 1024; // 메모리 누수 방지

    public override List<T> Create() => new List<T>();

    public override bool Return(List<T> obj)
    {
        if (obj == null || obj.Count > MaxCapacity)
            return false;

        obj.Clear();
        return true;
    }
}

/// <summary>
/// Dictionary<TKey, TValue> 객체 풀 정책
/// </summary>
public class DictionaryPoolPolicy<TKey, TValue> : PooledObjectPolicy<Dictionary<TKey, TValue>>
    where TKey : notnull
{
    private const int MaxCapacity = 1024; // 메모리 누수 방지

    public override Dictionary<TKey, TValue> Create() => new Dictionary<TKey, TValue>();

    public override bool Return(Dictionary<TKey, TValue> obj)
    {
        if (obj == null || obj.Count > MaxCapacity)
            return false;

        obj.Clear();
        return true;
    }
}