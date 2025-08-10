using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.CQRS.Abstractions;
using Athena.Invalidation.CQRS.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Athena.Invalidation.Tests.Unit.CQRS;

public class EventDrivenInvalidationTests
{
    private readonly Mock<IInvalidationEngine> _mockEngine;
    private readonly Mock<ILogger<EventDrivenInvalidation>> _mockLogger;
    private readonly EventDrivenInvalidation _handler;

    public EventDrivenInvalidationTests()
    {
        _mockEngine = new Mock<IInvalidationEngine>();
        _mockLogger = new Mock<ILogger<EventDrivenInvalidation>>();
        _handler = new EventDrivenInvalidation(_mockEngine.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task HandleEventAsync_UserCreatedEvent_InvalidatesUserTables()
    {
        // Arrange
        var domainEvent = new TestDomainEvent
        {
            EventId = Guid.NewGuid().ToString("N")[..12],
            EventType = "UserCreatedEvent",
            AggregateId = "user-123",
            OccurredAt = DateTimeOffset.UtcNow
        };

        _mockEngine.Setup(x => x.InvalidateBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockEngine.Setup(x => x.InvalidateByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleEventAsync(domainEvent);

        // Assert
        _mockEngine.Verify(x => x.InvalidateBatchAsync(
            It.Is<IEnumerable<string>>(tables => tables.Contains("Users")), 
            It.IsAny<CancellationToken>()), Times.Once);
        _mockEngine.Verify(x => x.InvalidateByPatternAsync("users:user-123:*", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleEventAsync_EventWithCustomInvalidationTables_UsesCustomTables()
    {
        // Arrange
        var domainEvent = new TestDomainEvent
        {
            EventId = Guid.NewGuid().ToString("N")[..12],
            EventType = "OrderUpdatedEvent",
            AggregateId = "order-456"
        };
        domainEvent.Metadata["InvalidationTables"] = new[] { "Orders", "OrderItems", "Inventory" };

        _mockEngine.Setup(x => x.InvalidateBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleEventAsync(domainEvent);

        // Assert
        _mockEngine.Verify(x => x.InvalidateBatchAsync(
            It.Is<IEnumerable<string>>(tables => 
                tables.Contains("Orders") && 
                tables.Contains("OrderItems") && 
                tables.Contains("Inventory")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterEventHandler_ValidHandler_RegistersSuccessfully()
    {
        // Arrange
        var mockHandler = new Mock<IEventInvalidationHandler<TestDomainEvent>>();
        mockHandler.Setup(x => x.Priority).Returns(1);

        // Act
        _handler.RegisterEventHandler(mockHandler.Object);

        // Assert
        var registeredTypes = _handler.GetRegisteredEventTypes();
        Assert.Contains(typeof(TestDomainEvent), registeredTypes);
    }

    [Fact]
    public async Task HandleEventAsync_WithRegisteredHandler_UsesSpecificHandler()
    {
        // Arrange
        var mockHandler = new Mock<IEventInvalidationHandler<TestDomainEvent>>();
        mockHandler.Setup(x => x.Priority).Returns(1);
        mockHandler.Setup(x => x.GetInvalidationTablesAsync(It.IsAny<TestDomainEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "CustomTable1", "CustomTable2" });
        mockHandler.Setup(x => x.GetInvalidationPatternsAsync(It.IsAny<TestDomainEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "custom:*" });
        // mockHandler.Setup(x => x.GetHierarchicalTargetsAsync(It.IsAny<TestDomainEvent>(), It.IsAny<CancellationToken>()))
        //     .ReturnsAsync(Array.Empty<HierarchicalInvalidationTarget>());

        _handler.RegisterEventHandler(mockHandler.Object);

        var domainEvent = new TestDomainEvent
        {
            EventId = Guid.NewGuid().ToString("N")[..12],
            EventType = "TestEvent"
        };

        _mockEngine.Setup(x => x.InvalidateByTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockEngine.Setup(x => x.InvalidateByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleEventAsync(domainEvent);

        // Assert
        mockHandler.Verify(x => x.GetInvalidationTablesAsync(domainEvent, It.IsAny<CancellationToken>()), Times.Once);
        mockHandler.Verify(x => x.GetInvalidationPatternsAsync(domainEvent, It.IsAny<CancellationToken>()), Times.Once);
        _mockEngine.Verify(x => x.InvalidateByTableAsync("CustomTable1", It.IsAny<CancellationToken>()), Times.Once);
        _mockEngine.Verify(x => x.InvalidateByTableAsync("CustomTable2", It.IsAny<CancellationToken>()), Times.Once);
        _mockEngine.Verify(x => x.InvalidateByPatternAsync("custom:*", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CanHandle_EventWithMetadata_ReturnsTrue()
    {
        // Arrange
        var domainEvent = new TestDomainEvent
        {
            EventId = Guid.NewGuid().ToString("N")[..12],
            AggregateId = "test-123"
        };

        // Act
        var result = _handler.CanHandle(domainEvent);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanHandle_NullEvent_ReturnsFalse()
    {
        // Act
        var result = _handler.CanHandle<TestDomainEvent>(null!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void UnregisterEventHandler_RegisteredHandler_RemovesSuccessfully()
    {
        // Arrange
        var mockHandler = new Mock<IEventInvalidationHandler<TestDomainEvent>>();
        mockHandler.Setup(x => x.Priority).Returns(1);
        
        _handler.RegisterEventHandler(mockHandler.Object);
        Assert.Contains(typeof(TestDomainEvent), _handler.GetRegisteredEventTypes());

        // Act
        _handler.UnregisterEventHandler<TestDomainEvent>();

        // Assert
        Assert.DoesNotContain(typeof(TestDomainEvent), _handler.GetRegisteredEventTypes());
    }
}

public class TestDomainEvent : IDomainEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string EventType { get; set; } = nameof(TestDomainEvent);
    public int Version { get; set; } = 1;
    public string? AggregateId { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

// Removed HierarchicalInvalidationTarget as it's not needed for this test