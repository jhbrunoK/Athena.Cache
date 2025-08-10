using Athena.Invalidation.Distributed.Abstractions;

namespace Athena.Invalidation.Distributed.Models;

/// <summary>
/// 분산 무효화 이벤트 기본 구현
/// </summary>
public class DistributedInvalidationEvent : IDistributedInvalidationEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string EventType { get; set; } = string.Empty;
    public string SourceNodeId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Target { get; set; } = string.Empty;
    public InvalidationType Type { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 테이블 기반 분산 무효화 이벤트
/// </summary>
public class TableInvalidationEvent : DistributedInvalidationEvent
{
    public string TableName { get; set; } = string.Empty;
    
    public TableInvalidationEvent()
    {
        EventType = nameof(TableInvalidationEvent);
        Type = InvalidationType.Table;
    }
    
    public TableInvalidationEvent(string tableName, string sourceNodeId) : this()
    {
        TableName = tableName;
        Target = tableName;
        SourceNodeId = sourceNodeId;
        Metadata["tableName"] = tableName;
    }
}

/// <summary>
/// 패턴 기반 분산 무효화 이벤트
/// </summary>
public class PatternInvalidationEvent : DistributedInvalidationEvent
{
    public string Pattern { get; set; } = string.Empty;
    
    public PatternInvalidationEvent()
    {
        EventType = nameof(PatternInvalidationEvent);
        Type = InvalidationType.Pattern;
    }
    
    public PatternInvalidationEvent(string pattern, string sourceNodeId) : this()
    {
        Pattern = pattern;
        Target = pattern;
        SourceNodeId = sourceNodeId;
        Metadata["pattern"] = pattern;
    }
}

/// <summary>
/// 키 기반 분산 무효화 이벤트
/// </summary>
public class KeyInvalidationEvent : DistributedInvalidationEvent
{
    public string Key { get; set; } = string.Empty;
    
    public KeyInvalidationEvent()
    {
        EventType = nameof(KeyInvalidationEvent);
        Type = InvalidationType.Key;
    }
    
    public KeyInvalidationEvent(string key, string sourceNodeId) : this()
    {
        Key = key;
        Target = key;
        SourceNodeId = sourceNodeId;
        Metadata["key"] = key;
    }
}

/// <summary>
/// 배치 무효화 이벤트
/// </summary>
public class BatchInvalidationEvent : DistributedInvalidationEvent
{
    public string[] TableNames { get; set; } = [];
    
    public BatchInvalidationEvent()
    {
        EventType = nameof(BatchInvalidationEvent);
        Type = InvalidationType.Batch;
    }
    
    public BatchInvalidationEvent(string[] tableNames, string sourceNodeId) : this()
    {
        TableNames = tableNames;
        Target = string.Join(",", tableNames);
        SourceNodeId = sourceNodeId;
        Metadata["tableNames"] = tableNames;
        Metadata["tableCount"] = tableNames.Length;
    }
}

/// <summary>
/// 계층적 무효화 이벤트
/// </summary>
public class HierarchicalInvalidationEvent : DistributedInvalidationEvent
{
    public string RootTable { get; set; } = string.Empty;
    public string[] RelatedTables { get; set; } = [];
    public int MaxDepth { get; set; } = 3;
    
    public HierarchicalInvalidationEvent()
    {
        EventType = nameof(HierarchicalInvalidationEvent);
        Type = InvalidationType.Hierarchy;
    }
    
    public HierarchicalInvalidationEvent(string rootTable, string[] relatedTables, int maxDepth, string sourceNodeId) : this()
    {
        RootTable = rootTable;
        RelatedTables = relatedTables;
        MaxDepth = maxDepth;
        Target = rootTable;
        SourceNodeId = sourceNodeId;
        Metadata["rootTable"] = rootTable;
        Metadata["relatedTables"] = relatedTables;
        Metadata["maxDepth"] = maxDepth;
    }
}

/// <summary>
/// CQRS 명령 기반 무효화 이벤트
/// </summary>
public class CommandInvalidationEvent : DistributedInvalidationEvent
{
    public string CommandType { get; set; } = string.Empty;
    public string CommandData { get; set; } = string.Empty; // JSON serialized command
    
    public CommandInvalidationEvent()
    {
        EventType = nameof(CommandInvalidationEvent);
        Type = InvalidationType.Custom;
    }
    
    public CommandInvalidationEvent(string commandType, string commandData, string sourceNodeId) : this()
    {
        CommandType = commandType;
        CommandData = commandData;
        Target = commandType;
        SourceNodeId = sourceNodeId;
        Metadata["commandType"] = commandType;
        Metadata["hasData"] = !string.IsNullOrEmpty(commandData);
    }
}

/// <summary>
/// CQRS 도메인 이벤트 기반 무효화 이벤트
/// </summary>
public class DomainEventInvalidationEvent : DistributedInvalidationEvent
{
    public string DomainEventType { get; set; } = string.Empty;
    public string DomainEventData { get; set; } = string.Empty; // JSON serialized domain event
    public string? AggregateId { get; set; }
    
    public DomainEventInvalidationEvent()
    {
        EventType = nameof(DomainEventInvalidationEvent);
        Type = InvalidationType.Custom;
    }
    
    public DomainEventInvalidationEvent(string domainEventType, string domainEventData, string? aggregateId, string sourceNodeId) : this()
    {
        DomainEventType = domainEventType;
        DomainEventData = domainEventData;
        AggregateId = aggregateId;
        Target = domainEventType;
        SourceNodeId = sourceNodeId;
        Metadata["domainEventType"] = domainEventType;
        Metadata["aggregateId"] = aggregateId ?? string.Empty;
        Metadata["hasData"] = !string.IsNullOrEmpty(domainEventData);
    }
}
