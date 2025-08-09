using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Memory;
using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;

namespace Athena.Cache.Core.Implementations;

/// <summary>
/// 기본 캐시 키 생성기 구현 (키 캐싱 포함)
/// </summary>
public class DefaultCacheKeyGenerator(AthenaCacheOptions options) : ICacheKeyGenerator
{
    // ConcurrentDictionary 최적화: 동시성 수준 설정
    // CPU 코어 수의 2배
    // 초기 용량 설정

    // LRU 캐시로 자주 사용되는 키 저장 (메모리 효율적)
    private readonly ConcurrentDictionary<string, string> _keyCache = new(
        concurrencyLevel: Environment.ProcessorCount * 2, // CPU 코어 수의 2배
        capacity: MaxCacheSize // 초기 용량 설정
    );
    private const int MaxCacheSize = 1000; // 최대 캐시 크기
    private long _cacheCount = 0; // 캐시 아이템 수 추적 (Interlocked로 접근)
    /// <summary>
    /// API 요청 기반 캐시 키 생성 (키 캐싱 포함) - 비동기 최적화 버전
    /// {Namespace}_{Version}_{Controller}_{Action}_{ParameterHash}
    /// 예: MyApp_PROD_v1.2_UsersController_GetUsers_ABC123
    /// </summary>
    public ValueTask<string> GenerateKeyAsync(string controller, string action, IDictionary<string, object?>? parameters = null)
    {
        // 캐시 키 조회를 위한 요청 식별자 생성
        var requestId = $"{controller}:{action}:{GenerateParameterHash(parameters)}";
        
        // 키 캐시 확인 (동기적으로 빠르게 처리)
        if (_keyCache.TryGetValue(requestId, out var cachedKey))
        {
            return new ValueTask<string>(cachedKey);
        }

        var keyParts = new List<string>();

        // 네임스페이스 추가
        if (!string.IsNullOrEmpty(options.Namespace))
        {
            keyParts.Add(options.Namespace);
        }

        // 버전 추가
        if (!string.IsNullOrEmpty(options.VersionKey))
        {
            keyParts.Add(options.VersionKey);
        }

        // 컨트롤러명 추가 (Controller 접미사 제거)
        var cleanController = controller.EndsWith(CachedConstants.ControllerSuffix)
            ? controller[..^CachedConstants.ControllerSuffix.Length] // "Controller" 제거
            : controller;
        keyParts.Add(cleanController);

        // 액션명 추가
        keyParts.Add(action);

        // 파라미터 해시 추가 (이미 계산됨)
        var parameterHash = GenerateParameterHash(parameters);
        if (!string.IsNullOrEmpty(parameterHash))
        {
            keyParts.Add(parameterHash);
        }

        // 고성능 문자열 결합 사용
        var finalKey = HighPerformanceStringPool.ConcatenateEfficiently(
            keyParts.ToArray().AsSpan(), options.KeySeparator[0]);
        
        // 키 캐시에 저장 (크기 제한) - Lock-free 최적화
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
    /// 동기 버전 유지 (하위 호환성)
    /// </summary>
    public string GenerateKey(string controller, string action, IDictionary<string, object?>? parameters = null)
    {
        return GenerateKeyAsync(controller, action, parameters).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 테이블 추적용 키 생성
    /// 예: MyApp_PROD_table_Users
    /// </summary>
    public string GenerateTableTrackingKey(string tableName)
    {
        var keyParts = new List<string>();

        // 네임스페이스 추가
        if (!string.IsNullOrEmpty(options.Namespace))
        {
            keyParts.Add(options.Namespace);
        }

        // 버전 추가
        if (!string.IsNullOrEmpty(options.VersionKey))
        {
            keyParts.Add(options.VersionKey);
        }

        // 테이블 추적 접두사 추가
        keyParts.Add(options.TableTrackingPrefix);

        // 테이블명 추가
        keyParts.Add(tableName);

        // 고성능 문자열 결합 사용
        return HighPerformanceStringPool.ConcatenateEfficiently(
            keyParts.ToArray().AsSpan(), options.KeySeparator[0]);
    }

    /// <summary>
    /// 파라미터를 해시로 변환
    /// 1. null/빈 값 제거
    /// 2. 키 알파벳 순 정렬
    /// 3. JSON 직렬화
    /// 4. XxHash64 해싱 (고성능)
    /// </summary>
    public string GenerateParameterHash(IDictionary<string, object?>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return string.Empty;
        }

        // null이나 빈 값 제거 후 정렬 (LINQ 없이, zero allocation)
        var validKeys = new List<string>(parameters.Count);
        var validValues = new List<object?>(parameters.Count);
        
        // 1단계: 유효한 키-값 쌍 필터링
        foreach (var kvp in parameters)
        {
            if (kvp.Value != null && !IsEmptyValue(kvp.Value))
            {
                validKeys.Add(kvp.Key);
                validValues.Add(NormalizeValue(kvp.Value));
            }
        }
        
        if (validKeys.Count == 0)
        {
            return string.Empty;
        }
        
        // 2단계: 키 기준으로 인덱스 정렬
        var indices = new int[validKeys.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }
        
        Array.Sort(indices, (i, j) => StringComparer.Ordinal.Compare(validKeys[i], validKeys[j]));
        
        // 3단계: 정렬된 Dictionary 생성
        var filteredParams = new Dictionary<string, object?>(validKeys.Count);
        for (int i = 0; i < indices.Length; i++)
        {
            var idx = indices[i];
            filteredParams[validKeys[idx]] = validValues[idx];
        }

        if (filteredParams.Count == 0)
        {
            return string.Empty;
        }

        // JSON 직렬화 (일관성을 위해 옵션 고정)
        var json = JsonSerializer.Serialize(filteredParams, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        // XxHash3 해싱 (최신 성능 최적화)
        return ComputeXxHash3(json);
    }

    /// <summary>
    /// 빈 값 체크
    /// </summary>
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

    /// <summary>
    /// 값 정규화 (일관된 해시를 위해)
    /// </summary>
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

    /// <summary>
    /// XxHash3 해시 계산 (Span 기반 zero allocation)
    /// </summary>
    private static string ComputeXxHash3(string input)
    {
        // Span으로 UTF8 바이트 계산 (allocation 없이)
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(input.Length);
        Span<byte> buffer = maxByteCount <= 1024 
            ? stackalloc byte[maxByteCount]  // 스택 할당 (1KB 이하)
            : new byte[maxByteCount];       // 힙 할당 (큰 데이터)
        
        var actualByteCount = Encoding.UTF8.GetBytes(input.AsSpan(), buffer);
        var inputSpan = buffer.Slice(0, actualByteCount);
        
        var hashValue = XxHash3.HashToUInt64(inputSpan);
        
        // Base36 인코딩으로 짧고 안전한 문자열 생성
        return ConvertToBase36(hashValue);
    }

    // Base36 변환 결과 캐싱 (자주 사용되는 해시값들)
    private static readonly ConcurrentDictionary<ulong, string> _base36Cache = new();
    private const int MaxBase36CacheSize = 500; // 메모리 제한
    
    /// <summary>
    /// UInt64를 Base36 문자열로 변환 (Span 기반 zero allocation + 캐싱)
    /// </summary>
    private static string ConvertToBase36(ulong value)
    {
        if (value == 0) return CachedConstants.Zero;

        // 캐시에서 먼저 확인
        if (_base36Cache.TryGetValue(value, out var cached))
            return cached;

        ReadOnlySpan<char> chars = "0123456789abcdefghijklmnopqrstuvwxyz";
        
        // 최대 13자리 (log36(2^64) ≈ 12.4)
        Span<char> buffer = stackalloc char[13];
        int index = buffer.Length;
        
        while (value > 0)
        {
            buffer[--index] = chars[(int)(value % 36)];
            value /= 36;
        }
        
        var result = new string(buffer.Slice(index));
        
        // 캐시 크기 제한 확인 후 저장
        if (_base36Cache.Count < MaxBase36CacheSize)
        {
            _base36Cache.TryAdd(value, result);
        }
        
        return result;
    }
}