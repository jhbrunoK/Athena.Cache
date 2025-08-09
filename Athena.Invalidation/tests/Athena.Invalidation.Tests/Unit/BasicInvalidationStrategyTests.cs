using Microsoft.Extensions.Logging;
using Moq;
using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Strategies.Basic;
using Xunit;

namespace Athena.Invalidation.Tests.Unit;

public class BasicInvalidationStrategyTests
{
    private readonly Mock<ILogger<BasicInvalidationStrategy>> _loggerMock;
    private readonly BasicInvalidationStrategy _strategy;

    public BasicInvalidationStrategyTests()
    {
        _loggerMock = new Mock<ILogger<BasicInvalidationStrategy>>();
        _strategy = new BasicInvalidationStrategy(_loggerMock.Object);
    }

    [Fact]
    public void StrategyName_ShouldReturnBasic()
    {
        // Act & Assert
        Assert.Equal("Basic", _strategy.StrategyName);
    }

    [Fact]
    public void Priority_ShouldReturn100()
    {
        // Act & Assert
        Assert.Equal(100, _strategy.Priority);
    }

    [Theory]
    [InlineData(InvalidationType.Table, true)]
    [InlineData(InvalidationType.Pattern, true)]
    [InlineData(InvalidationType.Key, true)]
    [InlineData(InvalidationType.Batch, true)]
    [InlineData(InvalidationType.Hierarchy, false)]
    [InlineData(InvalidationType.Custom, false)]
    public void CanHandle_ShouldReturnCorrectResult(InvalidationType type, bool expected)
    {
        // Arrange
        var contextMock = new Mock<IInvalidationContext>();
        contextMock.Setup(c => c.Type).Returns(type);

        // Act
        var result = _strategy.CanHandle(contextMock.Object);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ExecuteAsync_TableInvalidation_ShouldSucceed()
    {
        // Arrange
        var cacheProviderMock = new Mock<ICacheProvider>();
        var contextMock = new Mock<IInvalidationContext>();
        
        contextMock.Setup(c => c.Type).Returns(InvalidationType.Table);
        contextMock.Setup(c => c.Target).Returns("TestTable");
        contextMock.Setup(c => c.CacheProviders).Returns(new[] { cacheProviderMock.Object });
        contextMock.Setup(c => c.ContextId).Returns("test-context");

        // 추적된 키들 설정
        var trackedKeys = new HashSet<string> { "key1", "key2", "key3" };
        cacheProviderMock
            .Setup(p => p.GetAsync<HashSet<string>>("invalidation:tracking:TestTable", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackedKeys);
        
        cacheProviderMock
            .Setup(p => p.RemoveManyAsync(trackedKeys, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        // Act
        var result = await _strategy.ExecuteAsync(contextMock.Object);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.InvalidatedCount);
        Assert.True(result.ExecutionTime > TimeSpan.Zero);
        
        // 메서드 호출 확인
        cacheProviderMock.Verify(p => p.RemoveManyAsync(trackedKeys, It.IsAny<CancellationToken>()), Times.Once);
        cacheProviderMock.Verify(p => p.RemoveAsync("invalidation:tracking:TestTable", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PatternInvalidation_ShouldSucceed()
    {
        // Arrange
        var cacheProviderMock = new Mock<ICacheProvider>();
        var contextMock = new Mock<IInvalidationContext>();
        
        contextMock.Setup(c => c.Type).Returns(InvalidationType.Pattern);
        contextMock.Setup(c => c.Target).Returns("test:pattern:*");
        contextMock.Setup(c => c.CacheProviders).Returns(new[] { cacheProviderMock.Object });
        contextMock.Setup(c => c.ContextId).Returns("test-context");

        cacheProviderMock
            .Setup(p => p.RemoveByPatternAsync("test:pattern:*", It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        // Act
        var result = await _strategy.ExecuteAsync(contextMock.Object);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.InvalidatedCount);
        Assert.True(result.ExecutionTime > TimeSpan.Zero);
        
        // 메서드 호출 확인
        cacheProviderMock.Verify(p => p.RemoveByPatternAsync("test:pattern:*", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_KeyInvalidation_ShouldSucceed()
    {
        // Arrange
        var cacheProviderMock = new Mock<ICacheProvider>();
        var contextMock = new Mock<IInvalidationContext>();
        
        contextMock.Setup(c => c.Type).Returns(InvalidationType.Key);
        contextMock.Setup(c => c.Target).Returns("specific:key");
        contextMock.Setup(c => c.CacheProviders).Returns(new[] { cacheProviderMock.Object });
        contextMock.Setup(c => c.ContextId).Returns("test-context");

        cacheProviderMock
            .Setup(p => p.RemoveAsync("specific:key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _strategy.ExecuteAsync(contextMock.Object);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.InvalidatedCount);
        
        // 메서드 호출 확인
        cacheProviderMock.Verify(p => p.RemoveAsync("specific:key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BatchInvalidation_ShouldSucceed()
    {
        // Arrange
        var cacheProviderMock = new Mock<ICacheProvider>();
        var contextMock = new Mock<IInvalidationContext>();
        
        var tableNames = new List<string> { "Table1", "Table2" };
        contextMock.Setup(c => c.Type).Returns(InvalidationType.Batch);
        contextMock.Setup(c => c.CacheProviders).Returns(new[] { cacheProviderMock.Object });
        contextMock.Setup(c => c.ContextId).Returns("test-context");
        contextMock.Setup(c => c.GetMetadata<List<string>>("TableNames", It.IsAny<List<string>>()))
                  .Returns(tableNames);
        contextMock.Setup(c => c.Clone()).Returns(contextMock.Object);

        // 각 테이블별 추적 키 설정
        cacheProviderMock
            .Setup(p => p.GetAsync<HashSet<string>>("invalidation:tracking:Table1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "key1" });
        cacheProviderMock
            .Setup(p => p.GetAsync<HashSet<string>>("invalidation:tracking:Table2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "key2", "key3" });
        
        cacheProviderMock
            .Setup(p => p.RemoveManyAsync(It.IsAny<HashSet<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string> keys, CancellationToken ct) => keys.Count);

        // Act
        var result = await _strategy.ExecuteAsync(contextMock.Object);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.InvalidatedCount); // key1 + key2 + key3
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedType_ShouldReturnFailure()
    {
        // Arrange
        var contextMock = new Mock<IInvalidationContext>();
        contextMock.Setup(c => c.Type).Returns(InvalidationType.Custom);
        contextMock.Setup(c => c.ContextId).Returns("test-context");

        // Act
        var result = await _strategy.ExecuteAsync(contextMock.Object);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Unsupported invalidation type", result.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        var serviceProviderMock = new Mock<IServiceProvider>();

        // Act & Assert
        await _strategy.InitializeAsync(serviceProviderMock.Object);
        
        // 예외가 발생하지 않으면 성공
    }

    [Fact]
    public async Task DisposeAsync_ShouldCompleteSuccessfully()
    {
        // Act & Assert
        await _strategy.DisposeAsync();
        
        // 예외가 발생하지 않으면 성공
    }
}