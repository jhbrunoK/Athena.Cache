using Microsoft.Extensions.ObjectPool;
using System.Text;

namespace Athena.Cache.Core.Memory;

/// <summary>
/// 컬렉션 재사용을 위한 ObjectPool 정책들
/// 메모리 할당을 최소화하여 GC 압박을 줄임
/// </summary>
public static class CollectionPools
{
    // List<T> 풀들
    private static readonly ObjectPool<List<string>> _stringPool;
    
    // Dictionary 풀들
    private static readonly ObjectPool<Dictionary<string, object>> _stringObjectDictionaryPool;
    private static readonly ObjectPool<Dictionary<string, double>> _stringDoublePool;
    
    // StringBuilder 풀
    private static readonly ObjectPool<StringBuilder> _stringBuilderPool;

    static CollectionPools()
    {
        var provider = new DefaultObjectPoolProvider();
        
        _stringPool = provider.Create(new ListPoolPolicy<string>());
        
        _stringObjectDictionaryPool = provider.Create(new DictionaryPoolPolicy<string, object>());
        _stringDoublePool = provider.Create(new DictionaryPoolPolicy<string, double>());
        
        _stringBuilderPool = provider.Create(new StringBuilderPooledObjectPolicy());
    }

    // List<T> 대여/반환
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
    
    /// <summary>
    /// 모든 풀 정리 (메모리 압박 시 사용)
    /// </summary>
    public static void ClearAll()
    {
        // ObjectPool은 기본적으로 Clear 메서드가 없으므로
        // 새로운 인스턴스로 재생성하는 것보다 기존 풀을 유지하되
        // 임시로 많은 객체를 요청해서 풀을 비우는 방식 사용
        var tempLists = new List<object>();
        try
        {
            // 각 풀에서 최대 100개씩 가져와서 풀 비우기
            for (int i = 0; i < 100; i++)
            {
                tempLists.Add(_stringPool.Get());
                tempLists.Add(_stringObjectDictionaryPool.Get());
                tempLists.Add(_stringDoublePool.Get());
                tempLists.Add(_stringBuilderPool.Get());
            }
        }
        finally
        {
            // 모든 객체 반환하지 않음 - GC가 정리하도록 함
            tempLists.Clear();
        }
    }
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
