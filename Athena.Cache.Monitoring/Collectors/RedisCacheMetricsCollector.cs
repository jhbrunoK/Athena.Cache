using Athena.Cache.Monitoring.Interfaces;
using Athena.Cache.Monitoring.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using StackExchange.Redis;

namespace Athena.Cache.Monitoring.Collectors
{
    /// <summary>
    /// Redis 전용 메트릭 수집기 (Redis INFO 명령 활용)
    /// </summary>
    public class RedisCacheMetricsCollector(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheMetricsCollector> logger,
        IOptions<CacheMonitoringOptions> options)
        : ICacheMetricsCollector
    {
        private readonly CacheMonitoringOptions _options = options.Value;
        private readonly ConcurrentQueue<CacheMetrics> _metricsHistory = new();

        public async Task<CacheMetrics> CollectMetricsAsync()
        {
            try
            {
                var server = redis.GetServer(redis.GetEndPoints().First());
                var info = await server.InfoAsync();

                var metrics = new CacheMetrics
                {
                    Timestamp = DateTime.UtcNow
                };

                // Redis INFO에서 메트릭 파싱
                foreach (var section in info)
                {
                    foreach (var item in section)
                    {
                        switch (item.Key.ToLower())
                        {
                            case "keyspace_hits":
                                if (long.TryParse(item.Value, out var hits))
                                    metrics.HitCount = hits;
                                break;

                            case "keyspace_misses":
                                if (long.TryParse(item.Value, out var misses))
                                    metrics.MissCount = misses;
                                break;

                            case "used_memory":
                                if (long.TryParse(item.Value, out var memory))
                                    metrics.MemoryUsageMB = memory / (1024 * 1024);
                                break;

                            case "connected_clients":
                                if (int.TryParse(item.Value, out var connections))
                                    metrics.ConnectionCount = connections;
                                break;
                        }
                    }
                }

                // 계산된 메트릭
                metrics.TotalRequests = metrics.HitCount + metrics.MissCount;
                metrics.HitRatio = metrics.TotalRequests > 0
                    ? (double)metrics.HitCount / metrics.TotalRequests
                    : 0;

                // 응답 시간 측정 (PING 명령 사용)
                var database = redis.GetDatabase();
                var pingStart = DateTime.UtcNow;
                await database.PingAsync();
                metrics.AverageResponseTimeMs = (DateTime.UtcNow - pingStart).TotalMilliseconds;

                return metrics;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to collect Redis metrics");
                throw;
            }
        }

        public Task<List<CacheMetrics>> GetMetricsHistoryAsync(DateTime startTime, DateTime endTime)
        {
            var history = _metricsHistory
                .Where(m => m.Timestamp >= startTime && m.Timestamp <= endTime)
                .OrderBy(m => m.Timestamp)
                .ToList();

            return Task.FromResult(history);
        }

        public Task RecordMetricsAsync(CacheMetrics metrics)
        {
            _metricsHistory.Enqueue(metrics);

            // 오래된 메트릭 정리
            var cutoffTime = DateTime.UtcNow - _options.MetricsRetentionPeriod;
            while (_metricsHistory.TryPeek(out var oldMetrics) && oldMetrics.Timestamp < cutoffTime)
            {
                _metricsHistory.TryDequeue(out _);
            }

            return Task.CompletedTask;
        }
    }
}
