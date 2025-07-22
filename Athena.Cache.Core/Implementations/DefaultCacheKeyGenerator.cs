using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Athena.Cache.Core.Implementations;

/// <summary>
/// 기본 캐시 키 생성기 구현
/// </summary>
public class DefaultCacheKeyGenerator(AthenaCacheOptions options) : ICacheKeyGenerator
{
    /// <summary>
    /// API 요청 기반 캐시 키 생성
    /// {Namespace}_{Version}_{Controller}_{Action}_{ParameterHash}
    /// 예: MyApp_PROD_v1.2_UsersController_GetUsers_ABC123
    /// </summary>
    public string GenerateKey(string controller, string action, IDictionary<string, object?>? parameters = null)
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

        return string.Join(options.KeySeparator, keyParts);
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
    /// 4. SHA256 해싱
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

        // SHA256 해싱
        return ComputeSha256Hash(json);
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
    /// SHA256 해시 계산
    /// </summary>
    private static string ComputeSha256Hash(string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(inputBytes);

        // Base64URL 인코딩 (URL 안전한 문자만 사용)
        return Convert.ToBase64String(hashBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('='); // 패딩 제거
    }
}