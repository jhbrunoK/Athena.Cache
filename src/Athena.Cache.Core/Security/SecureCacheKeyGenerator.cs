using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Memory;

namespace Athena.Cache.Core.Security;

/// <summary>
/// 보안이 강화된 캐시 키 생성기
/// 민감한 데이터 감지 및 안전한 해싱 기능 제공
/// </summary>
public class SecureCacheKeyGenerator : ICacheKeyGenerator
{
    private readonly ILogger<SecureCacheKeyGenerator> _logger;
    private readonly SecureCacheKeyOptions _options;

    public SecureCacheKeyGenerator(
        ILogger<SecureCacheKeyGenerator> logger,
        SecureCacheKeyOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new SecureCacheKeyOptions();
    }

    /// <summary>
    /// API 요청 파라미터를 기반으로 캐시 키 생성 (동기)
    /// </summary>
    public string GenerateKey(string controller, string action, IDictionary<string, object?>? parameters = null)
    {
        return GenerateSecureKey(controller, action, parameters);
    }

    /// <summary>
    /// API 요청 파라미터를 기반으로 캐시 키 생성 (비동기 최적화)
    /// </summary>
    public ValueTask<string> GenerateKeyAsync(string controller, string action, IDictionary<string, object?>? parameters = null)
    {
        return ValueTask.FromResult(GenerateSecureKey(controller, action, parameters));
    }

