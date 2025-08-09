namespace Athena.Cache.Analytics.Models;

/// <summary>
/// 사용 패턴 분석 결과
/// </summary>
public class UsagePatternAnalysis
{
    public Dictionary<int, long> HourlyDistribution { get; set; } = new(); // 시간대별 사용량
    public Dictionary<string, long> EndpointPopularity { get; set; } = new(); // 엔드포인트별 인기도
    public Dictionary<string, double> AverageResponseTimes { get; set; } = new(); // 평균 응답 시간
    public List<string> FrequentlyInvalidatedTables { get; set; } = new(); // 자주 무효화되는 테이블
    public List<HotKeyAnalysis> HotKeys { get; set; } = new(); // 핫키 목록
    public List<string> ColdKeys { get; set; } = new(); // 콜드키 목록
}