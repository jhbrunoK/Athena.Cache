namespace Athena.Invalidation.CQRS.Abstractions;

/// <summary>
/// CQRS 명령을 나타내는 마커 인터페이스
/// </summary>
public interface ICommand
{
    /// <summary>
    /// 명령 고유 식별자
    /// </summary>
    string CommandId { get; }
    
    /// <summary>
    /// 명령 실행 시각
    /// </summary>
    DateTimeOffset Timestamp { get; }
    
    /// <summary>
    /// 명령 실행자 (사용자 ID 등)
    /// </summary>
    string? ExecutedBy { get; }
    
    /// <summary>
    /// 명령과 관련된 메타데이터
    /// </summary>
    Dictionary<string, object> Metadata { get; }
}

/// <summary>
/// 결과를 반환하는 CQRS 명령
/// </summary>
public interface ICommand<out TResult> : ICommand
{
}

/// <summary>
/// 기본 명령 구현체
/// </summary>
public abstract class BaseCommand : ICommand
{
    public string CommandId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? ExecutedBy { get; set; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// 결과를 반환하는 기본 명령 구현체
/// </summary>
public abstract class BaseCommand<TResult> : BaseCommand, ICommand<TResult>
{
}
