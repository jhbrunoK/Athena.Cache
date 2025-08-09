namespace Athena.Cache.Core.Abstractions;

/// <summary>
/// 캐시 메트릭 공통 인터페이스
/// Core와 Monitoring 라이브러리 간 표준화된 메트릭 교환을 위한 계약
/// </summary>
public interface ICacheMetrics
{
    /// <summary>메트릭 수집 시점</summary>
    DateTime Timestamp { get; }
    
    /// <summary>캐시 히트율 (0.0 ~ 1.0)</summary>
    double HitRatio { get; }
    
    /// <summary>총 히트 수</summary>
    long TotalHits { get; }
    
    /// <summary>총 미스 수</summary>
    long TotalMisses { get; }
    
    /// <summary>총 요청 수</summary>
    long TotalRequests => TotalHits + TotalMisses;
    
    /// <summary>메모리 사용량 (바이트)</summary>
    long MemoryUsageBytes { get; }
    
    /// <summary>메모리 사용량 (MB)</summary>
    double MemoryUsageMB => MemoryUsageBytes / (1024.0 * 1024.0);
    
    /// <summary>캐시 아이템 수</summary>
    long ItemCount { get; }
    
    /// <summary>평균 응답 시간 (밀리초)</summary>
    double AverageResponseTimeMs { get; }
    
    /// <summary>총 에러 수</summary>
    long TotalErrors { get; }
    
    /// <summary>에러율 (0.0 ~ 1.0)</summary>
    double ErrorRate => TotalRequests > 0 ? (double)TotalErrors / TotalRequests : 0.0;
    
    /// <summary>핫키 수</summary>
    long HotKeysCount { get; }
    
    /// <summary>무효화 수</summary>
    long TotalInvalidations { get; }
}

/// <summary>
/// 확장 가능한 캐시 메트릭 인터페이스
/// Monitoring 라이브러리에서 추가 메트릭을 위해 사용
/// </summary>
public interface IExtendedCacheMetrics : ICacheMetrics
{
    /// <summary>활성 연결 수</summary>
    int ConnectionCount { get; }
    
    /// <summary>사용자 정의 메트릭</summary>
    IReadOnlyDictionary<string, object> CustomMetrics { get; }
    
    /// <summary>분산 무효화 메시지 수</summary>
    long DistributedInvalidationMessages { get; }
    
    /// <summary>Circuit Breaker 상태</summary>
    string CircuitBreakerState { get; }
}