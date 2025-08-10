using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Hierarchical.Abstractions;
using System.Collections.Concurrent;

namespace Athena.Invalidation.Hierarchical.Implementations;

/// <summary>
/// 계층적 무효화 트리 구현
/// </summary>
public class HierarchicalInvalidationTree(
    IInvalidationEngine invalidationEngine,
    ILogger<HierarchicalInvalidationTree> logger)
    : IHierarchicalInvalidationTree
{
    private readonly IInvalidationEngine _invalidationEngine = invalidationEngine ?? throw new ArgumentNullException(nameof(invalidationEngine));
    private readonly ILogger<HierarchicalInvalidationTree> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<string, LayerInfo> _layers = new();
    private readonly ConcurrentDictionary<string, string> _tableToLayer = new();
    private readonly ConcurrentDictionary<string, HashSet<LayerRelation>> _layerRelations = new();

    public void DefineLayer(string layerName, string? parentLayer = null, LayerProperties? properties = null)
    {
        if (string.IsNullOrEmpty(layerName))
            throw new ArgumentException("Layer name cannot be null or empty", nameof(layerName));

        if (_layers.ContainsKey(layerName))
        {
            _logger.LogWarning("Layer {LayerName} already exists. Updating properties.", layerName);
        }

        var layerInfo = new LayerInfo
        {
            Name = layerName,
            ParentLayer = parentLayer,
            Properties = properties ?? new LayerProperties(),
            Tables = [],
            ChildLayers = []
        };

        _layers[layerName] = layerInfo;

        // 부모-자식 관계 설정
        if (!string.IsNullOrEmpty(parentLayer))
        {
            if (_layers.TryGetValue(parentLayer, out var parent))
            {
                parent.ChildLayers.Add(layerName);
                DefineLayerRelationship(parentLayer, layerName, LayerRelationType.StrongParentChild);
            }
            else
            {
                _logger.LogWarning("Parent layer {ParentLayer} not found for layer {LayerName}", parentLayer, layerName);
            }
        }

        _logger.LogInformation("Defined layer {LayerName} with parent {ParentLayer}", layerName, parentLayer ?? "none");
    }

    public void AssignTableToLayer(string tableName, string layerName)
    {
        if (string.IsNullOrEmpty(tableName))
            throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));
        if (string.IsNullOrEmpty(layerName))
            throw new ArgumentException("Layer name cannot be null or empty", nameof(layerName));

        if (!_layers.ContainsKey(layerName))
        {
            throw new ArgumentException($"Layer {layerName} does not exist", nameof(layerName));
        }

        // 기존 계층에서 제거
        if (_tableToLayer.TryRemove(tableName, out var oldLayer) && _layers.TryGetValue(oldLayer, out var oldLayerInfo))
        {
            oldLayerInfo.Tables.Remove(tableName);
            _logger.LogDebug("Removed table {TableName} from layer {OldLayer}", tableName, oldLayer);
        }

        // 새 계층에 할당
        _tableToLayer[tableName] = layerName;
        _layers[layerName].Tables.Add(tableName);

        _logger.LogInformation("Assigned table {TableName} to layer {LayerName}", tableName, layerName);
    }

    public async Task<HierarchicalInvalidationPlan> CreateInvalidationPlanAsync(
        string rootTable, 
        InvalidationDirection direction = InvalidationDirection.Down, 
        int maxDepth = 5)
    {
        if (string.IsNullOrEmpty(rootTable))
            throw new ArgumentException("Root table cannot be null or empty", nameof(rootTable));

        _logger.LogDebug("Creating invalidation plan for {RootTable} in direction {Direction} with max depth {MaxDepth}",
            rootTable, direction, maxDepth);

        var plan = new HierarchicalInvalidationPlan
        {
            RootTable = rootTable,
            Direction = direction,
            Steps = []
        };

        var rootLayer = GetTableLayer(rootTable);
        if (string.IsNullOrEmpty(rootLayer))
        {
            // 테이블이 어떤 계층에도 속하지 않는 경우
            plan.Steps.Add(new InvalidationStep
            {
                Order = 0,
                LayerName = "Unassigned",
                Tables = [rootTable],
                Strategy = InvalidationStrategy.Immediate
            });
            plan.TotalTables = 1;
            return plan;
        }

        var processedLayers = new HashSet<string>();
        var stepOrder = 0;

        BuildInvalidationPlan(rootLayer, direction, maxDepth, 0, processedLayers, plan, ref stepOrder);

        // 예상 실행 시간 계산
        plan.EstimatedDuration = CalculateEstimatedDuration(plan.Steps);
        plan.TotalTables = plan.Steps.SelectMany(s => s.Tables).Distinct().Count();

        _logger.LogInformation("Created invalidation plan with {StepCount} steps for {TotalTables} tables",
            plan.Steps.Count, plan.TotalTables);

        return plan;
    }

    public void DefineLayerRelationship(string parentLayer, string childLayer, LayerRelationType relationType)
    {
        if (string.IsNullOrEmpty(parentLayer) || string.IsNullOrEmpty(childLayer))
            throw new ArgumentException("Layer names cannot be null or empty");

        if (!_layers.ContainsKey(parentLayer) || !_layers.ContainsKey(childLayer))
            throw new ArgumentException("Both layers must exist before defining relationship");

        var relation = new LayerRelation
        {
            ParentLayer = parentLayer,
            ChildLayer = childLayer,
            RelationType = relationType
        };

        _layerRelations.AddOrUpdate(parentLayer,
            [relation],
            (key, existing) =>
            {
                existing.Add(relation);
                return existing;
            });

        _logger.LogDebug("Defined relationship: {ParentLayer} -> {ChildLayer} ({RelationType})",
            parentLayer, childLayer, relationType);
    }

    public string? GetTableLayer(string tableName)
    {
        _tableToLayer.TryGetValue(tableName, out var layer);
        return layer;
    }

    public IEnumerable<string> GetTablesInLayer(string layerName)
    {
        return _layers.TryGetValue(layerName, out var layer) 
            ? layer.Tables.ToList() 
            : Array.Empty<string>();
    }

    public LayerValidationResult ValidateLayerStructure()
    {
        var result = new LayerValidationResult { IsValid = true };

        // 순환 참조 검사
        CheckForCycles(result);

        // 고아 테이블 검사
        FindOrphanedTables(result);

        // 계층 일관성 검사
        ValidateLayerConsistency(result);

        result.IsValid = !result.Errors.Any();

        _logger.LogInformation("Layer validation completed - IsValid: {IsValid}, Errors: {ErrorCount}, Warnings: {WarningCount}",
            result.IsValid, result.Errors.Count, result.Warnings.Count);

        return result;
    }

    public LayerVisualizationData GetLayerVisualization()
    {
        var layerNodes = _layers.Values.Select(layer => new LayerNode
        {
            Name = layer.Name,
            Level = CalculateLayerLevel(layer.Name),
            Tables = layer.Tables.ToArray(),
            Properties = layer.Properties
        }).OrderBy(n => n.Level).ToArray();

        var connections = _layerRelations.Values
            .SelectMany(relations => relations)
            .Select(relation => new LayerConnection
            {
                FromLayer = relation.ParentLayer,
                ToLayer = relation.ChildLayer,
                Type = relation.RelationType
            }).ToArray();

        return new LayerVisualizationData
        {
            Layers = layerNodes,
            Connections = connections
        };
    }

    /// <summary>
    /// 계층적 무효화 실행
    /// </summary>
    public async Task ExecuteInvalidationPlanAsync(HierarchicalInvalidationPlan plan, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Executing invalidation plan with {StepCount} steps", plan.Steps.Count);

            var startTime = DateTimeOffset.UtcNow;

            foreach (var step in plan.Steps.OrderBy(s => s.Order))
            {
                await ExecuteInvalidationStep(step, cancellationToken);
            }

            var actualDuration = DateTimeOffset.UtcNow - startTime;
            _logger.LogInformation("Completed invalidation plan execution in {Duration}ms (estimated: {EstimatedDuration}ms)",
                actualDuration.TotalMilliseconds, plan.EstimatedDuration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute invalidation plan");
            throw;
        }
    }

    private void BuildInvalidationPlan(
        string layerName, 
        InvalidationDirection direction, 
        int maxDepth, 
        int currentDepth,
        HashSet<string> processedLayers, 
        HierarchicalInvalidationPlan plan, 
        ref int stepOrder)
    {
        if (currentDepth >= maxDepth || processedLayers.Contains(layerName))
            return;

        if (!_layers.TryGetValue(layerName, out var layer))
            return;

        processedLayers.Add(layerName);

        // 현재 계층의 무효화 단계 추가
        var step = new InvalidationStep
        {
            Order = stepOrder++,
            LayerName = layerName,
            Tables = layer.Tables.ToList(),
            Strategy = layer.Properties.Strategy,
            Delay = layer.Properties.Strategy == InvalidationStrategy.Delayed 
                ? TimeSpan.FromMilliseconds(100 * currentDepth) 
                : null
        };

        plan.Steps.Add(step);

        // 방향에 따른 다음 계층 탐색
        var nextLayers = GetNextLayers(layerName, direction);
        foreach (var nextLayer in nextLayers)
        {
            BuildInvalidationPlan(nextLayer, direction, maxDepth, currentDepth + 1, 
                processedLayers, plan, ref stepOrder);
        }
    }

    private async Task ExecuteInvalidationStep(InvalidationStep step, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Executing invalidation step {Order} for layer {LayerName} with {TableCount} tables",
                step.Order, step.LayerName, step.Tables.Count);

            // 지연 처리
            if (step.Delay.HasValue)
            {
                await Task.Delay(step.Delay.Value, cancellationToken);
            }

            // 전략에 따른 처리
            switch (step.Strategy)
            {
                case InvalidationStrategy.Immediate:
                    await ExecuteImmediateInvalidation(step.Tables, cancellationToken);
                    break;
                case InvalidationStrategy.Batch:
                    await ExecuteBatchInvalidation(step.Tables, cancellationToken);
                    break;
                case InvalidationStrategy.Conditional:
                    await ExecuteConditionalInvalidation(step.Tables, cancellationToken);
                    break;
                default:
                    await ExecuteImmediateInvalidation(step.Tables, cancellationToken);
                    break;
            }

            _logger.LogDebug("Completed invalidation step {Order} for layer {LayerName}", step.Order, step.LayerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute invalidation step {Order} for layer {LayerName}", 
                step.Order, step.LayerName);
            throw;
        }
    }

    private async Task ExecuteImmediateInvalidation(List<string> tables, CancellationToken cancellationToken)
    {
        var tasks = tables.Select(table => _invalidationEngine.InvalidateByTableAsync(table, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task ExecuteBatchInvalidation(List<string> tables, CancellationToken cancellationToken)
    {
        await _invalidationEngine.InvalidateBatchAsync(tables, cancellationToken);
    }

    private async Task ExecuteConditionalInvalidation(List<string> tables, CancellationToken cancellationToken)
    {
        // 조건부 무효화 - 여기서는 간단히 즉시 무효화로 처리
        await ExecuteImmediateInvalidation(tables, cancellationToken);
    }

    private List<string> GetNextLayers(string layerName, InvalidationDirection direction)
    {
        var nextLayers = new List<string>();

        switch (direction)
        {
            case InvalidationDirection.Down:
                if (_layers.TryGetValue(layerName, out var layer))
                {
                    nextLayers.AddRange(layer.ChildLayers);
                }
                break;
            case InvalidationDirection.Up:
                if (_layers.TryGetValue(layerName, out var currentLayer) && 
                    !string.IsNullOrEmpty(currentLayer.ParentLayer))
                {
                    nextLayers.Add(currentLayer.ParentLayer);
                }
                break;
            case InvalidationDirection.Both:
                var downLayers = GetNextLayers(layerName, InvalidationDirection.Down);
                var upLayers = GetNextLayers(layerName, InvalidationDirection.Up);
                nextLayers.AddRange(downLayers);
                nextLayers.AddRange(upLayers);
                break;
            case InvalidationDirection.Sibling:
                nextLayers.AddRange(GetSiblingLayers(layerName));
                break;
        }

        return nextLayers.Distinct().ToList();
    }

    private List<string> GetSiblingLayers(string layerName)
    {
        if (!_layers.TryGetValue(layerName, out var layer) || string.IsNullOrEmpty(layer.ParentLayer))
            return [];

        if (_layers.TryGetValue(layer.ParentLayer, out var parent))
        {
            return parent.ChildLayers.Where(child => child != layerName).ToList();
        }

        return [];
    }

    private int CalculateLayerLevel(string layerName)
    {
        if (!_layers.TryGetValue(layerName, out var layer))
            return 0;

        var level = 0;
        var current = layer;
        while (!string.IsNullOrEmpty(current.ParentLayer))
        {
            level++;
            if (!_layers.TryGetValue(current.ParentLayer, out current))
                break;
        }

        return level;
    }

    private TimeSpan CalculateEstimatedDuration(List<InvalidationStep> steps)
    {
        var totalMs = 0.0;
        foreach (var step in steps)
        {
            // 기본 처리 시간 (테이블 수에 비례)
            totalMs += step.Tables.Count * 10;
            
            // 지연 시간 추가
            if (step.Delay.HasValue)
            {
                totalMs += step.Delay.Value.TotalMilliseconds;
            }
        }

        return TimeSpan.FromMilliseconds(totalMs);
    }

    private void CheckForCycles(LayerValidationResult result)
    {
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var layer in _layers.Keys)
        {
            if (!visited.Contains(layer))
            {
                if (HasCycleDFS(layer, visited, recursionStack))
                {
                    result.Errors.Add($"Cyclic dependency detected involving layer {layer}");
                }
            }
        }
    }

    private bool HasCycleDFS(string layer, HashSet<string> visited, HashSet<string> recursionStack)
    {
        visited.Add(layer);
        recursionStack.Add(layer);

        if (_layers.TryGetValue(layer, out var layerInfo))
        {
            foreach (var childLayer in layerInfo.ChildLayers)
            {
                if (!visited.Contains(childLayer))
                {
                    if (HasCycleDFS(childLayer, visited, recursionStack))
                        return true;
                }
                else if (recursionStack.Contains(childLayer))
                {
                    return true;
                }
            }
        }

        recursionStack.Remove(layer);
        return false;
    }

    private void FindOrphanedTables(LayerValidationResult result)
    {
        var assignedTables = _tableToLayer.Keys.ToHashSet();
        var allTables = _layers.Values.SelectMany(l => l.Tables).ToHashSet();

        var orphanedTables = allTables.Except(assignedTables);
        result.OrphanedTables.AddRange(orphanedTables);

        if (result.OrphanedTables.Any())
        {
            result.Warnings.Add($"Found {result.OrphanedTables.Count} orphaned tables");
        }
    }

    private void ValidateLayerConsistency(LayerValidationResult result)
    {
        foreach (var kvp in _layers)
        {
            var layerName = kvp.Key;
            var layer = kvp.Value;

            // 부모 계층 존재 확인
            if (!string.IsNullOrEmpty(layer.ParentLayer) && !_layers.ContainsKey(layer.ParentLayer))
            {
                result.Errors.Add($"Layer {layerName} references non-existent parent layer {layer.ParentLayer}");
            }

            // 자식 계층 존재 확인
            foreach (var childLayer in layer.ChildLayers)
            {
                if (!_layers.ContainsKey(childLayer))
                {
                    result.Errors.Add($"Layer {layerName} references non-existent child layer {childLayer}");
                }
            }
        }
    }
}

/// <summary>
/// 계층 정보
/// </summary>
internal class LayerInfo
{
    public string Name { get; set; } = string.Empty;
    public string? ParentLayer { get; set; }
    public LayerProperties Properties { get; set; } = new();
    public HashSet<string> Tables { get; set; } = [];
    public HashSet<string> ChildLayers { get; set; } = [];
}

/// <summary>
/// 계층 간 관계 정보
/// </summary>
internal class LayerRelation : IEquatable<LayerRelation>
{
    public string ParentLayer { get; set; } = string.Empty;
    public string ChildLayer { get; set; } = string.Empty;
    public LayerRelationType RelationType { get; set; }

    public bool Equals(LayerRelation? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ParentLayer == other.ParentLayer && ChildLayer == other.ChildLayer;
    }

    public override bool Equals(object? obj) => Equals(obj as LayerRelation);

    public override int GetHashCode() => HashCode.Combine(ParentLayer, ChildLayer);
}
