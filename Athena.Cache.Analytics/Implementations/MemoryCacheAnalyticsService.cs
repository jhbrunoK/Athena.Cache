using Athena.Cache.Analytics.Abstractions;
using Athena.Cache.Analytics.Models;

namespace Athena.Cache.Analytics.Implementations;

/// <summary>
/// 메모리 기반 캐시 분석 서비스
/// </summary>
public class MemoryCacheAnalyticsService(MemoryCacheEventCollector eventCollector) : ICacheAnalyticsService
{
    private readonly MemoryCacheEventCollector _eventCollector = eventCollector;

    public async Task<CacheStatistics> GetStatisticsAsync(DateTime startDate, DateTime endDate)
    {
        var events = MemoryCacheEventCollector.GetStoredEvents()
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .ToList();

        var hits = events.Count(e => e.EventType == CacheEventType.Hit);
        var misses = events.Count(e => e.EventType == CacheEventType.Miss);
        var total = hits + misses;

        return await Task.FromResult(new CacheStatistics
        {
            PeriodStart = startDate,
            PeriodEnd = endDate,
            TotalRequests = total,
            TotalHits = hits,
            TotalMisses = misses,
            HitRatio = total > 0 ? (double)hits / total : 0,
            AverageResponseTimeMs = events.Average(e => e.ProcessingTimeMs),
            ActiveKeys = events.Select(e => e.CacheKey).Distinct().Count(),
            EndpointHits = events.GroupBy(e => e.EndpointPath)
                .ToDictionary(g => g.Key, g => (long)g.Count()),
            TableInvalidations = events.Where(e => e.EventType == CacheEventType.Invalidate)
                .GroupBy(e => e.TableName ?? "Unknown")
                .ToDictionary(g => g.Key, g => (long)g.Count())
        });
    }

    public async Task<List<CacheTimeSeriesData>> GetTimeSeriesDataAsync(
        DateTime startDate,
        DateTime endDate,
        TimeSpan interval)
    {
        var events = MemoryCacheEventCollector.GetStoredEvents()
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .ToList();

        var result = new List<CacheTimeSeriesData>();
        var current = startDate;

        while (current < endDate)
        {
            var periodEnd = current.Add(interval);
            var periodEvents = events.Where(e => e.Timestamp >= current && e.Timestamp < periodEnd).ToList();

            var hits = periodEvents.Count(e => e.EventType == CacheEventType.Hit);
            var misses = periodEvents.Count(e => e.EventType == CacheEventType.Miss);
            var total = hits + misses;

            result.Add(new CacheTimeSeriesData
            {
                Timestamp = current,
                HitRatio = total > 0 ? (double)hits / total : 0,
                RequestCount = total,
                AverageResponseTime = periodEvents.Count > 0 ? periodEvents.Average(e => e.ProcessingTimeMs) : 0,
                CacheSize = periodEvents.Select(e => e.CacheKey).Distinct().Count()
            });

            current = periodEnd;
        }

        return await Task.FromResult(result);
    }

    public async Task<UsagePatternAnalysis> AnalyzeUsagePatternsAsync(DateTime startDate, DateTime endDate)
    {
        var events = MemoryCacheEventCollector.GetStoredEvents()
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .ToList();

        var analysis = new UsagePatternAnalysis
        {
            HourlyDistribution = events.GroupBy(e => e.Timestamp.Hour)
                .ToDictionary(g => g.Key, g => (long)g.Count()),

            EndpointPopularity = events.GroupBy(e => e.EndpointPath)
                .ToDictionary(g => g.Key, g => (long)g.Count()),

            AverageResponseTimes = events.GroupBy(e => e.EndpointPath)
                .ToDictionary(g => g.Key, g => g.Average(e => e.ProcessingTimeMs)),

            FrequentlyInvalidatedTables = events.Where(e => e.EventType == CacheEventType.Invalidate)
                .GroupBy(e => e.TableName ?? "Unknown")
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => g.Key)
                .ToList(),

            HotKeys = await GetHotKeysAsync(startDate, endDate, 20),

            ColdKeys = await GetColdKeysAsync(startDate, endDate, TimeSpan.FromHours(24))
        };

        return analysis;
    }

    public async Task<List<HotKeyAnalysis>> GetHotKeysAsync(DateTime startDate, DateTime endDate, int topCount = 50)
    {
        var events = MemoryCacheEventCollector.GetStoredEvents()
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .ToList();

        var keyGroups = events.GroupBy(e => e.CacheKey)
            .Select(g => new HotKeyAnalysis
            {
                CacheKey = g.Key,
                EndpointPath = g.FirstOrDefault()?.EndpointPath ?? "",
                HitCount = g.Count(e => e.EventType == CacheEventType.Hit),
                MissCount = g.Count(e => e.EventType == CacheEventType.Miss),
                FirstAccess = g.Min(e => e.Timestamp),
                LastAccess = g.Max(e => e.Timestamp),
                AverageResponseTime = g.Average(e => e.ProcessingTimeMs),
                AverageResponseSize = g.Where(e => e.ResponseSize.HasValue)
                    .Select(e => e.ResponseSize!.Value)
                    .DefaultIfEmpty(0)
                    .Average() == 0 ? null : (int?)g.Where(e => e.ResponseSize.HasValue)
                    .Select(e => e.ResponseSize!.Value)
                    .Average()
            })
            .ToList();

        // HitRatio 계산
        foreach (var key in keyGroups)
        {
            var total = key.HitCount + key.MissCount;
            key.HitRatio = total > 0 ? (double)key.HitCount / total : 0;
        }

        return await Task.FromResult(keyGroups.OrderByDescending(k => k.HitCount)
            .Take(topCount)
            .ToList());
    }

    public async Task<List<string>> GetColdKeysAsync(DateTime startDate, DateTime endDate, TimeSpan inactiveThreshold)
    {
        var cutoffTime = DateTime.UtcNow.Subtract(inactiveThreshold);

        var events = MemoryCacheEventCollector.GetStoredEvents()
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .ToList();

        var coldKeys = events.GroupBy(e => e.CacheKey)
            .Where(g => g.Max(e => e.Timestamp) < cutoffTime)
            .Select(g => g.Key)
            .ToList();

        return await Task.FromResult(coldKeys);
    }

    public async Task<Dictionary<string, double>> AnalyzeCacheEfficiencyAsync(DateTime startDate, DateTime endDate)
    {
        var events = MemoryCacheEventCollector.GetStoredEvents()
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .ToList();

        var efficiency = new Dictionary<string, double>();

        // 전체 효율성
        var totalHits = events.Count(e => e.EventType == CacheEventType.Hit);
        var totalRequests = events.Count(e => e.EventType == CacheEventType.Hit || e.EventType == CacheEventType.Miss);
        efficiency["Overall"] = totalRequests > 0 ? (double)totalHits / totalRequests : 0;

        // 엔드포인트별 효율성
        var endpointGroups = events.Where(e => e.EventType == CacheEventType.Hit || e.EventType == CacheEventType.Miss)
            .GroupBy(e => e.EndpointPath);

        foreach (var group in endpointGroups)
        {
            var hits = group.Count(e => e.EventType == CacheEventType.Hit);
            var total = group.Count();
            efficiency[group.Key] = total > 0 ? (double)hits / total : 0;
        }

        return await Task.FromResult(efficiency);
    }
}