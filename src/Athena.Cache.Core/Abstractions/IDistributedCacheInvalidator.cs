using Athena.Cache.Core.Enums;

namespace Athena.Cache.Core.Abstractions;

/// <summary>
/// 분산 환경에서 캐시 무효화를 위한 인터페이스
/// Redis Pub/Sub 등을 통해 다중 인스턴스 간 캐시 동기화 제공
/// </summary>
public interface IDistributedCacheInvalidator : ICacheInvalidator
{
    /// <summary>
    /// 분산 무효화 이벤트 수신 시작
    /// </summary>
    Task StartListeningAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 분산 무효화 이벤트 수신 중지
    /// </summary>
    Task StopListeningAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 분산 환경의 모든 인스턴스에 무효화 메시지 브로드캐스트
    /// </summary>
    Task BroadcastInvalidationAsync(string tableName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 패턴 기반 분산 무효화 브로드캐스트
    /// </summary>
    Task BroadcastInvalidationByPatternAsync(string pattern, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 배치 분산 무효화 브로드캐스트
    /// </summary>
    Task BroadcastBatchInvalidationAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 현재 인스턴스 ID (분산 환경에서 구분용)
    /// </summary>
    string InstanceId { get; }
    
    /// <summary>
    /// 분산 무효화 연결 상태
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// 무효화 이벤트 발생 시 호출되는 이벤트
    /// </summary>
    event EventHandler<InvalidationEventArgs> InvalidationReceived;
}

/// <summary>
/// 무효화 이벤트 인자
/// </summary>
public class InvalidationEventArgs : EventArgs
{
    public required string SourceInstanceId { get; init; }
    public required InvalidationMessage Message { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 분산 무효화 메시지
/// </summary>
public class InvalidationMessage
{
    public required InvalidationType Type { get; init; }
    public required string[] TableNames { get; init; }
    public string? Pattern { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}
