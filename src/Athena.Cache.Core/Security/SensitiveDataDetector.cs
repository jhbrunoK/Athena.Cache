using System.Text.RegularExpressions;
using System.Text.Json;

namespace Athena.Cache.Core.Security;

/// <summary>
/// 민감한 데이터를 감지하고 캐시에서 제외하는 보안 유틸리티
/// </summary>
public static class SensitiveDataDetector
{
    private static ILogger? _logger;

    // 민감한 데이터 패턴들 (정규표현식)
    private static readonly Dictionary<string, Regex> SensitivePatterns = new()
    {
        // 신용카드 번호 (16자리, 하이픈/공백 포함)
        ["credit_card"] = new Regex(@"\b(?:\d{4}[-\s]?){3}\d{4}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        
        // 주민등록번호 (한국)
        ["ssn_kr"] = new Regex(@"\b\d{6}-[1-4]\d{6}\b", RegexOptions.Compiled),
        
        // 이메일 주소 (개인정보 포함 가능)
        ["email"] = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled),
        
        // 전화번호 (한국)
        ["phone_kr"] = new Regex(@"\b01[0-9]-\d{3,4}-\d{4}\b", RegexOptions.Compiled),
        
        // IP 주소
        ["ip_address"] = new Regex(@"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b", RegexOptions.Compiled),
        
        // JWT 토큰
        ["jwt_token"] = new Regex(@"\beyJ[A-Za-z0-9+/=]+\.[A-Za-z0-9+/=]+\.[A-Za-z0-9+/=]*\b", RegexOptions.Compiled),
        
        // API 키 패턴
        ["api_key"] = new Regex(@"\b[A-Za-z0-9]{32,}\b", RegexOptions.Compiled),
        
        // 비밀번호 해시 (bcrypt, scrypt 등)
        ["password_hash"] = new Regex(@"\$[a-z0-9]+\$[0-9]+\$[A-Za-z0-9+/=.]+", RegexOptions.Compiled),
    };

