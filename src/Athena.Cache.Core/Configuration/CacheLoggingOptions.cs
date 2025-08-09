namespace Athena.Cache.Core.Configuration;

/// <summary>
/// 로깅 설정
/// </summary>
public class CacheLoggingOptions
{
    /// <summary>캐시 히트/미스 로깅</summary>
    public bool LogCacheHitMiss { get; set; } = true;

    /// <summary>캐시 무효화 로깅</summary>
    public bool LogInvalidation { get; set; } = true;

    /// <summary>키 생성 로깅</summary>
    public bool LogKeyGeneration { get; set; } = false;

    /// <summary>에러 로깅</summary>
    public bool LogErrors { get; set; } = true;
}