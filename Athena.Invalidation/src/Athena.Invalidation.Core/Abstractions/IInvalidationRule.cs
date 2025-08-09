namespace Athena.Invalidation.Core.Abstractions;

/// <summary>
/// 무효화 규칙을 정의하는 인터페이스
/// 특정 조건에서 자동으로 캐시를 무효화하는 로직을 구현
/// </summary>
public interface IInvalidationRule
{
    /// <summary>
    /// 규칙 고유 식별자
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// 규칙 이름
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 규칙 설명
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 규칙 우선순위 (높을수록 먼저 실행)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 규칙이 활성화되어 있는지 여부
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 규칙 조건 평가 - 무효화를 실행해야 하는지 확인
    /// </summary>
    Task<bool> ShouldInvalidateAsync(IInvalidationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 무효화할 대상들 결정
    /// </summary>
    Task<IEnumerable<InvalidationTarget>> GetInvalidationTargetsAsync(IInvalidationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 규칙 검증 - 규칙이 올바르게 설정되었는지 확인
    /// </summary>
    Task<RuleValidationResult> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 무효화 대상
/// </summary>
public class InvalidationTarget
{
    public InvalidationTargetType Type { get; set; }
    public string Target { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
    public int Priority { get; set; }
}

/// <summary>
/// 무효화 대상 타입
/// </summary>
public enum InvalidationTargetType
{
    Table,
    Pattern,
    Key,
    Hierarchy,
    Custom
}

/// <summary>
/// 규칙 검증 결과
/// </summary>
public class RuleValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static RuleValidationResult Valid() => new() { IsValid = true };
    
    public static RuleValidationResult Invalid(params string[] errors) 
        => new() { IsValid = false, Errors = errors.ToList() };
}