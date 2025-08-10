using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Monitoring.Abstractions;
using Athena.Invalidation.Monitoring.Decorators;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Athena.Invalidation.Monitoring.Integration;

/// <summary>
/// 기존 무효화 엔진에 모니터링을 통합하는 확장 메서드
/// </summary>
public static class MonitoringIntegrationExtensions
{
    /// <summary>
    /// 기존 무효화 엔진을 모니터링 데코레이터로 감싸기
    /// </summary>
    public static IServiceCollection DecorateInvalidationEngineWithMonitoring(
        this IServiceCollection services)
    {
        // 기존 IInvalidationEngine 등록을 MonitoredInvalidationEngine로 데코레이트
        services.Decorate<IInvalidationEngine, MonitoredInvalidationEngine>();
        return services;
    }

    /// <summary>
    /// 분산 환경에서의 모니터링 통합
    /// </summary>
    public static IServiceCollection IntegrateDistributedMonitoring(
        this IServiceCollection services,
        Action<DistributedMonitoringOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 분산 이벤트 모니터링 컴포넌트 추가
        services.TryAddSingleton<IDistributedEventMonitor, DefaultDistributedEventMonitor>();
        
        return services;
    }
}

/// <summary>
/// 분산 이벤트 모니터링 인터페이스
/// </summary>
public interface IDistributedEventMonitor
{
    /// <summary>분산 이벤트 발행 기록</summary>
    void RecordEventPublished(string eventType, string targetNode, TimeSpan duration, bool success = true);
    
    /// <summary>분산 이벤트 수신 기록</summary>
    void RecordEventReceived(string eventType, string sourceNode, TimeSpan processingTime, bool success = true);
    
    /// <summary>노드 연결 상태 기록</summary>
    void RecordNodeConnection(string nodeId, bool connected);
    
    /// <summary>분산 메트릭 조회</summary>
    Task<DistributedMetricsSnapshot> GetDistributedMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 분산 메트릭 스냅샷
/// </summary>
public class DistributedMetricsSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public long TotalPublishedEvents { get; init; }
    public long TotalReceivedEvents { get; init; }
    public long FailedPublishedEvents { get; init; }
    public long FailedReceivedEvents { get; init; }
    public TimeSpan AveragePublishTime { get; init; }
    public TimeSpan AverageProcessingTime { get; init; }
    public Dictionary<string, NodeMetrics> NodeMetrics { get; init; } = new();
    public Dictionary<string, long> EventTypeDistribution { get; init; } = new();
}

/// <summary>
/// 노드별 메트릭
/// </summary>
public record NodeMetrics
{
    public string NodeId { get; init; } = string.Empty;
    public bool IsConnected { get; init; }
    public long PublishedEvents { get; init; }
    public long ReceivedEvents { get; init; }
    public TimeSpan LastSeen { get; init; }
    public TimeSpan AverageLatency { get; init; }
}

