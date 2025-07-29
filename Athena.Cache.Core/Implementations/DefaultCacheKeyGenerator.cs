using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;

namespace Athena.Cache.Core.Implementations;

/// <summary>
/// 기본 캐시 키 생성기 구현 (키 캐싱 포함)
/// </summary>
public class DefaultCacheKeyGenerator : ICacheKeyGenerator
{
    private readonly AthenaCacheOptions options;
    
    public DefaultCacheKeyGenerator(AthenaCacheOptions options)
    {
        this.options = options;
        
        // ConcurrentDictionary 최적화: 동시성 수준 설정
        _keyCache = new ConcurrentDictionary<string, string>(
            concurrencyLevel: Environment.ProcessorCount * 2, // CPU 코어 수의 2배
            capacity: MaxCacheSize // 초기 용량 설정
        );
    }
    // LRU 캐시로 자주 사용되는 키 저장 (메모리 효율적)
    private readonly ConcurrentDictionary<string, string> _keyCache;
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
        var cleanController = controller.EndsWith("Controller")
            ? controller[..^10] // "Controller" 제거
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

        var finalKey = string.Join(options.KeySeparator, keyParts);
        
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

        return string.Join(options.KeySeparator, keyParts);
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

        // null이나 빈 값 제거 후 정렬
        var filteredParams = parameters
            .Where(kvp => kvp.Value != null && !IsEmptyValue(kvp.Value))
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToDictionary(kvp => kvp.Key, kvp => NormalizeValue(kvp.Value));

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

        // XxHash64 해싱 (성능 최적화)
        return ComputeXxHash64(json);
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
    /// XxHash64 해시 계산 (고성능 해시 함수)
    /// </summary>
    private static string ComputeXxHash64(string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashValue = XxHash64.HashToUInt64(inputBytes);
        
        // Base36 인코딩으로 짧고 안전한 문자열 생성
        return ConvertToBase36(hashValue);
    }

    /// <summary>
    /// UInt64를 Base36 문자열로 변환 (0-9, a-z 사용)
    /// </summary>
    private static string ConvertToBase36(ulong value)
    {
        const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value == 0) return "0";

        var result = new StringBuilder();
        while (value > 0)
        {
            result.Insert(0, chars[(int)(value % 36)]);
            value /= 36;
        }
        
        return result.ToString();
    }
}