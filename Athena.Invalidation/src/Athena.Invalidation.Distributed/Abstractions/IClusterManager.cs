namespace Athena.Invalidation.Distributed.Abstractions;

/// <summary>
/// 클러스터 관리자 - 노드 검색 및 상태 관리
/// </summary>
public interface IClusterManager
{
    /// <summary>현재 노드 정보</summary>
    ClusterNode CurrentNode { get; }
    
    /// <summary>
    /// 클러스터에 참여
    /// </summary>
    Task JoinClusterAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 클러스터에서 탈퇴
    /// </summary>
    Task LeaveClusterAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 활성 노드 목록 조회
    /// </summary>
    Task<IEnumerable<ClusterNode>> GetActiveNodesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 특정 노드 상태 조회
    /// </summary>
    Task<ClusterNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 하트비트 전송
    /// </summary>
    Task SendHeartbeatAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 리더 선출 (Primary 노드 결정)
    /// </summary>
    Task<ClusterNode?> ElectLeaderAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 현재 노드가 리더인지 확인
    /// </summary>
    Task<bool> IsLeaderAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 클러스터 상태 확인
    /// </summary>
    Task<ClusterHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 노드 추가/제거/상태 변경 이벤트
    /// </summary>
    event EventHandler<ClusterTopologyChangedEventArgs> TopologyChanged;
}

/// <summary>
/// 클러스터 토폴로지 변경 이벤트 인자
/// </summary>
public class ClusterTopologyChangedEventArgs : EventArgs
{
    public ClusterTopologyChangeType ChangeType { get; set; }
    public ClusterNode Node { get; set; } = new();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 클러스터 토폴로지 변경 타입
/// </summary>
public enum ClusterTopologyChangeType
{
    NodeJoined,
    NodeLeft,
    NodeStatusChanged,
    LeaderChanged
}

/// <summary>
/// 클러스터 헬스 상태
/// </summary>
public class ClusterHealthStatus
{
    public bool IsHealthy { get; set; }
    public int TotalNodes { get; set; }
    public int OnlineNodes { get; set; }
    public int OfflineNodes { get; set; }
    public ClusterNode? Leader { get; set; }
    public TimeSpan MaxHeartbeatAge { get; set; }
    public List<string> Issues { get; set; } = new();
    public Dictionary<string, object> Metrics { get; set; } = new();
}

/// <summary>
/// 분산 락 관리자
/// </summary>
public interface IDistributedLockManager
{
    /// <summary>
    /// 분산 락 획득 시도
    /// </summary>
    Task<IDistributedLock?> TryAcquireLockAsync(string lockKey, TimeSpan expiry, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 분산 락 강제 해제
    /// </summary>
    Task ReleaseLockAsync(string lockKey, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 락 소유자 확인
    /// </summary>
    Task<string?> GetLockOwnerAsync(string lockKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// 분산 락
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
    /// <summary>락 키</summary>
    string LockKey { get; }
    
    /// <summary>락 소유자</summary>
    string Owner { get; }
    
    /// <summary>만료 시간</summary>
    DateTimeOffset ExpiresAt { get; }
    
    /// <summary>락 갱신</summary>
    Task<bool> RenewAsync(TimeSpan additionalTime, CancellationToken cancellationToken = default);
}