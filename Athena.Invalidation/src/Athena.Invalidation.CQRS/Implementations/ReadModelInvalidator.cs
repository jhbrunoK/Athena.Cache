using Athena.Invalidation.CQRS.Abstractions;
using System.Collections.Concurrent;

namespace Athena.Invalidation.CQRS.Implementations;

/// <summary>
/// CQRS 읽기 모델 및 프로젝션 캐시 무효화 구현
/// </summary>
public class ReadModelInvalidator : IReadModelInvalidator
{
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly ILogger<ReadModelInvalidator> _logger;
    private readonly ConcurrentDictionary<Type, ReadModelMetadata> _readModelMetadata = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _modelDependencies = new();

    public ReadModelInvalidator(
        IInvalidationEngine invalidationEngine,
        ILogger<ReadModelInvalidator> logger)
    {
        _invalidationEngine = invalidationEngine ?? throw new ArgumentNullException(nameof(invalidationEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvalidateReadModelAsync<TReadModel>(string? modelId = null, CancellationToken cancellationToken = default) 
        where TReadModel : class, IReadModel
    {
        try
        {
            var readModelType = typeof(TReadModel);
            _logger.LogDebug("Invalidating read model {ReadModelType} with ID {ModelId}", 
                readModelType.Name, modelId ?? "ALL");

            var metadata = GetOrCreateMetadata(readModelType);

            // 특정 모델 ID가 지정된 경우 해당 캐시 키들만 무효화
            if (!string.IsNullOrEmpty(modelId))
            {
                await InvalidateSpecificModelAsync(readModelType, modelId, metadata, cancellationToken);
            }
            else
            {
                // 모든 인스턴스 무효화
                await InvalidateAllModelInstancesAsync(readModelType, metadata, cancellationToken);
            }

            // 종속 모델들도 함께 무효화
            await InvalidateDependentModelsAsync<TReadModel>(modelId ?? "*", cancellationToken);

            _logger.LogInformation("Successfully invalidated read model {ReadModelType}", readModelType.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate read model {ReadModelType} with ID {ModelId}", 
                typeof(TReadModel).Name, modelId);
            throw;
        }
    }

    public async Task InvalidateProjectionAsync<TProjection>(string? projectionId = null, CancellationToken cancellationToken = default) 
        where TProjection : class, IProjection
    {
        try
        {
            var projectionType = typeof(TProjection);
            _logger.LogDebug("Invalidating projection {ProjectionType} with ID {ProjectionId}", 
                projectionType.Name, projectionId ?? "ALL");

            // 프로젝션은 읽기 모델의 특수한 형태이므로 동일한 로직 사용
            await InvalidateReadModelAsync<TProjection>(projectionId, cancellationToken);

            // 프로젝션 특화 추가 무효화 (예: 프로젝션 재구성 트리거)
            await InvalidateProjectionSpecificCachesAsync(projectionType, projectionId, cancellationToken);

            _logger.LogInformation("Successfully invalidated projection {ProjectionType}", projectionType.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate projection {ProjectionType} with ID {ProjectionId}", 
                typeof(TProjection).Name, projectionId);
            throw;
        }
    }

    public async Task InvalidateByEventAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) 
        where TEvent : IDomainEvent
    {
        try
        {
            _logger.LogDebug("Invalidating read models based on event {EventType}:{EventId}", 
                domainEvent.EventType, domainEvent.EventId);

            var invalidatedModels = new HashSet<Type>();

            // 등록된 모든 읽기 모델 메타데이터를 확인
            foreach (var kvp in _readModelMetadata)
            {
                var readModelType = kvp.Key;
                var metadata = kvp.Value;

                if (ShouldInvalidateForEvent(metadata, domainEvent))
                {
                    await InvalidateReadModelByEventAsync(readModelType, domainEvent, metadata, cancellationToken);
                    invalidatedModels.Add(readModelType);
                }
            }

            _logger.LogInformation("Invalidated {Count} read model types for event {EventType}:{EventId}",
                invalidatedModels.Count, domainEvent.EventType, domainEvent.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate read models for event {EventType}:{EventId}",
                domainEvent.EventType, domainEvent.EventId);
            throw;
        }
    }

    public async Task InvalidateDependentModelsAsync<TReadModel>(string modelId, CancellationToken cancellationToken = default) 
        where TReadModel : class, IReadModel
    {
        var readModelType = typeof(TReadModel);
        var typeName = readModelType.Name;

        if (!_modelDependencies.TryGetValue(typeName, out var dependentTypes))
        {
            return; // 종속 모델이 없음
        }

        try
        {
            _logger.LogDebug("Invalidating {Count} dependent models for {ReadModelType}:{ModelId}",
                dependentTypes.Count, typeName, modelId);

            var invalidationTasks = dependentTypes.Select(async dependentTypeName =>
            {
                try
                {
                    // 종속 모델의 캐시 패턴 무효화
                    var dependentPattern = $"{dependentTypeName.ToLower()}:*";
                    await _invalidationEngine.InvalidateByPatternAsync(dependentPattern, cancellationToken);
                    
                    _logger.LogDebug("Invalidated dependent model pattern {Pattern}", dependentPattern);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to invalidate dependent model {DependentType}", dependentTypeName);
                }
            });

            await Task.WhenAll(invalidationTasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate dependent models for {ReadModelType}:{ModelId}",
                typeName, modelId);
            throw;
        }
    }

    public async Task TrackReadModelAsync<TReadModel>(TReadModel readModel, CancellationToken cancellationToken = default) 
        where TReadModel : class, IReadModel
    {
        try
        {
            var readModelType = typeof(TReadModel);
            var metadata = GetOrCreateMetadata(readModelType);

            // 읽기 모델의 캐시 키들을 관련 테이블과 연결하여 추적
            var cacheKeys = readModel.GetCacheKeys().ToList();
            var relatedTables = readModel.GetRelatedTables().ToList();

            if (cacheKeys.Any() && relatedTables.Any())
            {
                foreach (var cacheKey in cacheKeys)
                {
                    await _invalidationEngine.TrackCacheKeyAsync(relatedTables.ToArray(), cacheKey, cancellationToken);
                }

                _logger.LogDebug("Tracked {KeyCount} cache keys for {TableCount} tables for read model {ReadModelType}:{ModelId}",
                    cacheKeys.Count, relatedTables.Count, readModelType.Name, readModel.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track read model {ReadModelType}:{ModelId}",
                typeof(TReadModel).Name, readModel.Id);
            throw;
        }
    }

    /// <summary>
    /// 읽기 모델 간의 의존성 관계 등록
    /// </summary>
    public void RegisterModelDependency<TReadModel, TDependentModel>()
        where TReadModel : class, IReadModel
        where TDependentModel : class, IReadModel
    {
        var sourceName = typeof(TReadModel).Name;
        var dependentName = typeof(TDependentModel).Name;

        _modelDependencies.AddOrUpdate(sourceName,
            new HashSet<string> { dependentName },
            (key, existing) =>
            {
                existing.Add(dependentName);
                return existing;
            });

        _logger.LogInformation("Registered dependency: {SourceModel} -> {DependentModel}",
            sourceName, dependentName);
    }

    private async Task InvalidateSpecificModelAsync(Type readModelType, string modelId, ReadModelMetadata metadata, CancellationToken cancellationToken)
    {
        // 특정 모델 인스턴스의 캐시 키 패턴
        var modelPattern = $"{readModelType.Name.ToLower()}:{modelId}:*";
        await _invalidationEngine.InvalidateByPatternAsync(modelPattern, cancellationToken);

        // 관련 테이블들도 무효화 (해당 모델과 관련된 캐시만)
        if (metadata.RelatedTables.Any())
        {
            foreach (var table in metadata.RelatedTables)
            {
                var tablePattern = $"{table.ToLower()}:{modelId}:*";
                await _invalidationEngine.InvalidateByPatternAsync(tablePattern, cancellationToken);
            }
        }
    }

    private async Task InvalidateAllModelInstancesAsync(Type readModelType, ReadModelMetadata metadata, CancellationToken cancellationToken)
    {
        // 해당 읽기 모델 타입의 모든 캐시
        var modelPattern = $"{readModelType.Name.ToLower()}:*";
        await _invalidationEngine.InvalidateByPatternAsync(modelPattern, cancellationToken);

        // 관련 테이블 전체 무효화
        if (metadata.RelatedTables.Any())
        {
            await _invalidationEngine.InvalidateBatchAsync(metadata.RelatedTables, cancellationToken);
        }
    }

    private async Task InvalidateProjectionSpecificCachesAsync(Type projectionType, string? projectionId, CancellationToken cancellationToken)
    {
        // 프로젝션 상태 캐시 무효화
        var projectionStatePattern = $"projection:{projectionType.Name.ToLower()}:state:*";
        await _invalidationEngine.InvalidateByPatternAsync(projectionStatePattern, cancellationToken);

        // 프로젝션 인덱스 캐시 무효화
        var projectionIndexPattern = $"projection:{projectionType.Name.ToLower()}:index:*";
        await _invalidationEngine.InvalidateByPatternAsync(projectionIndexPattern, cancellationToken);
    }

    private async Task InvalidateReadModelByEventAsync(Type readModelType, IDomainEvent domainEvent, ReadModelMetadata metadata, CancellationToken cancellationToken)
    {
        // 집계 루트 ID가 있는 경우 특정 인스턴스만 무효화
        if (!string.IsNullOrEmpty(domainEvent.AggregateId))
        {
            await InvalidateSpecificModelAsync(readModelType, domainEvent.AggregateId, metadata, cancellationToken);
        }
        else
        {
            // 전체 무효화
            await InvalidateAllModelInstancesAsync(readModelType, metadata, cancellationToken);
        }
    }

    private bool ShouldInvalidateForEvent(ReadModelMetadata metadata, IDomainEvent domainEvent)
    {
        // 이벤트 타입이 읽기 모델이 구독하는 이벤트인지 확인
        if (metadata.SourceEventTypes.Contains(domainEvent.EventType))
        {
            return true;
        }

        // 관련 테이블이 있는 경우 이벤트에서 추론된 테이블과 비교
        var eventTable = InferTableFromEventType(domainEvent.EventType);
        if (!string.IsNullOrEmpty(eventTable) && metadata.RelatedTables.Contains(eventTable))
        {
            return true;
        }

        return false;
    }

    private string InferTableFromEventType(string eventType)
    {
        if (eventType.EndsWith("Event"))
        {
            var eventName = eventType.Replace("Event", "");
            var prefixesToRemove = new[] { "Created", "Updated", "Deleted", "Changed", "Modified" };
            foreach (var prefix in prefixesToRemove)
            {
                if (eventName.EndsWith(prefix))
                {
                    eventName = eventName.Substring(0, eventName.Length - prefix.Length);
                    break;
                }
            }
            return string.IsNullOrEmpty(eventName) ? string.Empty : 
                   (eventName.EndsWith("s") ? eventName : eventName + "s");
        }
        return string.Empty;
    }

    private ReadModelMetadata GetOrCreateMetadata(Type readModelType)
    {
        return _readModelMetadata.GetOrAdd(readModelType, type =>
        {
            var metadata = new ReadModelMetadata
            {
                ReadModelType = type,
                RelatedTables = new HashSet<string>(),
                SourceEventTypes = new HashSet<string>()
            };

            // 타입에서 기본 관련 테이블 추론
            var tableName = type.Name.EndsWith("ReadModel") ? 
                type.Name.Replace("ReadModel", "s") : 
                type.Name + "s";
            metadata.RelatedTables.Add(tableName);

            _logger.LogDebug("Created metadata for read model {ReadModelType} with inferred table {TableName}",
                type.Name, tableName);

            return metadata;
        });
    }
}

/// <summary>
/// 읽기 모델 메타데이터
/// </summary>
internal class ReadModelMetadata
{
    public Type ReadModelType { get; set; } = null!;
    public HashSet<string> RelatedTables { get; set; } = new();
    public HashSet<string> SourceEventTypes { get; set; } = new();
}