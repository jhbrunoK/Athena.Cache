namespace Athena.Invalidation.Engine.Core;

/// <summary>
/// 무효화 실행 컨텍스트 구현
/// </summary>
public class InvalidationContext : IInvalidationContext
{
    public string ContextId { get; init; } = string.Empty;
    public InvalidationTrigger Trigger { get; init; } = null!;
    public string Target { get; set; } = string.Empty;
    public InvalidationType Type { get; set; }
    public DateTimeOffset Timestamp { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
    public IEnumerable<ICacheProvider> CacheProviders { get; init; } = Enumerable.Empty<ICacheProvider>();
    public int Priority { get; set; }
    public TimeSpan? Timeout { get; set; }
    public int MaxRetries { get; set; }

    public void AddMetadata(string key, object value)
    {
        Metadata[key] = value;
    }

    public T? GetMetadata<T>(string key, T? defaultValue = default)
    {
        if (Metadata.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }

    public IInvalidationContext Clone()
    {
        return new InvalidationContext
        {
            ContextId = Guid.NewGuid().ToString("N")[..8],
            Trigger = Trigger,
            Target = Target,
            Type = Type,
            Timestamp = Timestamp,
            Metadata = new Dictionary<string, object>(Metadata),
            CacheProviders = CacheProviders,
            Priority = Priority,
            Timeout = Timeout,
            MaxRetries = MaxRetries
        };
    }
}