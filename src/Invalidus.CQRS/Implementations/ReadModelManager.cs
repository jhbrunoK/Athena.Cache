using Invalidus.Core.Abstractions;
using Invalidus.CQRS.Abstractions;

namespace Invalidus.CQRS.Implementations;

/// <summary>
/// Read Model 관리자 구현체
/// Read model manager implementation
/// </summary>
public class ReadModelManager : IReadModelManager
{
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<ReadModelManager> _logger;
    private readonly ConcurrentDictionary<string, ReadModelMetadata> _readModelMetadata = new();
    private readonly ConcurrentDictionary<string, List<ReadModelDependency>> _dependencies = new();

    public ReadModelManager(
        IInvalidationEngine invalidationEngine,
        ILogger<ReadModelManager> logger,
        IEventPublisher? eventPublisher = null)
    {
        _invalidationEngine = invalidationEngine ?? throw new ArgumentNullException(nameof(invalidationEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventPublisher = eventPublisher;
    }

    public async Task InvalidateReadModelAsync(string readModelType, string? aggregateId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(readModelType)) 
            throw new ArgumentException("Read model type cannot be null or empty", nameof(readModelType));

        try
        {
            _logger.LogDebug("Invalidating read model {ReadModelType} for aggregate {AggregateId}", 
                readModelType, aggregateId);

            // Use generic method for read model invalidation
            var readModelTypeObj = Type.GetType(readModelType);
            if (readModelTypeObj != null)
            {
                var method = typeof(IInvalidationEngine).GetMethod("InvalidateReadModelAsync")?.MakeGenericMethod(readModelTypeObj);
                var task = (Task?)method?.Invoke(_invalidationEngine, new object?[] { aggregateId, cancellationToken });
                if (task != null)
                {
                    await task;
                }
            }

            // Publish read model updated event
            if (_eventPublisher != null)
            {
                var readModelEvent = new ReadModelUpdatedEvent
                {
                    ReadModelType = readModelType,
                    ReadModelId = aggregateId ?? "*",
                    UpdateReason = "Manual invalidation",
                    RequiresCacheInvalidation = true,
                    Metadata = new Dictionary<string, object>
                    {
                        ["invalidation_type"] = "manual",
                        ["read_model_type"] = readModelType,
                        ["aggregate_id"] = aggregateId ?? "all"
                    }
                };

                await _eventPublisher.PublishAsync(readModelEvent, cancellationToken);
            }

            _logger.LogInformation("Read model {ReadModelType} invalidated successfully", readModelType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate read model {ReadModelType} for aggregate {AggregateId}", 
                readModelType, aggregateId);
            throw;
        }
    }

    public async Task InvalidateRelatedReadModelsAsync(string entityType, string entityId, IEnumerable<string>? changedProperties = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityType)) 
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));
        if (string.IsNullOrWhiteSpace(entityId)) 
            throw new ArgumentException("Entity ID cannot be null or empty", nameof(entityId));

        try
        {
            var propertyList = changedProperties?.ToList() ?? new List<string>();
            _logger.LogDebug("Invalidating related read models for entity {EntityType}:{EntityId} with changed properties: {Properties}", 
                entityType, entityId, string.Join(", ", propertyList));

            // Find all dependencies for this entity type
            var allDependencies = await GetReadModelDependenciesAsync(entityType, cancellationToken);
            var relevantDependencies = propertyList.Any() 
                ? allDependencies.Where(dep => dep.Properties == null || dep.Properties.Intersect(propertyList).Any())
                : allDependencies;

            var invalidationTasks = new List<Task>();

            foreach (var dependency in relevantDependencies)
            {
                var task = ExecuteInvalidationStrategy(dependency, entityId, propertyList, cancellationToken);
                
                if (dependency.Strategy == InvalidationStrategy.Immediate)
                {
                    invalidationTasks.Add(task);
                }
                else
                {
                    // Fire and forget for delayed/batched strategies
                    _ = task;
                }
            }

            // Wait for immediate invalidations
            if (invalidationTasks.Any())
            {
                await Task.WhenAll(invalidationTasks);
            }

            _logger.LogInformation("Invalidated {DependencyCount} related read models for entity {EntityType}:{EntityId}", 
                relevantDependencies.Count(), entityType, entityId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate related read models for entity {EntityType}:{EntityId}", 
                entityType, entityId);
            throw;
        }
    }

    public async Task RefreshReadModelAsync(string readModelType, string aggregateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(readModelType)) 
            throw new ArgumentException("Read model type cannot be null or empty", nameof(readModelType));
        if (string.IsNullOrWhiteSpace(aggregateId)) 
            throw new ArgumentException("Aggregate ID cannot be null or empty", nameof(aggregateId));

        try
        {
            _logger.LogDebug("Refreshing read model {ReadModelType} for aggregate {AggregateId}", 
                readModelType, aggregateId);

            // First invalidate the existing read model
            await InvalidateReadModelAsync(readModelType, aggregateId, cancellationToken);

            // Update metadata
            if (_readModelMetadata.TryGetValue(readModelType, out var metadata))
            {
                var updatedMetadata = metadata with 
                { 
                    LastUpdatedAt = DateTime.UtcNow,
                    Version = metadata.Version + 1
                };
                _readModelMetadata[readModelType] = updatedMetadata;
            }

            _logger.LogInformation("Read model {ReadModelType} refreshed for aggregate {AggregateId}", 
                readModelType, aggregateId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh read model {ReadModelType} for aggregate {AggregateId}", 
                readModelType, aggregateId);
            throw;
        }
    }

    public async Task RefreshReadModelsBatchAsync(IEnumerable<ReadModelRefreshRequest> requests, CancellationToken cancellationToken = default)
    {
        if (requests == null) throw new ArgumentNullException(nameof(requests));

        var requestList = requests.ToList();
        _logger.LogInformation("Refreshing batch of {RequestCount} read models", requestList.Count);

        try
        {
            var refreshTasks = requestList.Select(request => 
                RefreshReadModelWithModeAsync(request, cancellationToken));

            await Task.WhenAll(refreshTasks);

            _logger.LogInformation("Successfully refreshed batch of {RequestCount} read models", requestList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh read models batch");
            throw;
        }
    }

    public Task RegisterReadModelDependencyAsync(string readModelType, string dependsOnEntity, IEnumerable<string>? properties = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(readModelType)) 
            throw new ArgumentException("Read model type cannot be null or empty", nameof(readModelType));
        if (string.IsNullOrWhiteSpace(dependsOnEntity)) 
            throw new ArgumentException("Depends on entity cannot be null or empty", nameof(dependsOnEntity));

        var dependency = new ReadModelDependency
        {
            ReadModelType = readModelType,
            DependsOnEntity = dependsOnEntity,
            Properties = properties?.ToList(),
            Strategy = InvalidationStrategy.Immediate,
            CascadeInvalidation = true
        };

        _dependencies.AddOrUpdate(dependsOnEntity,
            new List<ReadModelDependency> { dependency },
            (_, existing) =>
            {
                // Remove existing dependency for same read model type
                var filtered = existing.Where(d => d.ReadModelType != readModelType).ToList();
                filtered.Add(dependency);
                return filtered;
            });

        _logger.LogInformation("Registered read model dependency: {ReadModelType} -> {DependsOnEntity}", 
            readModelType, dependsOnEntity);

        return Task.CompletedTask;
    }

    public Task<IEnumerable<ReadModelDependency>> GetReadModelDependenciesAsync(string entityType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityType)) 
            throw new ArgumentException("Entity type cannot be null or empty", nameof(entityType));

        var dependencies = _dependencies.TryGetValue(entityType, out var deps) 
            ? deps.AsEnumerable() 
            : Enumerable.Empty<ReadModelDependency>();

        return Task.FromResult(dependencies);
    }

    public Task<ReadModelMetadata?> GetReadModelMetadataAsync(string readModelType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(readModelType)) 
            throw new ArgumentException("Read model type cannot be null or empty", nameof(readModelType));

        var metadata = _readModelMetadata.TryGetValue(readModelType, out var meta) ? meta : null;
        return Task.FromResult(metadata);
    }

    /// <summary>
    /// Read Model 메타데이터 등록
    /// Register read model metadata
    /// </summary>
    public Task RegisterReadModelMetadataAsync(ReadModelMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (metadata == null) throw new ArgumentNullException(nameof(metadata));

        _readModelMetadata[metadata.ReadModelType] = metadata;

        _logger.LogInformation("Registered read model metadata for {ReadModelType}", metadata.ReadModelType);

        return Task.CompletedTask;
    }

    private async Task ExecuteInvalidationStrategy(ReadModelDependency dependency, string entityId, List<string> changedProperties, CancellationToken cancellationToken)
    {
        try
        {
            switch (dependency.Strategy)
            {
                case InvalidationStrategy.Immediate:
                    await InvalidateReadModelAsync(dependency.ReadModelType, entityId, cancellationToken);
                    break;

                case InvalidationStrategy.Delayed:
                    if (dependency.Delay.HasValue)
                    {
                        await Task.Delay(dependency.Delay.Value, cancellationToken);
                    }
                    await InvalidateReadModelAsync(dependency.ReadModelType, entityId, cancellationToken);
                    break;

                case InvalidationStrategy.Batched:
                    // In a real implementation, you'd queue this for batch processing
                    _logger.LogDebug("Queuing read model {ReadModelType} for batch invalidation", dependency.ReadModelType);
                    break;

                case InvalidationStrategy.Conditional:
                    if (ShouldInvalidateConditionally(dependency, changedProperties))
                    {
                        await InvalidateReadModelAsync(dependency.ReadModelType, entityId, cancellationToken);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute invalidation strategy {Strategy} for read model {ReadModelType}", 
                dependency.Strategy, dependency.ReadModelType);
        }
    }

    private async Task RefreshReadModelWithModeAsync(ReadModelRefreshRequest request, CancellationToken cancellationToken)
    {
        switch (request.Mode)
        {
            case RefreshMode.Incremental:
                await RefreshReadModelAsync(request.ReadModelType, request.AggregateId, cancellationToken);
                break;

            case RefreshMode.Full:
                // First invalidate all instances of this read model type
                await InvalidateReadModelAsync(request.ReadModelType, null, cancellationToken);
                await RefreshReadModelAsync(request.ReadModelType, request.AggregateId, cancellationToken);
                break;

            case RefreshMode.Rebuild:
                // Rebuild from scratch - this would typically involve reprocessing events
                await InvalidateReadModelAsync(request.ReadModelType, null, cancellationToken);
                await RefreshReadModelAsync(request.ReadModelType, request.AggregateId, cancellationToken);
                _logger.LogInformation("Read model {ReadModelType} rebuilt from scratch", request.ReadModelType);
                break;
        }
    }

    private bool ShouldInvalidateConditionally(ReadModelDependency dependency, List<string> changedProperties)
    {
        // Simple conditional logic - in a real implementation this could be much more sophisticated
        if (dependency.Properties == null || !dependency.Properties.Any())
        {
            return true; // No specific properties means always invalidate
        }

        return dependency.Properties.Intersect(changedProperties).Any();
    }
}