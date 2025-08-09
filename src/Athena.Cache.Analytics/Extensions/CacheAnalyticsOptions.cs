namespace Athena.Cache.Analytics.Extensions;

/// <summary>
/// 캐시 분석 설정 옵션
/// </summary>
public class CacheAnalyticsOptions
{
    /// <summary>
    /// 이벤트 버퍼 최대 크기
    /// </summary>
    public int MaxBufferSize { get; set; } = 1000;

    /// <summary>
    /// 자동 플러시 간격
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 분석 데이터 보존 기간
    /// </summary>
    public TimeSpan DataRetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// 미들웨어 자동 등록 여부
    /// </summary>
    public bool AutoRegisterMiddleware { get; set; } = true;
}