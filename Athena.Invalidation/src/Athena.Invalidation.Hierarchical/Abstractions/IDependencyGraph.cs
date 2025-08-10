namespace Athena.Invalidation.Hierarchical.Abstractions;

/// <summary>
/// 캐시 의존성 그래프를 관리하는 인터페이스
/// </summary>
public interface IDependencyGraph
{
    /// <summary>
    /// 의존성 관계 추가
    /// </summary>
    void AddDependency(string source, string target, DependencyType type = DependencyType.Strong);
    
    /// <summary>
    /// 의존성 관계 제거
    /// </summary>
    void RemoveDependency(string source, string target);
    
    /// <summary>
    /// 특정 노드의 모든 의존성 관계 제거
    /// </summary>
    void RemoveNode(string node);
    
    /// <summary>
    /// 의존성 그래프에서 영향을 받는 모든 노드 조회
    /// </summary>
    IEnumerable<DependencyNode> GetAffectedNodes(string sourceNode, int maxDepth = 10);
    
    /// <summary>
    /// 순환 의존성 검사
    /// </summary>
    bool HasCyclicDependency();
    
    /// <summary>
    /// 특정 노드들 간의 의존성 경로 찾기
    /// </summary>
    IEnumerable<string[]> FindPaths(string source, string target, int maxDepth = 10);
    
    /// <summary>
    /// 그래프 통계 정보
    /// </summary>
    GraphStatistics GetStatistics();
    
    /// <summary>
    /// 의존성 그래프 시각화용 데이터
    /// </summary>
    GraphVisualizationData GetVisualizationData();
}

/// <summary>
/// 의존성 타입
/// </summary>
public enum DependencyType
{
    /// <summary>강한 의존성 - 반드시 무효화되어야 함</summary>
    Strong,
    
    /// <summary>약한 의존성 - 선택적으로 무효화</summary>
    Weak,
    
    /// <summary>조건적 의존성 - 특정 조건에서만 무효화</summary>
    Conditional,
    
    /// <summary>지연 의존성 - 지연된 시간 후 무효화</summary>
    Delayed
}

/// <summary>
/// 의존성 노드 정보
/// </summary>
public class DependencyNode
{
    public string Name { get; set; } = string.Empty;
    public DependencyType Type { get; set; }
    public int Depth { get; set; }
    public TimeSpan? Delay { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
    public string[] Path { get; set; } = [];
}

/// <summary>
/// 그래프 통계 정보
/// </summary>
public class GraphStatistics
{
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public int MaxDepth { get; set; }
    public bool HasCycles { get; set; }
    public Dictionary<DependencyType, int> DependencyTypeDistribution { get; set; } = new();
}

/// <summary>
/// 그래프 시각화 데이터
/// </summary>
public class GraphVisualizationData
{
    public GraphNode[] Nodes { get; set; } = [];
    public GraphEdge[] Edges { get; set; } = [];
}

public class GraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class GraphEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public DependencyType Type { get; set; }
    public string Label { get; set; } = string.Empty;
}