/// <summary>
/// 기본 분산 이벤트 모니터 구현
/// </summary>
internal class DefaultDistributedEventMonitor(
    IInvalidationMetricsCollector metricsCollector,
    ILogger<DefaultDistributedEventMonitor> logger,
    IOptions<DistributedMonitoringOptions> options)
    : IDistributedEventMonitor
{
    private readonly DistributedMonitoringOptions _options = options.Value ?? new DistributedMonitoringOptions();

    private readonly ConcurrentDictionary<string, NodeMetrics> _nodeMetrics = new();
    private readonly ConcurrentDictionary<string, long> _eventTypeCounters = new();

    private long _totalPublishedEvents = 0;
    private long _totalReceivedEvents = 0;
    private long _failedPublishedEvents = 0;
    private long _failedReceivedEvents = 0;

    public void RecordEventPublished(string eventType, string targetNode, TimeSpan duration, bool success = true)
    {
        Interlocked.Increment(ref _totalPublishedEvents);
        if (!success)
        {
            Interlocked.Increment(ref _failedPublishedEvents);
        }

        _eventTypeCounters.AddOrUpdate(eventType, 1, (key, value) => value + 1);
        
        // 메트릭 수집기에도 전달
        metricsCollector.RecordDistributedEvent(eventType, targetNode, duration, success);

        if (_options.LogDistributedEvents)
        {
            logger.LogDebug("Published distributed event {EventType} to node {TargetNode} in {Duration}ms (Success: {Success})",
                eventType, targetNode, duration.TotalMilliseconds, success);
        }
    }

    public void RecordEventReceived(string eventType, string sourceNode, TimeSpan processingTime, bool success = true)
    {
        Interlocked.Increment(ref _totalReceivedEvents);
        if (!success)
        {
            Interlocked.Increment(ref _failedReceivedEvents);
        }

        _eventTypeCounters.AddOrUpdate(eventType, 1, (key, value) => value + 1);
        
        // 노드 메트릭 업데이트
        _nodeMetrics.AddOrUpdate(sourceNode, 
            new NodeMetrics 
            { 
                NodeId = sourceNode, 
                IsConnected = true, 
                ReceivedEvents = 1,
                LastSeen = TimeSpan.Zero,
                AverageLatency = processingTime
            },
            (key, existing) => existing with 
            { 
                ReceivedEvents = existing.ReceivedEvents + 1,
                LastSeen = TimeSpan.Zero,
                AverageLatency = CalculateAverageLatency(existing.AverageLatency, processingTime)
            });

        if (_options.LogDistributedEvents)
        {
            logger.LogDebug("Received distributed event {EventType} from node {SourceNode} processed in {Duration}ms (Success: {Success})",
                eventType, sourceNode, processingTime.TotalMilliseconds, success);
        }
    }

    public void RecordNodeConnection(string nodeId, bool connected)
    {
        _nodeMetrics.AddOrUpdate(nodeId,
            new NodeMetrics { NodeId = nodeId, IsConnected = connected },
            (key, existing) => existing with { IsConnected = connected });

        logger.LogInformation("Node {NodeId} connection status changed: {Connected}", nodeId, connected);
    }

    public Task<DistributedMetricsSnapshot> GetDistributedMetricsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new DistributedMetricsSnapshot
        {
            TotalPublishedEvents = Interlocked.Read(ref _totalPublishedEvents),
            TotalReceivedEvents = Interlocked.Read(ref _totalReceivedEvents),
            FailedPublishedEvents = Interlocked.Read(ref _failedPublishedEvents),
            FailedReceivedEvents = Interlocked.Read(ref _failedReceivedEvents),
            NodeMetrics = new Dictionary<string, NodeMetrics>(_nodeMetrics),
            EventTypeDistribution = new Dictionary<string, long>(_eventTypeCounters)
        };

        return Task.FromResult(snapshot);
    }

    private static TimeSpan CalculateAverageLatency(TimeSpan current, TimeSpan newValue)
    {
        // 간단한 이동 평균
        return TimeSpan.FromMilliseconds((current.TotalMilliseconds * 0.8) + (newValue.TotalMilliseconds * 0.2));
    }
}

/// <summary>
/// 분산 모니터링 구성 옵션
/// </summary>
public class DistributedMonitoringOptions
{
    /// <summary>분산 이벤트 로깅 활성화</summary>
    public bool LogDistributedEvents { get; set; } = false;
    
    /// <summary>노드 메트릭 보존 기간</summary>
    public TimeSpan NodeMetricsRetention { get; set; } = TimeSpan.FromHours(24);
    
    /// <summary>비활성 노드 정리 간격</summary>
    public TimeSpan InactiveNodeCleanupInterval { get; set; } = TimeSpan.FromHours(1);
    
    /// <summary>최대 추적 노드 수</summary>
    public int MaxTrackedNodes { get; set; } = 100;
}
