namespace Athena.Cache.Core.Enums;

/// <summary>
/// 무효화 결과
/// </summary>
public class InvalidationResult
{
    public bool Success { get; set; }
    public int InvalidatedCount { get; set; }
    public string[] InvalidatedKeys { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
}