namespace Athena.Invalidation.Hierarchical.Abstractions;

/// <summary>
/// 계층적 무효화 트리를 관리하는 인터페이스
/// </summary>
public interface IHierarchicalInvalidationTree
{
    /// <summary>
    /// 계층 정의 추가
    /// </summary>
    void DefineLayer(string layerName, string? parentLayer = null, LayerProperties? properties = null);
    
    /// <summary>
    /// 테이블을 특정 계층에 할당
    /// </summary>
    void AssignTableToLayer(string tableName, string layerName);
    
    /// <summary>
    /// 계층적 무효화 계획 생성
    /// </summary>
    Task<HierarchicalInvalidationPlan> CreateInvalidationPlanAsync(
        string rootTable, 
        InvalidationDirection direction = InvalidationDirection.Down,
        int maxDepth = 5);
    
    /// <summary>
    /// 계층 간 관계 정의
    /// </summary>
    void DefineLayerRelationship(string parentLayer, string childLayer, LayerRelationType relationType);
    
    /// <summary>
    /// 특정 테이블이 속한 계층 조회
    /// </summary>
    string? GetTableLayer(string tableName);
    
    /// <summary>
    /// 특정 계층의 모든 테이블 조회
    /// </summary>
    IEnumerable<string> GetTablesInLayer(string layerName);
    
    /// <summary>
    /// 계층 구조 검증
    /// </summary>
    LayerValidationResult ValidateLayerStructure();
    
    /// <summary>
    /// 계층 구조 시각화 데이터
    /// </summary>
    LayerVisualizationData GetLayerVisualization();
}

/// <summary>
/// 무효화 방향
/// </summary>
public enum InvalidationDirection
{
    /// <summary>상위 계층으로</summary>
    Up,
    
    /// <summary>하위 계층으로</summary>
    Down,
    
    /// <summary>양방향</summary>
    Both,
    
    /// <summary>형제 계층으로</summary>
    Sibling
}

/// <summary>
/// 계층 관계 타입
/// </summary>
public enum LayerRelationType
{
    /// <summary>강한 부모-자식 관계</summary>
    StrongParentChild,
    
    /// <summary>약한 부모-자식 관계</summary>
    WeakParentChild,
    
    /// <summary>형제 관계</summary>
    Sibling,
    
    /// <summary>조건부 관계</summary>
    Conditional
}

/// <summary>
/// 계층 속성
/// </summary>
public class LayerProperties
{
    public int Priority { get; set; } = 0;
    public TimeSpan? DefaultCacheExpiration { get; set; }
    public bool EnableAutoInvalidation { get; set; } = true;
    public InvalidationStrategy Strategy { get; set; } = InvalidationStrategy.Immediate;
    public Dictionary<string, object> CustomProperties { get; set; } = new();
}

/// <summary>
/// 무효화 전략
/// </summary>
public enum InvalidationStrategy
{
    Immediate,
    Delayed,
    Batch,
    Conditional
}

/// <summary>
/// 계층적 무효화 계획
/// </summary>
public class HierarchicalInvalidationPlan
{
    public string RootTable { get; set; } = string.Empty;
    public InvalidationDirection Direction { get; set; }
    public List<InvalidationStep> Steps { get; set; } = new();
    public TimeSpan EstimatedDuration { get; set; }
    public int TotalTables { get; set; }
}

/// <summary>
/// 무효화 단계
/// </summary>
public class InvalidationStep
{
    public int Order { get; set; }
    public string LayerName { get; set; } = string.Empty;
    public List<string> Tables { get; set; } = new();
    public InvalidationStrategy Strategy { get; set; }
    public TimeSpan? Delay { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// 계층 구조 검증 결과
/// </summary>
public class LayerValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> OrphanedTables { get; set; } = new();
}

/// <summary>
/// 계층 시각화 데이터
/// </summary>
public class LayerVisualizationData
{
    public LayerNode[] Layers { get; set; } = Array.Empty<LayerNode>();
    public LayerConnection[] Connections { get; set; } = Array.Empty<LayerConnection>();
}

public class LayerNode
{
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public string[] Tables { get; set; } = Array.Empty<string>();
    public LayerProperties Properties { get; set; } = new();
}

public class LayerConnection
{
    public string FromLayer { get; set; } = string.Empty;
    public string ToLayer { get; set; } = string.Empty;
    public LayerRelationType Type { get; set; }
}