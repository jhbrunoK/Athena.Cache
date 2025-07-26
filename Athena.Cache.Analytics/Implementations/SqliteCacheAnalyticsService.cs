using Athena.Cache.Analytics.Abstractions;
using Athena.Cache.Analytics.Data;
using Athena.Cache.Analytics.Models;
using Microsoft.EntityFrameworkCore;

namespace Athena.Cache.Analytics.Implementations;

/// <summary>
/// SQLite 기반 캐시 분석 서비스
/// </summary>
public class SqliteCacheAnalyticsService(CacheAnalyticsDbContext dbContext) : ICacheAnalyticsService
{
    public async Task<CacheStatistics> GetStatisticsAsync(DateTime startDate, DateTime endDate)
    {
        var events = await dbContext.CacheEvents
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .ToListAsync();

        var hits = events.Count(e => e.EventType == (int)CacheEventType.Hit);
        var misses = events.Count(e => e.EventType == (int)CacheEventType.Miss);
        var total = hits + misses;

        return new CacheStatistics
        {
            PeriodStart = startDate,
            PeriodEnd = endDate,
            TotalRequests = total,
            TotalHits = hits,
            TotalMisses = misses,
            HitRatio = total > 0 ? (double)hits / total : 0,
            AverageResponseTimeMs = events.Count > 0 ? events.Average(e => e.ProcessingTimeMs) : 0,
            ActiveKeys = events.Select(e => e.CacheKey).Distinct().Count(),
            EndpointHits = events.GroupBy(e => e.EndpointPath)
                .ToDictionary(g => g.Key, g => (long)g.Count()),
            TableInvalidations = events.Where(e => e.EventType == (int)CacheEventType.Invalidate)
                .GroupBy(e => e.TableName ?? "Unknown")
                .ToDictionary(g => g.Key, g => (long)g.Count())
        };
    }

    public async Task<List<CacheTimeSeriesData>> GetTimeSeriesDataAsync(
        DateTime startDate,
        DateTime endDate,
        TimeSpan interval)
    {
        var events = await dbContext.CacheEvents
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();

        var result = new List<CacheTimeSeriesData>();
        var current = startDate;

        while (current < endDate)
        {
            var periodEnd = current.Add(interval);
            var periodEvents = events.Where(e => e.Timestamp >= current && e.Timestamp < periodEnd).ToList();

            var hits = periodEvents.Count(e => e.EventType == (int)CacheEventType.Hit);
            var misses = periodEvents.Count(e => e.EventType == (int)CacheEventType.Miss);
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

        return result;
    }

    public async Task<UsagePatternAnalysis> AnalyzeUsagePatternsAsync(DateTime startDate, DateTime endDate)
    {
        var events = await dbContext.CacheEvents
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .ToListAsync();

        var hotKeys = await GetHotKeysAsync(startDate, endDate, 20);
        var coldKeys = await GetColdKeysAsync(startDate, endDate, TimeSpan.FromHours(24));

        return new UsagePatternAnalysis
        {
            HourlyDistribution = events.GroupBy(e => e.Timestamp.Hour)
                .ToDictionary(g => g.Key, g => (long)g.Count()),

            EndpointPopularity = events.GroupBy(e => e.EndpointPath)
                .ToDictionary(g => g.Key, g => (long)g.Count()),

            AverageResponseTimes = events.GroupBy(e => e.EndpointPath)
                .ToDictionary(g => g.Key, g => g.Average(e => e.ProcessingTimeMs)),

            FrequentlyInvalidatedTables = events.Where(e => e.EventType == (int)CacheEventType.Invalidate)
                .GroupBy(e => e.TableName ?? "Unknown")
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => g.Key)
                .ToList(),

            HotKeys = hotKeys,
            ColdKeys = coldKeys
        };
    }

    public async Task<List<HotKeyAnalysis>> GetHotKeysAsync(DateTime startDate, DateTime endDate, int topCount = 50)
    {
        var keyStats = await dbContext.CacheEvents
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .GroupBy(e => e.CacheKey)
            .Select(g => new
            {
                CacheKey = g.Key,
                HitCount = g.Count(e => e.EventType == (int)CacheEventType.Hit),
                MissCount = g.Count(e => e.EventType == (int)CacheEventType.Miss),
                FirstAccess = g.Min(e => e.Timestamp),
                LastAccess = g.Max(e => e.Timestamp),
                AverageResponseTime = g.Average(e => e.ProcessingTimeMs),
                AverageResponseSize = g.Where(e => e.ResponseSize.HasValue).Average(e => e.ResponseSize),
                EndpointPath = g.First().EndpointPath
            })
            .OrderByDescending(x => x.HitCount)
            .Take(topCount)
            .ToListAsync();

        return keyStats.Select(x => new HotKeyAnalysis
        {
            CacheKey = x.CacheKey,
            EndpointPath = x.EndpointPath,
            HitCount = x.HitCount,
            MissCount = x.MissCount,
            HitRatio = (x.HitCount + x.MissCount) > 0 ? (double)x.HitCount / (x.HitCount + x.MissCount) : 0,
            FirstAccess = x.FirstAccess,
            LastAccess = x.LastAccess,
            AverageResponseTime = x.AverageResponseTime,
            AverageResponseSize = x.AverageResponseSize.HasValue ? (int?)x.AverageResponseSize : null
        }).ToList();
    }

    public async Task<List<string>> GetColdKeysAsync(DateTime startDate, DateTime endDate, TimeSpan inactiveThreshold)
    {
        var cutoffTime = DateTime.UtcNow.Subtract(inactiveThreshold);

        var coldKeys = await dbContext.CacheEvents
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .GroupBy(e => e.CacheKey)
            .Where(g => g.Max(e => e.Timestamp) < cutoffTime)
            .Select(g => g.Key)
            .ToListAsync();

        return coldKeys;
    }

    public async Task<Dictionary<string, double>> AnalyzeCacheEfficiencyAsync(DateTime startDate, DateTime endDate)
    {
        var events = await dbContext.CacheEvents
            .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
            .Where(e => e.EventType == (int)CacheEventType.Hit || e.EventType == (int)CacheEventType.Miss)
            .ToListAsync();

        var efficiency = new Dictionary<string, double>();

        // 전체 효율성
        var totalHits = events.Count(e => e.EventType == (int)CacheEventType.Hit);
        var totalRequests = events.Count;
        efficiency["Overall"] = totalRequests > 0 ? (double)totalHits / totalRequests : 0;

        // 엔드포인트별 효율성
        var endpointGroups = events.GroupBy(e => e.EndpointPath);
        foreach (var group in endpointGroups)
        {
            var hits = group.Count(e => e.EventType == (int)CacheEventType.Hit);
            var total = group.Count();
            efficiency[group.Key] = total > 0 ? (double)hits / total : 0;
        }

        return efficiency;
    }
}