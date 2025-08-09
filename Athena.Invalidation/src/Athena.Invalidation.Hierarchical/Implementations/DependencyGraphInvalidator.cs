using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Hierarchical.Abstractions;
using System.Collections.Concurrent;

namespace Athena.Invalidation.Hierarchical.Implementations;

/// <summary>
/// 의존성 그래프 기반 캐시 무효화 구현
/// </summary>
public class DependencyGraphInvalidator : IDependencyGraph
{
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly ILogger<DependencyGraphInvalidator> _logger;
    private readonly ConcurrentDictionary<string, HashSet<DependencyEdge>> _dependencies = new();
    private readonly ConcurrentDictionary<string, HashSet<DependencyEdge>> _reverseDependencies = new();

    public DependencyGraphInvalidator(
        IInvalidationEngine invalidationEngine,
        ILogger<DependencyGraphInvalidator> logger)
    {
        _invalidationEngine = invalidationEngine ?? throw new ArgumentNullException(nameof(invalidationEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void AddDependency(string source, string target, DependencyType type = DependencyType.Strong)
    {
        if (string.IsNullOrEmpty(source)) throw new ArgumentException("Source cannot be null or empty", nameof(source));
        if (string.IsNullOrEmpty(target)) throw new ArgumentException("Target cannot be null or empty", nameof(target));
        if (source == target) throw new ArgumentException("Cannot create self-dependency");

        var edge = new DependencyEdge(source, target, type);

        // 순환 의존성 검사 (새 의존성 추가 전)
        if (WouldCreateCycle(source, target))
        {
            throw new InvalidOperationException($"Adding dependency {source} -> {target} would create a cycle");
        }

        // Forward dependencies
        _dependencies.AddOrUpdate(source,
            new HashSet<DependencyEdge> { edge },
            (key, existing) =>
            {
                existing.Add(edge);
                return existing;
            });

        // Reverse dependencies for faster traversal
        _reverseDependencies.AddOrUpdate(target,
            new HashSet<DependencyEdge> { edge },
            (key, existing) =>
            {
                existing.Add(edge);
                return existing;
            });

        _logger.LogDebug("Added dependency: {Source} -> {Target} ({Type})", source, target, type);
    }

    public void RemoveDependency(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return;

        // Remove from forward dependencies
        if (_dependencies.TryGetValue(source, out var forwardEdges))
        {
            forwardEdges.RemoveWhere(e => e.Target == target);
            if (!forwardEdges.Any())
            {
                _dependencies.TryRemove(source, out _);
            }
        }

        // Remove from reverse dependencies
        if (_reverseDependencies.TryGetValue(target, out var reverseEdges))
        {
            reverseEdges.RemoveWhere(e => e.Source == source);
            if (!reverseEdges.Any())
            {
                _reverseDependencies.TryRemove(target, out _);
            }
        }

        _logger.LogDebug("Removed dependency: {Source} -> {Target}", source, target);
    }

    public void RemoveNode(string node)
    {
        if (string.IsNullOrEmpty(node)) return;

        // Remove all outgoing dependencies
        if (_dependencies.TryRemove(node, out var outgoingEdges))
        {
            foreach (var edge in outgoingEdges)
            {
                if (_reverseDependencies.TryGetValue(edge.Target, out var reverseEdges))
                {
                    reverseEdges.RemoveWhere(e => e.Source == node);
                    if (!reverseEdges.Any())
                    {
                        _reverseDependencies.TryRemove(edge.Target, out _);
                    }
                }
            }
        }

        // Remove all incoming dependencies
        if (_reverseDependencies.TryRemove(node, out var incomingEdges))
        {
            foreach (var edge in incomingEdges)
            {
                if (_dependencies.TryGetValue(edge.Source, out var forwardEdges))
                {
                    forwardEdges.RemoveWhere(e => e.Target == node);
                    if (!forwardEdges.Any())
                    {
                        _dependencies.TryRemove(edge.Source, out _);
                    }
                }
            }
        }

        _logger.LogDebug("Removed node: {Node}", node);
    }

    public IEnumerable<DependencyNode> GetAffectedNodes(string sourceNode, int maxDepth = 10)
    {
        if (string.IsNullOrEmpty(sourceNode) || maxDepth <= 0)
            return Array.Empty<DependencyNode>();

        var visited = new HashSet<string>();
        var result = new List<DependencyNode>();
        var queue = new Queue<(string node, int depth, string[] path, DependencyType type, TimeSpan? delay)>();

        queue.Enqueue((sourceNode, 0, new[] { sourceNode }, DependencyType.Strong, null));

        while (queue.Count > 0)
        {
            var (currentNode, currentDepth, currentPath, currentType, currentDelay) = queue.Dequeue();

            if (currentDepth >= maxDepth || visited.Contains(currentNode))
                continue;

            visited.Add(currentNode);

            if (currentDepth > 0) // Don't include the source node itself
            {
                result.Add(new DependencyNode
                {
                    Name = currentNode,
                    Type = currentType,
                    Depth = currentDepth,
                    Delay = currentDelay,
                    Path = currentPath.ToArray()
                });
            }

            // Get all dependencies of current node
            if (_dependencies.TryGetValue(currentNode, out var edges))
            {
                foreach (var edge in edges.OrderBy(e => e.Type))
                {
                    if (!visited.Contains(edge.Target))
                    {
                        var newPath = currentPath.Concat(new[] { edge.Target }).ToArray();
                        var delay = CalculateDelay(edge.Type);
                        queue.Enqueue((edge.Target, currentDepth + 1, newPath, edge.Type, delay));
                    }
                }
            }
        }

        _logger.LogDebug("Found {Count} affected nodes for {SourceNode} within depth {MaxDepth}",
            result.Count, sourceNode, maxDepth);

        return result.OrderBy(n => n.Depth).ThenBy(n => n.Type);
    }

    public bool HasCyclicDependency()
    {
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var node in _dependencies.Keys)
        {
            if (!visited.Contains(node))
            {
                if (HasCycleDFS(node, visited, recursionStack))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public IEnumerable<string[]> FindPaths(string source, string target, int maxDepth = 10)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target) || maxDepth <= 0)
            return Array.Empty<string[]>();

        var paths = new List<string[]>();
        var currentPath = new List<string>();
        
        FindPathsDFS(source, target, currentPath, paths, maxDepth, new HashSet<string>());

        _logger.LogDebug("Found {PathCount} paths from {Source} to {Target}", paths.Count, source, target);
        
        return paths;
    }

    public GraphStatistics GetStatistics()
    {
        var nodeCount = _dependencies.Keys.Union(_reverseDependencies.Keys).Count();
        var edgeCount = _dependencies.Values.SelectMany(edges => edges).Count();
        var maxDepth = CalculateMaxDepth();
        var hasCycles = HasCyclicDependency();
        var dependencyTypeDistribution = _dependencies.Values
            .SelectMany(edges => edges)
            .GroupBy(e => e.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        return new GraphStatistics
        {
            NodeCount = nodeCount,
            EdgeCount = edgeCount,
            MaxDepth = maxDepth,
            HasCycles = hasCycles,
            DependencyTypeDistribution = dependencyTypeDistribution
        };
    }

    public GraphVisualizationData GetVisualizationData()
    {
        var allNodes = _dependencies.Keys.Union(_reverseDependencies.Keys).ToHashSet();
        var nodes = allNodes.Select(node => new GraphNode
        {
            Id = node,
            Label = node,
            Group = GetNodeGroup(node)
        }).ToArray();

        var edges = _dependencies.Values
            .SelectMany(edgeSet => edgeSet)
            .Select(edge => new GraphEdge
            {
                From = edge.Source,
                To = edge.Target,
                Type = edge.Type,
                Label = edge.Type.ToString()
            }).ToArray();

        return new GraphVisualizationData
        {
            Nodes = nodes,
            Edges = edges
        };
    }

    /// <summary>
    /// 의존성 그래프에 기반하여 계층적 무효화 실행
    /// </summary>
    public async Task InvalidateWithDependenciesAsync(string sourceNode, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Starting dependency-based invalidation for {SourceNode}", sourceNode);

            var affectedNodes = GetAffectedNodes(sourceNode).ToList();
            
            if (!affectedNodes.Any())
            {
                // Source node만 무효화
                await _invalidationEngine.InvalidateByTableAsync(sourceNode, cancellationToken);
                return;
            }

            // 깊이별로 그룹화하여 순차적으로 무효화
            var nodesByDepth = affectedNodes
                .GroupBy(n => n.Depth)
                .OrderBy(g => g.Key)
                .ToList();

            // Source node 먼저 무효화
            await _invalidationEngine.InvalidateByTableAsync(sourceNode, cancellationToken);

            foreach (var depthGroup in nodesByDepth)
            {
                var tasks = new List<Task>();

                foreach (var node in depthGroup)
                {
                    tasks.Add(ProcessNodeInvalidation(node, cancellationToken));
                }

                // 같은 깊이의 노드들은 병렬로 처리
                await Task.WhenAll(tasks);

                _logger.LogDebug("Completed invalidation for depth {Depth} - {NodeCount} nodes",
                    depthGroup.Key, depthGroup.Count());
            }

            _logger.LogInformation("Completed dependency-based invalidation for {SourceNode} - {AffectedCount} nodes affected",
                sourceNode, affectedNodes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform dependency-based invalidation for {SourceNode}", sourceNode);
            throw;
        }
    }

    private async Task ProcessNodeInvalidation(DependencyNode node, CancellationToken cancellationToken)
    {
        try
        {
            // Delayed 타입의 경우 지연 시간 적용
            if (node.Type == DependencyType.Delayed && node.Delay.HasValue)
            {
                await Task.Delay(node.Delay.Value, cancellationToken);
            }

            // Conditional 타입의 경우 조건 확인 (여기서는 간단히 무효화)
            if (node.Type == DependencyType.Weak)
            {
                // Weak 의존성은 실패해도 무시
                try
                {
                    await _invalidationEngine.InvalidateByTableAsync(node.Name, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to invalidate weak dependency {NodeName}, continuing", node.Name);
                }
            }
            else
            {
                await _invalidationEngine.InvalidateByTableAsync(node.Name, cancellationToken);
            }

            _logger.LogDebug("Invalidated node {NodeName} (Type: {Type}, Depth: {Depth})",
                node.Name, node.Type, node.Depth);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate node {NodeName}", node.Name);
            if (node.Type == DependencyType.Strong)
            {
                throw; // Strong dependencies는 실패 시 전체 실패
            }
        }
    }

    private bool WouldCreateCycle(string source, string target)
    {
        // Check if target can reach source (which would create a cycle)
        var visited = new HashSet<string>();
        return CanReach(target, source, visited);
    }

    private bool CanReach(string from, string to, HashSet<string> visited)
    {
        if (from == to) return true;
        if (visited.Contains(from)) return false;

        visited.Add(from);

        if (_dependencies.TryGetValue(from, out var edges))
        {
            foreach (var edge in edges)
            {
                if (CanReach(edge.Target, to, visited))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasCycleDFS(string node, HashSet<string> visited, HashSet<string> recursionStack)
    {
        visited.Add(node);
        recursionStack.Add(node);

        if (_dependencies.TryGetValue(node, out var edges))
        {
            foreach (var edge in edges)
            {
                if (!visited.Contains(edge.Target))
                {
                    if (HasCycleDFS(edge.Target, visited, recursionStack))
                    {
                        return true;
                    }
                }
                else if (recursionStack.Contains(edge.Target))
                {
                    return true;
                }
            }
        }

        recursionStack.Remove(node);
        return false;
    }

    private void FindPathsDFS(string current, string target, List<string> currentPath, List<string[]> allPaths, int maxDepth, HashSet<string> visited)
    {
        if (currentPath.Count >= maxDepth) return;

        currentPath.Add(current);
        visited.Add(current);

        if (current == target)
        {
            allPaths.Add(currentPath.ToArray());
        }
        else if (_dependencies.TryGetValue(current, out var edges))
        {
            foreach (var edge in edges)
            {
                if (!visited.Contains(edge.Target))
                {
                    FindPathsDFS(edge.Target, target, currentPath, allPaths, maxDepth, visited);
                }
            }
        }

        currentPath.RemoveAt(currentPath.Count - 1);
        visited.Remove(current);
    }

    private int CalculateMaxDepth()
    {
        var maxDepth = 0;
        var visited = new HashSet<string>();

        foreach (var startNode in _dependencies.Keys)
        {
            if (!visited.Contains(startNode))
            {
                var depth = CalculateDepthDFS(startNode, new HashSet<string>());
                maxDepth = Math.Max(maxDepth, depth);
            }
        }

        return maxDepth;
    }

    private int CalculateDepthDFS(string node, HashSet<string> visited)
    {
        if (visited.Contains(node)) return 0;

        visited.Add(node);
        var maxChildDepth = 0;

        if (_dependencies.TryGetValue(node, out var edges))
        {
            foreach (var edge in edges)
            {
                var childDepth = CalculateDepthDFS(edge.Target, visited);
                maxChildDepth = Math.Max(maxChildDepth, childDepth);
            }
        }

        visited.Remove(node);
        return maxChildDepth + 1;
    }

    private TimeSpan? CalculateDelay(DependencyType type)
    {
        return type switch
        {
            DependencyType.Delayed => TimeSpan.FromMilliseconds(500),
            _ => null
        };
    }

    private string GetNodeGroup(string node)
    {
        // Simple grouping based on node name patterns
        if (node.ToLower().Contains("user")) return "Users";
        if (node.ToLower().Contains("order")) return "Orders";
        if (node.ToLower().Contains("product")) return "Products";
        return "Default";
    }
}

/// <summary>
/// 의존성 간선 정보
/// </summary>
internal class DependencyEdge : IEquatable<DependencyEdge>
{
    public string Source { get; }
    public string Target { get; }
    public DependencyType Type { get; }

    public DependencyEdge(string source, string target, DependencyType type)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Type = type;
    }

    public bool Equals(DependencyEdge? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Source == other.Source && Target == other.Target;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as DependencyEdge);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Source, Target);
    }
}