namespace Athena.Invalidation.Core.Abstractions;

/// <summary>
/// 무효화 실행 컨텍스트 기본 구현
/// 모든 InvalidationContext 구현체의 공통 로직을 제공
/// </summary>
public abstract class BaseInvalidationContext : IInvalidationContext
{
    public string ContextId { get; } = Guid.NewGuid().ToString("N")[..12];
    public InvalidationTrigger Trigger { get; protected set; }
    public string Target { get; set; } = string.Empty;
    public InvalidationType Type { get; set; } = InvalidationType.Key;
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
    public Dictionary<string, object> Metadata { get; } = new();
    public IEnumerable<ICacheProvider> CacheProviders { get; set; } = Array.Empty<ICacheProvider>();
    public int Priority { get; set; } = 0;
    public TimeSpan? Timeout { get; set; }
    public int MaxRetries { get; set; } = 3;

    protected BaseInvalidationContext(InvalidationTrigger trigger)
    {
        Trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
    }

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

    public abstract IInvalidationContext Clone();

    protected virtual void CopyPropertiesTo(BaseInvalidationContext target)
    {
        target.Target = Target;
        target.Type = Type;
        target.CacheProviders = CacheProviders;
        target.Priority = Priority;
        target.Timeout = Timeout;
        target.MaxRetries = MaxRetries;

        foreach (var kvp in Metadata)
        {
            target.Metadata[kvp.Key] = kvp.Value;
        }
    }
}