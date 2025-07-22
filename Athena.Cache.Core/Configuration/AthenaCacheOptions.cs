using Athena.Cache.Core.Enums;

namespace Athena.Cache.Core.Configuration;

/// <summary>
/// Athena Cache 전역 설정
/// </summary>
public class AthenaCacheOptions
{
    /// <summary>네임스페이스 (환경/앱 구분)</summary>
    public string Namespace { get; set; } = "AthenaCache";

    /// <summary>버전 키</summary>
    public string? VersionKey { get; set; }

    /// <summary>기본 캐시 만료 시간 (분)</summary>
    public int DefaultExpirationMinutes { get; set; } = 30;

    /// <summary>연쇄 무효화 최대 깊이</summary>
    public int MaxRelatedDepth { get; set; } = 3;

    /// <summary>키 구분자</summary>
    public string KeySeparator { get; set; } = "_";

    /// <summary>테이블 추적 키 접두사</summary>
    public string TableTrackingPrefix { get; set; } = "table";

    /// <summary>서비스 시작 시 캐시 정리 방식</summary>
    public CleanupMode StartupCacheCleanup { get; set; } = CleanupMode.ExpireShorten;

    /// <summary>로깅 레벨 설정</summary>
    public CacheLoggingOptions Logging { get; set; } = new();

    /// <summary>Convention 기반 테이블 매핑 설정</summary>
    public ConventionOptions Convention { get; set; } = new();

    /// <summary>테이블별 캐시 설정</summary>
    public Dictionary<string, TableCachePolicy> TablePolicies { get; set; } = new();

    /// <summary>에러 처리 옵션</summary>
    public ErrorHandlingOptions ErrorHandling { get; set; } = new();
}