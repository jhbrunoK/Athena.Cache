using Invalidus.Core.Abstractions;
using Invalidus.CQRS.Abstractions;
using Invalidus.CQRS.Extensions;

namespace Invalidus.CQRS.Implementations;

/// <summary>
/// Projection 관리자 구현체
/// Projection manager implementation
/// </summary>
public class ProjectionManager : IProjectionManager
{
    private readonly IInvalidationEngine _invalidationEngine;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<ProjectionManager> _logger;
    private readonly ConcurrentDictionary<string, ProjectionDefinition> _projections = new();
    private readonly ConcurrentDictionary<string, ProjectionStatus> _projectionStatuses = new();

    public ProjectionManager(
        IInvalidationEngine invalidationEngine,
        ILogger<ProjectionManager> logger,
        IEventPublisher? eventPublisher = null)
    {
        _invalidationEngine = invalidationEngine ?? throw new ArgumentNullException(nameof(invalidationEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventPublisher = eventPublisher;
    }

    public async Task InvalidateProjectionAsync(string projectionName, string? partitionKey = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectionName)) 
            throw new ArgumentException("Projection name cannot be null or empty", nameof(projectionName));

        try
        {
            _logger.LogDebug("Invalidating projection {ProjectionName} with partition key {PartitionKey}", 
                projectionName, partitionKey);

            // Use generic method for projection invalidation
            var projectionType = Type.GetType(projectionName);
            if (projectionType != null)
            {
                var method = typeof(IInvalidationEngine).GetMethod("InvalidateProjectionAsync")?.MakeGenericMethod(projectionType);
                var task = (Task?)method?.Invoke(_invalidationEngine, new object?[] { partitionKey, cancellationToken });
                if (task != null)
                {
                    await task;
                }
            }

            // Update projection status
            UpdateProjectionStatus(projectionName, status => status with 
            { 
                LastUpdateTime = DateTime.UtcNow,
                PendingInvalidations = new List<string> { partitionKey ?? "*" }
            });

            _logger.LogInformation("Projection {ProjectionName} invalidated successfully", projectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate projection {ProjectionName}", projectionName);
            
            // Update status to indicate error
            UpdateProjectionStatus(projectionName, status => status with 
            { 
                State = ProjectionState.Faulted,
                ErrorMessage = ex.Message,
                LastUpdateTime = DateTime.UtcNow
            });
            
            throw;
        }
    }

    public async Task UpdateProjectionAsync(string projectionName, IInvalidationEvent eventData, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectionName)) 
            throw new ArgumentException("Projection name cannot be null or empty", nameof(projectionName));
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));

        try
        {
            _logger.LogDebug("Updating projection {ProjectionName} with event {EventType}", 
                projectionName, eventData.EventType);

            // Check if projection is registered and active
            if (!_projections.TryGetValue(projectionName, out var definition))
            {
                _logger.LogWarning("Projection {ProjectionName} not found", projectionName);
                return;
            }

            var status = GetProjectionStatusInternal(projectionName);
            if (status.State != ProjectionState.Running)
            {
                _logger.LogDebug("Projection {ProjectionName} is not running, skipping update", projectionName);
                return;
            }

            // Check if projection handles this event type
            if (!definition.EventTypes.Contains(eventData.EventType))
            {
                _logger.LogDebug("Projection {ProjectionName} does not handle event type {EventType}", 
                    projectionName, eventData.EventType);
                return;
            }

            // Update projection status
            UpdateProjectionStatus(projectionName, s => s with
            {
                LastProcessedEventTimestamp = eventData.Timestamp,
                TotalEventsProcessed = s.TotalEventsProcessed + 1,
                LastUpdateTime = DateTime.UtcNow
            });

            // Auto-invalidate cache if configured
            if (definition.AutoInvalidateCache && definition.CachePatterns != null)
            {
                foreach (var cachePattern in definition.CachePatterns)
                {
                    await _invalidationEngine.InvalidateByPatternAsync(cachePattern, cancellationToken);
                }
            }

            // Publish projection updated event
            if (_eventPublisher != null)
            {
                var projectionEvent = new ProjectionUpdatedEvent
                {
                    ProjectionName = projectionName,
                    PartitionKey = definition.PartitionKey,
                    UpdateType = "Incremental",
                    LastProjectedEventTimestamp = eventData.Timestamp,
                    ProcessedEventCount = 1,
                    CorrelationId = eventData.CorrelationId,
                    CausationId = eventData.EventId
                };

                await _eventPublisher.PublishAsync(projectionEvent, cancellationToken);
            }

            _logger.LogDebug("Projection {ProjectionName} updated successfully", projectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update projection {ProjectionName} with event {EventType}", 
                projectionName, eventData.EventType);
            
            UpdateProjectionStatus(projectionName, status => status with 
            { 
                State = ProjectionState.Faulted,
                ErrorMessage = ex.Message,
                LastUpdateTime = DateTime.UtcNow
            });
        }
    }

    public async Task RebuildProjectionAsync(string projectionName, DateTime? fromTimestamp = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectionName)) 
            throw new ArgumentException("Projection name cannot be null or empty", nameof(projectionName));

        try
        {
            _logger.LogInformation("Rebuilding projection {ProjectionName} from timestamp {FromTimestamp}", 
                projectionName, fromTimestamp);

            // Update status to rebuilding
            UpdateProjectionStatus(projectionName, status => status with 
            { 
                State = ProjectionState.Rebuilding,
                LastUpdateTime = DateTime.UtcNow,
                ErrorMessage = null
            });

            // Clear existing projection cache
            await InvalidateProjectionAsync(projectionName, null, cancellationToken);

            // Reset projection status
            UpdateProjectionStatus(projectionName, status => status with
            {
                LastProcessedEventTimestamp = fromTimestamp,
                LastProcessedEventPosition = 0,
                TotalEventsProcessed = 0,
                State = ProjectionState.Running,
                LastUpdateTime = DateTime.UtcNow
            });

            // Publish rebuild event
            if (_eventPublisher != null)
            {
                var rebuildEvent = new ProjectionUpdatedEvent
                {
                    ProjectionName = projectionName,
                    UpdateType = "Rebuild",
                    LastProjectedEventTimestamp = fromTimestamp,
                    ProcessedEventCount = 0
                };

                await _eventPublisher.PublishAsync(rebuildEvent, cancellationToken);
            }

            _logger.LogInformation("Projection {ProjectionName} rebuild completed", projectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild projection {ProjectionName}", projectionName);
            
            UpdateProjectionStatus(projectionName, status => status with 
            { 
                State = ProjectionState.Faulted,
                ErrorMessage = ex.Message,
                LastUpdateTime = DateTime.UtcNow
            });
            
            throw;
        }
    }

    public async Task UpdateProjectionsBatchAsync(string projectionName, IEnumerable<IInvalidationEvent> events, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectionName)) 
            throw new ArgumentException("Projection name cannot be null or empty", nameof(projectionName));
        if (events == null) throw new ArgumentNullException(nameof(events));

        var eventList = events.ToList();
        _logger.LogInformation("Updating projection {ProjectionName} with batch of {EventCount} events", 
            projectionName, eventList.Count);

        try
        {
            var updateTasks = eventList.Select(eventData => 
                UpdateProjectionAsync(projectionName, eventData, cancellationToken));

            await Task.WhenAll(updateTasks);

            _logger.LogInformation("Projection {ProjectionName} batch update completed", projectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update projection {ProjectionName} with event batch", projectionName);
            throw;
        }
    }

    public Task<ProjectionStatus> GetProjectionStatusAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectionName)) 
            throw new ArgumentException("Projection name cannot be null or empty", nameof(projectionName));

        var status = GetProjectionStatusInternal(projectionName);
        return Task.FromResult(status);
    }

    public Task<IEnumerable<ProjectionStatus>> GetAllProjectionStatusAsync(CancellationToken cancellationToken = default)
    {
        var allStatuses = _projectionStatuses.Values.AsEnumerable();
        return Task.FromResult(allStatuses);
    }

    public Task RegisterProjectionAsync(ProjectionDefinition definition, CancellationToken cancellationToken = default)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));

        _projections[definition.Name] = definition;
        
        // Initialize projection status
        _projectionStatuses[definition.Name] = new ProjectionStatus
        {
            Name = definition.Name,
            State = ProjectionState.Stopped,
            LastUpdateTime = DateTime.UtcNow,
            Statistics = new Dictionary<string, object>
            {
                ["definition"] = definition,
                ["registered_at"] = DateTime.UtcNow
            }
        };

        _logger.LogInformation("Projection {ProjectionName} registered with {EventTypeCount} event types", 
            definition.Name, definition.EventTypes.Count);

        return Task.CompletedTask;
    }

    public Task UnregisterProjectionAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectionName)) 
            throw new ArgumentException("Projection name cannot be null or empty", nameof(projectionName));

        _projections.TryRemove(projectionName, out _);
        _projectionStatuses.TryRemove(projectionName, out _);

        _logger.LogInformation("Projection {ProjectionName} unregistered", projectionName);

        return Task.CompletedTask;
    }

    public Task PauseProjectionAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectionName)) 
            throw new ArgumentException("Projection name cannot be null or empty", nameof(projectionName));

        UpdateProjectionStatus(projectionName, status => status with 
        { 
            State = ProjectionState.Paused,
            LastUpdateTime = DateTime.UtcNow
        });

        _logger.LogInformation("Projection {ProjectionName} paused", projectionName);

        return Task.CompletedTask;
    }

    public Task ResumeProjectionAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectionName)) 
            throw new ArgumentException("Projection name cannot be null or empty", nameof(projectionName));

        UpdateProjectionStatus(projectionName, status => status with 
        { 
            State = ProjectionState.Running,
            LastUpdateTime = DateTime.UtcNow,
            ErrorMessage = null
        });

        _logger.LogInformation("Projection {ProjectionName} resumed", projectionName);

        return Task.CompletedTask;
    }

    private ProjectionStatus GetProjectionStatusInternal(string projectionName)
    {
        return _projectionStatuses.GetValueOrDefault(projectionName, new ProjectionStatus
        {
            Name = projectionName,
            State = ProjectionState.Stopped,
            LastUpdateTime = DateTime.UtcNow
        });
    }

    private void UpdateProjectionStatus(string projectionName, Func<ProjectionStatus, ProjectionStatus> updateFunc)
    {
        _projectionStatuses.AddOrUpdate(projectionName,
            updateFunc(GetProjectionStatusInternal(projectionName)),
            (_, existing) => updateFunc(existing));
    }
}