    /// <summary>
    /// 테이블 추적용 키 생성
    /// </summary>
    public string GenerateTableTrackingKey(string tableName)
    {
        if (string.IsNullOrEmpty(tableName))
        {
            throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));
        }

        return GenerateSecureKey("table", "tracking", new Dictionary<string, object?> { ["table"] = tableName });
    }

    /// <summary>
    /// 파라미터 해시 생성
    /// </summary>
    public string GenerateParameterHash(IDictionary<string, object?>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return "no_params";
        }

        var paramString = BuildParameterString(parameters);
        return ComputeSecureHash(paramString);
    }

    /// <summary>
    /// 보안이 강화된 캐시 키를 생성합니다 (내부 구현)
    /// </summary>
    /// <param name="controller">컨트롤러명</param>
    /// <param name="action">액션명</param>
    /// <param name="parameters">파라미터</param>
    /// <returns>안전한 캐시 키</returns>
    private string GenerateSecureKey(string controller, string action, IDictionary<string, object?>? parameters = null)
    {
        if (string.IsNullOrEmpty(controller))
        {
            throw new ArgumentException("Controller cannot be null or empty", nameof(controller));
        }
        if (string.IsNullOrEmpty(action))
        {
            throw new ArgumentException("Action cannot be null or empty", nameof(action));
        }

        // 1. 민감한 데이터 검사
        if (!ValidateParameters(controller, action, parameters))
        {
            throw new SecurityException("Sensitive data detected in cache key parameters");
        }

        // 2. 키 구성 요소 수집
        var keyComponents = CollectKeyComponents(controller, action, parameters);

        // 3. 키 생성 전략에 따라 처리
        return _options.KeyGenerationStrategy switch
        {
            KeyGenerationStrategy.PlainText => GeneratePlainTextKey(keyComponents),
            KeyGenerationStrategy.Hashed => GenerateHashedKey(keyComponents),
            KeyGenerationStrategy.Hybrid => GenerateHybridKey(keyComponents),
            _ => throw new NotSupportedException($"Unsupported key generation strategy: {_options.KeyGenerationStrategy}")
        };
    }

    /// <summary>
    /// 파라미터들이 안전한지 검증합니다
    /// </summary>
    private bool ValidateParameters(string controller, string action, IDictionary<string, object?>? parameters)
    {
        // 컨트롤러 검증
        if (!SensitiveDataDetector.IsSafeCacheKey(controller))
        {
            _logger.LogWarning("Unsafe controller detected: {Controller}", SensitiveDataDetector.MaskSensitiveData(controller));
            return false;
        }

        // 액션 검증
        if (!SensitiveDataDetector.IsSafeCacheKey(action))
        {
            _logger.LogWarning("Unsafe action detected: {Action}", SensitiveDataDetector.MaskSensitiveData(action));
            return false;
        }

        // 파라미터 검증
        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                if (param.Value == null) continue;

                var detection = SensitiveDataDetector.DetectSensitiveData(param.Value);
                if (!detection.IsSafe)
                {
                    _logger.LogWarning("Sensitive data detected in cache key parameter '{Key}': {SensitiveTypes}", 
                        param.Key, string.Join(", ", detection.DetectedTypes));
                    
                    if (_options.StrictMode)
                    {
                        return false;
                    }
                    
                    // Non-strict 모드에서는 경고만 로그
                    _logger.LogWarning("Continuing with potentially unsafe parameter (strict mode disabled)");
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 키 구성 요소들을 수집합니다
    /// </summary>
    private List<string> CollectKeyComponents(string controller, string action, IDictionary<string, object?>? parameters)
    {
        var components = new List<string> { controller, action };

        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                var paramString = param.Value switch
                {
                    null => "null",
                    string s => s,
                    int i => LazyCache.IntToString(i),
                    long l => LazyCache.LongToString(l),
                    double d => MemoryUtils.DoubleToFixedString(d, 2),
                    DateTime dt => dt.ToString("yyyyMMddHHmmss"),
                    Guid g => g.ToString("N"),
                    _ => param.Value.ToString() ?? "null"
                };

                components.Add($"{param.Key}={paramString}");
            }
        }

        return components;
    }

    /// <summary>
    /// 파라미터 딕셔너리를 문자열로 변환합니다
    /// </summary>
    private string BuildParameterString(IDictionary<string, object?> parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return "no_params";
        }

        var sb = HighPerformanceStringPool.RentStringBuilder(256);
        try
        {
            var sortedParams = parameters.OrderBy(kvp => kvp.Key);
            var first = true;
            
            foreach (var kvp in sortedParams)
            {
                if (!first)
                {
                    sb.Append('&');
                }
                first = false;

                sb.Append(kvp.Key);
                sb.Append('=');
                
                var valueString = kvp.Value switch
                {
                    null => "null",
                    string s => s,
                    int i => LazyCache.IntToString(i),
                    long l => LazyCache.LongToString(l),
                    double d => MemoryUtils.DoubleToFixedString(d, 2),
                    DateTime dt => dt.ToString("yyyyMMddHHmmss"),
                    Guid g => g.ToString("N"),
                    _ => kvp.Value.ToString() ?? "null"
                };
                
                sb.Append(valueString);
            }

            return sb.ToString();
        }
        finally
        {
            HighPerformanceStringPool.ReturnStringBuilder(sb, 256);
        }
    }

    /// <summary>
    /// 일반 텍스트 키를 생성합니다 (개발/테스트용)
    /// </summary>
    private string GeneratePlainTextKey(List<string> components)
    {
        var delimiter = _options.KeyDelimiter;
        var key = string.Join(delimiter, components);

        // 길이 제한 확인
        if (key.Length > _options.MaxKeyLength)
        {
            _logger.LogWarning("Generated key exceeds max length: {ActualLength}/{MaxLength}", 
                key.Length, _options.MaxKeyLength);
            
            // 긴 키는 해시로 변환
            return GenerateHashedKey(components);
        }

        return key;
    }

    /// <summary>
    /// 해시된 키를 생성합니다 (프로덕션 권장)
    /// </summary>
    private string GenerateHashedKey(List<string> components)
    {
        var plainKey = string.Join(_options.KeyDelimiter, components);
        var hash = ComputeSecureHash(plainKey);
        
        // 접두사 유지 + 해시
        var prefix = components[0];
        return $"{prefix}{_options.KeyDelimiter}hash_{hash}";
    }

    /// <summary>
    /// 하이브리드 키를 생성합니다 (접두사는 평문, 나머지는 해시)
    /// </summary>
    private string GenerateHybridKey(List<string> components)
    {
        if (components.Count <= 1)
        {
            return GeneratePlainTextKey(components);
        }

        var prefix = components[0];
        var remainingComponents = components.Skip(1).ToList();
        var remainingKey = string.Join(_options.KeyDelimiter, remainingComponents);
        
        // 나머지 부분이 짧으면 평문 유지
        if (remainingKey.Length <= _options.HashThreshold)
        {
            return GeneratePlainTextKey(components);
        }

        // 나머지 부분 해시
        var hash = ComputeSecureHash(remainingKey);
        return $"{prefix}{_options.KeyDelimiter}{hash}";
    }

    /// <summary>
    /// 보안 해시를 계산합니다
    /// </summary>
    private string ComputeSecureHash(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return "empty";
        }

        // Salt 추가 (옵션)
        var saltedInput = _options.UseSalt 
            ? $"{_options.Salt}{input}{_options.Salt}" 
            : input;

        var inputBytes = Encoding.UTF8.GetBytes(saltedInput);
        
        return _options.HashAlgorithm switch
        {
            HashAlgorithmType.SHA256 => ComputeSHA256Hash(inputBytes),
            HashAlgorithmType.SHA1 => ComputeSHA1Hash(inputBytes),
            HashAlgorithmType.MD5 => ComputeMD5Hash(inputBytes),
            _ => throw new NotSupportedException($"Unsupported hash algorithm: {_options.HashAlgorithm}")
        };
    }

    /// <summary>
    /// SHA256 해시를 계산합니다 (권장)
    /// </summary>
    private static string ComputeSHA256Hash(byte[] input)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(input);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// SHA1 해시를 계산합니다
    /// </summary>
    private static string ComputeSHA1Hash(byte[] input)
    {
        using var sha1 = SHA1.Create();
        var hashBytes = sha1.ComputeHash(input);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// MD5 해시를 계산합니다 (호환성용)
    /// </summary>
    private static string ComputeMD5Hash(byte[] input)
    {
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(input);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 캐시 키를 검증합니다
    /// </summary>
    /// <param name="cacheKey">검증할 캐시 키</param>
    /// <returns>키가 유효한지 여부</returns>
    public bool ValidateKey(string cacheKey)
    {
        if (string.IsNullOrEmpty(cacheKey))
        {
            return false;
        }

        // 길이 검사
        if (cacheKey.Length > _options.MaxKeyLength)
        {
            _logger.LogWarning("Cache key exceeds maximum length: {Length}", cacheKey.Length);
            return false;
        }

        // 민감한 데이터 검사
        if (!SensitiveDataDetector.IsSafeCacheKey(cacheKey))
        {
            return false;
        }

        // 허용된 문자 검사
        if (_options.AllowedCharacters != null)
        {
            foreach (var ch in cacheKey)
            {
                if (!_options.AllowedCharacters.Contains(ch))
                {
                    _logger.LogWarning("Cache key contains disallowed character: {Character}", ch);
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 키 생성 통계를 반환합니다
    /// </summary>
    public CacheKeyGenerationStats GetStats()
    {
        return new CacheKeyGenerationStats
        {
            Strategy = _options.KeyGenerationStrategy,
            MaxKeyLength = _options.MaxKeyLength,
            HashAlgorithm = _options.HashAlgorithm,
            StrictMode = _options.StrictMode,
            UseSalt = _options.UseSalt
        };
    }
}

/// <summary>
/// 보안 캐시 키 생성 옵션
/// </summary>
public class SecureCacheKeyOptions
{
    /// <summary>
    /// 키 생성 전략
    /// </summary>
    public KeyGenerationStrategy KeyGenerationStrategy { get; set; } = KeyGenerationStrategy.Hybrid;

    /// <summary>
    /// 해시 알고리즘
    /// </summary>
    public HashAlgorithmType HashAlgorithm { get; set; } = HashAlgorithmType.SHA256;

    /// <summary>
    /// 최대 키 길이
    /// </summary>
    public int MaxKeyLength { get; set; } = 250;

    /// <summary>
    /// 해시 변환 임계값 (하이브리드 모드용)
    /// </summary>
    public int HashThreshold { get; set; } = 50;

    /// <summary>
    /// 키 구분자
    /// </summary>
    public string KeyDelimiter { get; set; } = ":";

    /// <summary>
    /// 엄격 모드 (민감한 데이터 감지 시 예외 발생)
    /// </summary>
    public bool StrictMode { get; set; } = true;

    /// <summary>
    /// Salt 사용 여부
    /// </summary>
    public bool UseSalt { get; set; } = true;

    /// <summary>
    /// Salt 값
    /// </summary>
    public string Salt { get; set; } = "athena_cache_2024";

    /// <summary>
    /// 허용된 문자 집합 (null이면 모든 문자 허용)
    /// </summary>
    public HashSet<char>? AllowedCharacters { get; set; } = new()
    {
        // 영문자
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
        'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M',
        'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        // 숫자
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        // 특수문자
        ':', '-', '_', '.', '@', '#'
    };
}

/// <summary>
/// 키 생성 전략
/// </summary>
public enum KeyGenerationStrategy
{
    /// <summary>
    /// 일반 텍스트 (개발/테스트용)
    /// </summary>
    PlainText,

    /// <summary>
    /// 전체 해시 (최고 보안)
    /// </summary>
    Hashed,

    /// <summary>
    /// 하이브리드 (접두사 평문 + 파라미터 해시)
    /// </summary>
    Hybrid
}

/// <summary>
/// 해시 알고리즘 타입
/// </summary>
public enum HashAlgorithmType
{
    MD5,
    SHA1,
    SHA256
}

/// <summary>
/// 키 생성 통계 정보
/// </summary>
public class CacheKeyGenerationStats
{
    public KeyGenerationStrategy Strategy { get; init; }
    public int MaxKeyLength { get; init; }
    public HashAlgorithmType HashAlgorithm { get; init; }
    public bool StrictMode { get; init; }
    public bool UseSalt { get; init; }
}

/// <summary>
/// 보안 예외
/// </summary>
public class SecurityException : Exception
{
    public SecurityException(string message) : base(message) { }
    public SecurityException(string message, Exception innerException) : base(message, innerException) { }
}