using Athena.Cache.Analytics.Models;

namespace Athena.Cache.Analytics.Abstractions;

/// <summary>
/// 캐시 분석 서비스
/// </summary>
public interface ICacheAnalyticsService
{
    /// <summary>
    /// 기간별 캐시 통계 조회
    /// </summary>
    Task<CacheStatistics> GetStatisticsAsync(DateTime startDate, DateTime endDate);

    /// <summary>
    /// 시계열 데이터 조회
    /// </summary>
    Task<List<CacheTimeSeriesData>> GetTimeSeriesDataAsync(
        DateTime startDate,
        DateTime endDate,
        TimeSpan interval);

    /// <summary>
    /// 사용 패턴 분석
    /// </summary>
    Task<UsagePatternAnalysis> AnalyzeUsagePatternsAsync(
        DateTime startDate,
        DateTime endDate);

    /// <summary>
    /// 핫키 분석
    /// </summary>
    Task<List<HotKeyAnalysis>> GetHotKeysAsync(
        DateTime startDate,
        DateTime endDate,
        int topCount = 50);

    /// <summary>
    /// 콜드키 식별
    /// </summary>
    Task<List<string>> GetColdKeysAsync(
        DateTime startDate,
        DateTime endDate,
        TimeSpan inactiveThreshold);

    /// <summary>
    /// 캐시 효율성 분석
    /// </summary>
    Task<Dictionary<string, double>> AnalyzeCacheEfficiencyAsync(
        DateTime startDate,
        DateTime endDate);
}