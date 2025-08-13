using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;

namespace Athena.Cache.FusionCache.Implementations;

/// <summary>
/// FusionCache GetOrSet 패턴에 최적화된 캐시 키 생성기
/// Athena.Cache의 정교한 키 맹글링과 FusionCache의 패턴을 결합
/// </summary>
public class FusionCacheKeyGenerator(AthenaCacheOptions options) : ICacheKeyGenerator
{
    private readonly AthenaCacheOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ConcurrentDictionary<string, string> _keyCache = new(
        concurrencyLevel: Environment.ProcessorCount * 2,
        capacity: MaxCacheSize
    );
    private const int MaxCacheSize = 1000;
    private long _cacheCount = 0;

    /// <summary>
    /// FusionCache GetOrSet에 최적화된 비동기 키 생성
    /// </summary>
    public ValueTask<string> GenerateKeyAsync(string controller, string action, IDictionary<string, object?>? parameters = null)
    {
        var requestId = $"{controller}:{action}:{GenerateParameterHash(parameters)}";
        
        if (_keyCache.TryGetValue(requestId, out var cachedKey))
        {
            return new ValueTask<string>(cachedKey);
        }

        var keyParts = new List<string>();

        // 네임스페이스 추가
        if (!string.IsNullOrEmpty(_options.Namespace))
        {
            keyParts.Add(_options.Namespace);
        }

        // 버전 추가
        if (!string.IsNullOrEmpty(_options.VersionKey))
        {
            keyParts.Add(_options.VersionKey);
        }

        // 컨트롤러명 추가 (Controller 접미사 제거)
        var cleanController = controller.EndsWith("Controller")
            ? controller[..^10] // "Controller" 제거
            : controller;
        keyParts.Add(cleanController);

        // 액션명 추가
        keyParts.Add(action);

        // 파라미터 해시 추가
        var parameterHash = GenerateParameterHash(parameters);
        if (!string.IsNullOrEmpty(parameterHash))
        {
            keyParts.Add(parameterHash);
        }

        // FusionCache에 적합한 키 형식 생성 (콜론(:) 구분자 사용)
        var finalKey = string.Join(":", keyParts);
        
        // 키 캐시에 저장
        if (Interlocked.Read(ref _cacheCount) < MaxCacheSize)
        {
            if (_keyCache.TryAdd(requestId, finalKey))
            {
                Interlocked.Increment(ref _cacheCount);
            }
        }

        return new ValueTask<string>(finalKey);
    }

    /// <summary>
    /// 동기 버전 (하위 호환성)
    /// </summary>
    public string GenerateKey(string controller, string action, IDictionary<string, object?>? parameters = null)
    {
        return GenerateKeyAsync(controller, action, parameters).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 테이블 추적용 태그 키 생성 (FusionCache 태그와 호환)
    /// </summary>
    public string GenerateTableTrackingKey(string tableName)
    {
        var keyParts = new List<string>();

        if (!string.IsNullOrEmpty(_options.Namespace))
        {
            keyParts.Add(_options.Namespace);
        }

        if (!string.IsNullOrEmpty(_options.VersionKey))
        {
            keyParts.Add(_options.VersionKey);
        }

        keyParts.Add("tag");
        keyParts.Add(tableName);

        return string.Join(":", keyParts);
    }

    /// <summary>
    /// FusionCache GetOrSet 팩토리 함수용 키 생성
    /// </summary>
    public string GenerateFactoryKey(string baseKey, IDictionary<string, object?>? context = null)
    {
        if (context == null || context.Count == 0)
        {
            return baseKey;
        }

        var contextHash = GenerateParameterHash(context);
        return string.IsNullOrEmpty(contextHash) ? baseKey : $"{baseKey}:{contextHash}";
    }

    /// <summary>
    /// 파라미터 해시 생성 (XxHash3 + Base36 최적화)
    /// </summary>
    public string GenerateParameterHash(IDictionary<string, object?>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return string.Empty;
        }

        var validParams = new Dictionary<string, object?>();
        
        foreach (var kvp in parameters)
        {
            if (kvp.Value != null && !IsEmptyValue(kvp.Value))
            {
                validParams[kvp.Key] = NormalizeValue(kvp.Value);
            }
        }

        if (validParams.Count == 0)
        {
            return string.Empty;
        }

        // 키 기준 정렬
        var sortedParams = validParams
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var json = JsonSerializer.Serialize(sortedParams, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        return ComputeXxHash3(json);
    }

    /// <summary>
    /// FusionCache 태그 배열 생성 (테이블 이름 기반)
    /// </summary>
    public string[] GenerateTableTags(params string[] tableNames)
    {
        if (tableNames == null || tableNames.Length == 0)
        {
            return [];
        }

        return tableNames.Select(tableName => 
        {
            var keyParts = new List<string>();
            
            if (!string.IsNullOrEmpty(_options.Namespace))
            {
                keyParts.Add(_options.Namespace);
            }
            
            keyParts.Add(tableName);
            
            return string.Join(":", keyParts);
        }).ToArray();
    }

    /// <summary>
    /// GetOrSet 패턴용 캐시 키와 태그를 함께 생성
    /// </summary>
    public (string Key, string[] Tags) GenerateKeyWithTags(
        string controller, 
        string action, 
        IDictionary<string, object?>? parameters = null,
        params string[] tableNames)
    {
        var key = GenerateKey(controller, action, parameters);
        var tags = GenerateTableTags(tableNames);
        
        return (key, tags);
    }

    private static bool IsEmptyValue(object? value)
    {
        return value switch
        {
            null => true,
            string str => string.IsNullOrWhiteSpace(str),
            System.Collections.ICollection collection => collection.Count == 0,
            _ => false
        };
    }

    private static object? NormalizeValue(object? value)
    {
        return value switch
        {
            string str => str.Trim(),
            DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            decimal dec => dec.ToString("F"),
            float f => f.ToString("F"),
            double d => d.ToString("F"),
            _ => value
        };
    }

    private static string ComputeXxHash3(string input)
    {
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(input.Length);
        Span<byte> buffer = maxByteCount <= 1024 
            ? stackalloc byte[maxByteCount]
            : new byte[maxByteCount];
        
        var actualByteCount = Encoding.UTF8.GetBytes(input.AsSpan(), buffer);
        var inputSpan = buffer.Slice(0, actualByteCount);
        
        var hashValue = XxHash3.HashToUInt64(inputSpan);
        
        return ConvertToBase36(hashValue);
    }

    private static readonly ConcurrentDictionary<ulong, string> _base36Cache = new();
    private const int MaxBase36CacheSize = 500;
    
    private static string ConvertToBase36(ulong value)
    {
        if (value == 0) return "0";

        if (_base36Cache.TryGetValue(value, out var cached))
            return cached;

        ReadOnlySpan<char> chars = "0123456789abcdefghijklmnopqrstuvwxyz";
        
        Span<char> buffer = stackalloc char[13];
        int index = buffer.Length;
        
        while (value > 0)
        {
            buffer[--index] = chars[(int)(value % 36)];
            value /= 36;
        }
        
        var result = new string(buffer.Slice(index));
        
        if (_base36Cache.Count < MaxBase36CacheSize)
        {
            _base36Cache.TryAdd(value, result);
        }
        
        return result;
    }
}