    // 민감한 필드명들
    private static readonly HashSet<string> SensitiveFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pwd", "passwd", "pass",
        "secret", "token", "key", "apikey", "api_key",
        "ssn", "social_security_number", "jumin",
        "credit_card", "creditcard", "card_number",
        "phone", "telephone", "mobile",
        "email", "mail", "email_address",
        "address", "home_address", "work_address",
        "birth_date", "birthday", "birthdate",
        "bank_account", "account_number",
        "session_id", "sessionid",
        "authorization", "auth", "bearer"
    };

    /// <summary>
    /// 문자열에서 민감한 데이터를 감지합니다
    /// </summary>
    /// <param name="text">검사할 텍스트</param>
    /// <returns>감지된 민감한 데이터 정보</returns>
    public static SensitiveDataDetectionResult DetectSensitiveData(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new SensitiveDataDetectionResult { IsSafe = true };
        }

        var detectedTypes = new List<string>();

        foreach (var (patternName, regex) in SensitivePatterns)
        {
            if (regex.IsMatch(text))
            {
                detectedTypes.Add(patternName);
                _logger?.LogWarning("Sensitive data detected: {PatternType} in text", patternName);
            }
        }

        return new SensitiveDataDetectionResult
        {
            IsSafe = detectedTypes.Count == 0,
            DetectedTypes = detectedTypes.ToArray(),
            OriginalTextLength = text.Length
        };
    }

    /// <summary>
    /// 객체에서 민감한 데이터를 감지합니다 (JSON 직렬화 기반)
    /// </summary>
    /// <param name="obj">검사할 객체</param>
    /// <returns>감지된 민감한 데이터 정보</returns>
    public static SensitiveDataDetectionResult DetectSensitiveData(object? obj)
    {
        if (obj == null)
        {
            return new SensitiveDataDetectionResult { IsSafe = true };
        }

        try
        {
            // 객체를 JSON으로 직렬화하여 검사
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var textResult = DetectSensitiveData(json);
            if (!textResult.IsSafe)
            {
                return textResult;
            }

            // 필드명 기반 검사
            var fieldResult = DetectSensitiveFieldNames(json);
            if (!fieldResult.IsSafe)
            {
                return fieldResult;
            }

            return new SensitiveDataDetectionResult { IsSafe = true };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error while detecting sensitive data in object");
            // 오류가 발생하면 안전하지 않다고 가정
            return new SensitiveDataDetectionResult 
            { 
                IsSafe = false, 
                DetectedTypes = new[] { "serialization_error" } 
            };
        }
    }

    /// <summary>
    /// JSON에서 민감한 필드명을 감지합니다
    /// </summary>
    /// <param name="json">JSON 문자열</param>
    /// <returns>감지된 민감한 데이터 정보</returns>
    private static SensitiveDataDetectionResult DetectSensitiveFieldNames(string json)
    {
        var detectedTypes = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            CheckJsonElement(doc.RootElement, detectedTypes, "");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing JSON for sensitive field detection");
            return new SensitiveDataDetectionResult 
            { 
                IsSafe = false, 
                DetectedTypes = new[] { "json_parse_error" } 
            };
        }

        return new SensitiveDataDetectionResult
        {
            IsSafe = detectedTypes.Count == 0,
            DetectedTypes = detectedTypes.ToArray()
        };
    }

    /// <summary>
    /// JSON 요소를 재귀적으로 검사하여 민감한 필드명을 찾습니다
    /// </summary>
    private static void CheckJsonElement(JsonElement element, List<string> detectedTypes, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var currentPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
                    
                    if (SensitiveFieldNames.Contains(property.Name))
                    {
                        detectedTypes.Add($"sensitive_field:{property.Name}");
                        _logger?.LogWarning("Sensitive field detected: {FieldName} at path {Path}", 
                            property.Name, currentPath);
                    }
                    
                    CheckJsonElement(property.Value, detectedTypes, currentPath);
                }
                break;
                
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    CheckJsonElement(item, detectedTypes, $"{path}[{index}]");
                    index++;
                }
                break;
        }
    }

    /// <summary>
    /// 캐시 키가 안전한지 검증합니다
    /// </summary>
    /// <param name="cacheKey">검사할 캐시 키</param>
    /// <returns>캐시 키가 안전한지 여부</returns>
    public static bool IsSafeCacheKey(string cacheKey)
    {
        if (string.IsNullOrEmpty(cacheKey))
        {
            return false;
        }

        // 캐시 키에서 민감한 데이터 패턴 검사
        var result = DetectSensitiveData(cacheKey);
        if (!result.IsSafe)
        {
            _logger?.LogWarning("Unsafe cache key detected: contains {SensitiveTypes}", 
                string.Join(", ", result.DetectedTypes));
            return false;
        }

        // 캐시 키 길이 제한 (너무 긴 키는 의심스러움)
        if (cacheKey.Length > 500)
        {
            _logger?.LogWarning("Cache key too long: {Length} characters", cacheKey.Length);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 민감한 데이터를 마스킹합니다 (로깅용)
    /// </summary>
    /// <param name="text">마스킹할 텍스트</param>
    /// <returns>마스킹된 텍스트</returns>
    public static string MaskSensitiveData(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var masked = text;

        foreach (var (patternName, regex) in SensitivePatterns)
        {
            masked = regex.Replace(masked, match =>
            {
                // 처음 2글자와 마지막 2글자만 보여주고 나머지는 *로 마스킹
                var value = match.Value;
                if (value.Length <= 4)
                {
                    return new string('*', value.Length);
                }
                
                return value.Substring(0, 2) + new string('*', value.Length - 4) + value.Substring(value.Length - 2);
            });
        }

        return masked;
    }

    /// <summary>
    /// 새로운 민감한 패턴을 추가합니다
    /// </summary>
    /// <param name="name">패턴 이름</param>
    /// <param name="pattern">정규표현식 패턴</param>
    public static void AddSensitivePattern(string name, string pattern)
    {
        try
        {
            SensitivePatterns[name] = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _logger?.LogInformation("Added new sensitive pattern: {PatternName}", name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to add sensitive pattern: {PatternName}", name);
        }
    }

    /// <summary>
    /// 새로운 민감한 필드명을 추가합니다
    /// </summary>
    /// <param name="fieldName">필드명</param>
    public static void AddSensitiveFieldName(string fieldName)
    {
        SensitiveFieldNames.Add(fieldName);
        _logger?.LogInformation("Added new sensitive field name: {FieldName}", fieldName);
    }
}

/// <summary>
/// 민감한 데이터 감지 결과
/// </summary>
public class SensitiveDataDetectionResult
{
    /// <summary>
    /// 데이터가 안전한지 여부
    /// </summary>
    public bool IsSafe { get; init; }

    /// <summary>
    /// 감지된 민감한 데이터 타입들
    /// </summary>
    public string[] DetectedTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 원본 텍스트 길이
    /// </summary>
    public int OriginalTextLength { get; init; }

    /// <summary>
    /// 감지된 민감한 데이터 타입들의 요약
    /// </summary>
    public string Summary => DetectedTypes.Length == 0 
        ? "Safe" 
        : $"Detected: {string.Join(", ", DetectedTypes)}";
}
