namespace Athena.Cache.Core.Attributes;

/// <summary>
/// 캐시 활성화 및 설정 Attribute
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
public class AthenaCacheAttribute : Attribute
{
    /// <summary>캐시 만료 시간 (분)</summary>
    public int ExpirationMinutes { get; set; } = -1; // -1은 기본값 사용

    /// <summary>캐시 활성화 여부</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>연쇄 무효화 최대 깊이</summary>
    public int MaxRelatedDepth { get; set; } = -1; // -1은 글로벌 설정 사용

    /// <summary>캐시 키에 포함할 추가 파라미터</summary>
    public string[]? AdditionalKeyParameters { get; set; }

    /// <summary>캐시 키에서 제외할 파라미터</summary>
    public string[]? ExcludeParameters { get; set; }

    /// <summary>커스텀 캐시 키 접두사</summary>
    public string? CustomKeyPrefix { get; set; }
}