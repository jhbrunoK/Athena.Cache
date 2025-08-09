using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.CQRS.Abstractions;
using Athena.Invalidation.CQRS.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Athena.Invalidation.Tests.Unit.CQRS;

public class CommandInvalidationHandlerTests
{
    private readonly Mock<IInvalidationEngine> _mockEngine;
    private readonly Mock<ILogger<CommandInvalidationHandler>> _mockLogger;
    private readonly CommandInvalidationHandler _handler;

    public CommandInvalidationHandlerTests()
    {
        _mockEngine = new Mock<IInvalidationEngine>();
        _mockLogger = new Mock<ILogger<CommandInvalidationHandler>>();
        _handler = new CommandInvalidationHandler(_mockEngine.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task HandleCommandAsync_ValidCommand_InvalidatesCorrectTables()
    {
        // Arrange
        var command = new TestCommand
        {
            Id = Guid.NewGuid(),
            UserId = "test-user-123"
        };

        _mockEngine.Setup(x => x.InvalidateByTableAsync("Users", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleCommandAsync(command);

        // Assert
        _mockEngine.Verify(x => x.InvalidateByTableAsync("Users", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCommandAsync_CommandWithMetadata_UsesCustomInvalidationTables()
    {
        // Arrange
        var command = new TestCommand
        {
            Id = Guid.NewGuid(),
            UserId = "test-user-123"
        };
        command.Metadata["InvalidationTables"] = new[] { "CustomTable1", "CustomTable2" };

        _mockEngine.Setup(x => x.InvalidateBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleCommandAsync(command);

        // Assert
        _mockEngine.Verify(x => x.InvalidateBatchAsync(
            It.Is<IEnumerable<string>>(tables => tables.Contains("CustomTable1") && tables.Contains("CustomTable2")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCommandAsync_CommandWithPatterns_InvalidatesByPattern()
    {
        // Arrange
        var command = new TestCommand
        {
            Id = Guid.NewGuid(),
            UserId = "test-user-123"
        };
        command.Metadata["InvalidationPatterns"] = new[] { "user:*", "profile:test-user-123:*" };

        _mockEngine.Setup(x => x.InvalidateByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleCommandAsync(command);

        // Assert
        _mockEngine.Verify(x => x.InvalidateByPatternAsync("user:*", It.IsAny<CancellationToken>()), Times.Once);
        _mockEngine.Verify(x => x.InvalidateByPatternAsync("profile:test-user-123:*", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleCommandAsync_NullCommand_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _handler.HandleCommandAsync<TestCommand>(null!));
    }

    [Fact]
    public async Task CanHandle_ValidCommand_ReturnsTrue()
    {
        // Arrange
        var command = new TestCommand { Id = Guid.NewGuid() };

        // Act
        var result = _handler.CanHandle(command);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanHandle_NullCommand_ReturnsFalse()
    {
        // Act
        var result = _handler.CanHandle<TestCommand>(null!);

        // Assert
        Assert.False(result);
    }
}

public class TestCommand : ICommand
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ExecutedBy { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string? UserId { get; set; }
}