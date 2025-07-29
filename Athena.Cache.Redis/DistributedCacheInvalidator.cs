using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Athena.Cache.Redis;

/// <summary>
/// Redis Pub/Sub 기반 분산 캐시 무효화 구현
/// </summary>
public class DistributedCacheInvalidator : IDistributedCacheInvalidator, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ISubscriber _subscriber;
    private readonly ICacheInvalidator _localInvalidator;
    private readonly AthenaCacheOptions _options;
    private readonly ILogger<DistributedCacheInvalidator> _logger;
    
    private readonly string _channelPrefix;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private volatile bool _isListening = false;
    private volatile bool _disposed = false;

    public string InstanceId { get; }
    public bool IsConnected => _redis.IsConnected;
    
    public event EventHandler<InvalidationEventArgs>? InvalidationReceived;

    public DistributedCacheInvalidator(
        IConnectionMultiplexer redis,
        ICacheInvalidator localInvalidator,
        AthenaCacheOptions options,
        ILogger<DistributedCacheInvalidator> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _localInvalidator = localInvalidator ?? throw new ArgumentNullException(nameof(localInvalidator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _database = _redis.GetDatabase();
        _subscriber = _redis.GetSubscriber();
        
        InstanceId = Environment.MachineName + "_" + Environment.ProcessId + "_" + Guid.NewGuid().ToString("N")[..8];
        _channelPrefix = $"{_options.Namespace}:invalidation";
        
        _logger.LogInformation("DistributedCacheInvalidator initialized with InstanceId: {InstanceId}", InstanceId);
    }

    #region IDistributedCacheInvalidator Implementation

    public async Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedCacheInvalidator));
        
        await _connectionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isListening) return;

            var channel = new RedisChannel(_channelPrefix, RedisChannel.PatternMode.Literal);
            await _subscriber.SubscribeAsync(channel, OnInvalidationMessageReceived).ConfigureAwait(false);
            
            _isListening = true;
            _logger.LogInformation("Started listening for distributed invalidation messages on channel: {Channel}", _channelPrefix);
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public async Task StopListeningAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        
        await _connectionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_isListening) return;

            var channel = new RedisChannel(_channelPrefix, RedisChannel.PatternMode.Literal);
            await _subscriber.UnsubscribeAsync(channel).ConfigureAwait(false);
            
            _isListening = false;
            _logger.LogInformation("Stopped listening for distributed invalidation messages");
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public async Task BroadcastInvalidationAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedCacheInvalidator));

        var message = new InvalidationMessage
        {
            Type = InvalidationType.Table,
            TableNames = [tableName],
            CorrelationId = Guid.NewGuid().ToString()
        };

        await BroadcastMessageAsync(message, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Broadcasted table invalidation for: {TableName}", tableName);
    }

    public async Task BroadcastInvalidationByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedCacheInvalidator));

        var message = new InvalidationMessage
        {
            Type = InvalidationType.Pattern,
            TableNames = [],
            Pattern = pattern,
            CorrelationId = Guid.NewGuid().ToString()
        };

        await BroadcastMessageAsync(message, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Broadcasted pattern invalidation for: {Pattern}", pattern);
    }

    public async Task BroadcastBatchInvalidationAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DistributedCacheInvalidator));

        var tables = tableNames.ToArray();
        if (tables.Length == 0) return;

        var message = new InvalidationMessage
        {
            Type = InvalidationType.Batch,
            TableNames = tables,
            CorrelationId = Guid.NewGuid().ToString()
        };

        await BroadcastMessageAsync(message, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Broadcasted batch invalidation for {Count} tables", tables.Length);
    }

    #endregion

    #region ICacheInvalidator Implementation (Delegated to local + broadcast)

    public async Task InvalidateAsync(string tableName, CancellationToken cancellationToken = default)
    {
        // 로컬 무효화
        await _localInvalidator.InvalidateAsync(tableName, cancellationToken).ConfigureAwait(false);
        
        // 분산 브로드캐스트
        await BroadcastInvalidationAsync(tableName, cancellationToken).ConfigureAwait(false);
    }

    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        // 로컬 무효화
        await _localInvalidator.InvalidateByPatternAsync(pattern, cancellationToken).ConfigureAwait(false);
        
        // 분산 브로드캐스트
        await BroadcastInvalidationByPatternAsync(pattern, cancellationToken).ConfigureAwait(false);
    }

    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        // 로컬 무효화
        await _localInvalidator.InvalidateBatchAsync(tableNames, cancellationToken).ConfigureAwait(false);
        
        // 분산 브로드캐스트
        await BroadcastBatchInvalidationAsync(tableNames, cancellationToken).ConfigureAwait(false);
    }

    public async Task InvalidateByPatternBatchAsync(IEnumerable<string> patterns, CancellationToken cancellationToken = default)
    {
        // 로컬 무효화
        await _localInvalidator.InvalidateByPatternBatchAsync(patterns, cancellationToken).ConfigureAwait(false);
        
        // 각 패턴별로 분산 브로드캐스트
        var patternArray = patterns.ToArray();
        var tasks = patternArray.Select(pattern => BroadcastInvalidationByPatternAsync(pattern, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        return _localInvalidator.TrackCacheKeyAsync(tableName, cacheKey, cancellationToken);
    }

    public Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
    {
        return _localInvalidator.TrackCacheKeyAsync(tableNames, cacheKey, cancellationToken);
    }

    public Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        return _localInvalidator.GetTrackedKeysAsync(tableName, cancellationToken);
    }

    public Task InvalidateWithRelatedAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        return _localInvalidator.InvalidateWithRelatedAsync(tableName, relatedTables, maxDepth, cancellationToken);
    }

    #endregion

    #region Private Methods

    private async Task BroadcastMessageAsync(InvalidationMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = new InvalidationEnvelope
            {
                SourceInstanceId = InstanceId,
                Message = message,
                Timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(envelope, JsonSerializerOptions.Web);
            var channel = new RedisChannel(_channelPrefix, RedisChannel.PatternMode.Literal);
            
            await _subscriber.PublishAsync(channel, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast invalidation message: {MessageType}", message.Type);
            throw;
        }
    }

    private async void OnInvalidationMessageReceived(RedisChannel channel, RedisValue message)
    {
        try
        {
            if (!message.HasValue) return;

            var envelope = JsonSerializer.Deserialize<InvalidationEnvelope>(message.ToString(), JsonSerializerOptions.Web);
            if (envelope == null) return;

            // 자신이 보낸 메시지는 무시
            if (envelope.SourceInstanceId == InstanceId) return;

            _logger.LogDebug("Received invalidation message from {SourceInstance}: {MessageType}", 
                envelope.SourceInstanceId, envelope.Message.Type);

            // 로컬 무효화 실행 (브로드캐스트하지 않음)
            await ProcessInvalidationMessageAsync(envelope.Message).ConfigureAwait(false);

            // 이벤트 발생
            InvalidationReceived?.Invoke(this, new InvalidationEventArgs
            {
                SourceInstanceId = envelope.SourceInstanceId,
                Message = envelope.Message,
                Timestamp = envelope.Timestamp
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing invalidation message from channel {Channel}", channel);
        }
    }

    private async Task ProcessInvalidationMessageAsync(InvalidationMessage message)
    {
        switch (message.Type)
        {
            case InvalidationType.Table:
                foreach (var tableName in message.TableNames)
                {
                    await _localInvalidator.InvalidateAsync(tableName).ConfigureAwait(false);
                }
                break;

            case InvalidationType.Pattern:
                if (!string.IsNullOrEmpty(message.Pattern))
                {
                    await _localInvalidator.InvalidateByPatternAsync(message.Pattern).ConfigureAwait(false);
                }
                break;

            case InvalidationType.Batch:
                await _localInvalidator.InvalidateBatchAsync(message.TableNames).ConfigureAwait(false);
                break;
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            StopListeningAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disposal");
        }

        _connectionSemaphore?.Dispose();
        _disposed = true;
    }

    #endregion
}

/// <summary>
/// Redis 메시지 전송용 봉투 클래스
/// </summary>
internal class InvalidationEnvelope
{
    public required string SourceInstanceId { get; init; }
    public required InvalidationMessage Message { get; init; }
    public DateTime Timestamp { get; init; }
}