/// <summary>
/// Projection 백그라운드 서비스
/// Projection background service
/// </summary>
public class ProjectionBackgroundService : BackgroundService
{
    private readonly IProjectionManager _projectionManager;
    private readonly IEventStore? _eventStore;
    private readonly ILogger<ProjectionBackgroundService> _logger;
    private readonly InvalidusCQRSOptions _options;

    public ProjectionBackgroundService(
        IProjectionManager projectionManager,
        ILogger<ProjectionBackgroundService> logger,
        IOptions<InvalidusCQRSOptions> options,
        IEventStore? eventStore = null)
    {
        _projectionManager = projectionManager ?? throw new ArgumentNullException(nameof(projectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventStore = eventStore;
        _options = options.Value ?? new InvalidusCQRSOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Projection background service started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ProcessProjectionMaintenance(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in projection background service");
        }
        finally
        {
            _logger.LogInformation("Projection background service stopped");
        }
    }

    private async Task ProcessProjectionMaintenance(CancellationToken cancellationToken)
    {
        try
        {
            var allProjections = await _projectionManager.GetAllProjectionStatusAsync(cancellationToken);
            
            foreach (var projection in allProjections)
            {
                // Restart faulted projections after some time
                if (projection.State == ProjectionState.Faulted && 
                    projection.LastUpdateTime.HasValue &&
                    DateTime.UtcNow - projection.LastUpdateTime.Value > TimeSpan.FromMinutes(10))
                {
                    _logger.LogInformation("Attempting to restart faulted projection {ProjectionName}", projection.Name);
                    await _projectionManager.ResumeProjectionAsync(projection.Name, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during projection maintenance");
        }
    }
}