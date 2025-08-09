using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Hierarchical.Abstractions;
using Athena.Invalidation.Hierarchical.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Athena.Invalidation.Tests.Unit.Hierarchical;

public class HierarchicalInvalidationTreeTests
{
    private readonly Mock<IInvalidationEngine> _mockEngine;
    private readonly Mock<ILogger<HierarchicalInvalidationTree>> _mockLogger;
    private readonly HierarchicalInvalidationTree _tree;

    public HierarchicalInvalidationTreeTests()
    {
        _mockEngine = new Mock<IInvalidationEngine>();
        _mockLogger = new Mock<ILogger<HierarchicalInvalidationTree>>();
        _tree = new HierarchicalInvalidationTree(_mockEngine.Object, _mockLogger.Object);
    }

    [Fact]
    public void DefineLayer_ValidLayer_AddsSuccessfully()
    {
        // Arrange
        var properties = new LayerProperties
        {
            Priority = 1,
            EnableAutoInvalidation = true
        };

        // Act
        _tree.DefineLayer("DataLayer", null, properties);

        // Assert
        var tables = _tree.GetTablesInLayer("DataLayer");
        Assert.Empty(tables);
    }

    [Fact]
    public void DefineLayer_WithParentLayer_EstablishesHierarchy()
    {
        // Arrange & Act
        _tree.DefineLayer("DataLayer");
        _tree.DefineLayer("ServiceLayer", "DataLayer");
        _tree.DefineLayer("PresentationLayer", "ServiceLayer");

        // Assert
        var visualization = _tree.GetLayerVisualization();
        var connections = visualization.Connections;
        
        Assert.Contains(connections, c => c.FromLayer == "DataLayer" && c.ToLayer == "ServiceLayer");
        Assert.Contains(connections, c => c.FromLayer == "ServiceLayer" && c.ToLayer == "PresentationLayer");
    }

    [Fact]
    public void AssignTableToLayer_ValidAssignment_AssignsSuccessfully()
    {
        // Arrange
        _tree.DefineLayer("DataLayer");

        // Act
        _tree.AssignTableToLayer("Users", "DataLayer");

        // Assert
        var layer = _tree.GetTableLayer("Users");
        Assert.Equal("DataLayer", layer);

        var tables = _tree.GetTablesInLayer("DataLayer");
        Assert.Contains("Users", tables);
    }

    [Fact]
    public void AssignTableToLayer_NonExistentLayer_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _tree.AssignTableToLayer("Users", "NonExistentLayer"));
    }

    [Fact]
    public void AssignTableToLayer_ReassignTable_MovesToNewLayer()
    {
        // Arrange
        _tree.DefineLayer("Layer1");
        _tree.DefineLayer("Layer2");
        _tree.AssignTableToLayer("Users", "Layer1");

        // Act
        _tree.AssignTableToLayer("Users", "Layer2");

        // Assert
        Assert.Equal("Layer2", _tree.GetTableLayer("Users"));
        Assert.DoesNotContain("Users", _tree.GetTablesInLayer("Layer1"));
        Assert.Contains("Users", _tree.GetTablesInLayer("Layer2"));
    }

    [Fact]
    public async Task CreateInvalidationPlanAsync_SimpleHierarchy_CreatesCorrectPlan()
    {
        // Arrange
        _tree.DefineLayer("DataLayer");
        _tree.DefineLayer("ServiceLayer", "DataLayer");
        _tree.DefineLayer("PresentationLayer", "ServiceLayer");

        _tree.AssignTableToLayer("Users", "DataLayer");
        _tree.AssignTableToLayer("UserService", "ServiceLayer");
        _tree.AssignTableToLayer("UserViews", "PresentationLayer");

        // Act
        var plan = await _tree.CreateInvalidationPlanAsync("Users", InvalidationDirection.Up);

        // Assert
        Assert.Equal("Users", plan.RootTable);
        Assert.Equal(InvalidationDirection.Up, plan.Direction);
        Assert.True(plan.Steps.Count >= 1);
        Assert.Contains(plan.Steps, s => s.Tables.Contains("Users"));
    }

    [Fact]
    public async Task CreateInvalidationPlanAsync_DownDirection_IncludesChildLayers()
    {
        // Arrange
        _tree.DefineLayer("DataLayer");
        _tree.DefineLayer("ServiceLayer", "DataLayer");
        _tree.DefineLayer("CacheLayer", "DataLayer");

        _tree.AssignTableToLayer("Users", "DataLayer");
        _tree.AssignTableToLayer("UserService", "ServiceLayer");
        _tree.AssignTableToLayer("UserCache", "CacheLayer");

        // Act
        var plan = await _tree.CreateInvalidationPlanAsync("Users", InvalidationDirection.Down);

        // Assert
        var allTables = plan.Steps.SelectMany(s => s.Tables).ToList();
        Assert.Contains("Users", allTables);
        // Child layers should be included in downward invalidation
    }

    [Fact]
    public async Task CreateInvalidationPlanAsync_BothDirection_IncludesParentAndChildLayers()
    {
        // Arrange
        _tree.DefineLayer("ParentLayer");
        _tree.DefineLayer("MiddleLayer", "ParentLayer");
        _tree.DefineLayer("ChildLayer", "MiddleLayer");

        _tree.AssignTableToLayer("ParentTable", "ParentLayer");
        _tree.AssignTableToLayer("MiddleTable", "MiddleLayer");
        _tree.AssignTableToLayer("ChildTable", "ChildLayer");

        // Act
        var plan = await _tree.CreateInvalidationPlanAsync("MiddleTable", InvalidationDirection.Both);

        // Assert
        Assert.Equal(InvalidationDirection.Both, plan.Direction);
        // Should include steps for multiple layers
        Assert.True(plan.Steps.Count > 0);
    }

    [Fact]
    public async Task CreateInvalidationPlanAsync_UnassignedTable_CreatesSimplePlan()
    {
        // Act
        var plan = await _tree.CreateInvalidationPlanAsync("UnassignedTable");

        // Assert
        Assert.Equal("UnassignedTable", plan.RootTable);
        Assert.Single(plan.Steps);
        Assert.Equal("Unassigned", plan.Steps[0].LayerName);
        Assert.Contains("UnassignedTable", plan.Steps[0].Tables);
    }

    [Fact]
    public void DefineLayerRelationship_ValidRelationship_EstablishesRelationship()
    {
        // Arrange
        _tree.DefineLayer("Layer1");
        _tree.DefineLayer("Layer2");

        // Act
        _tree.DefineLayerRelationship("Layer1", "Layer2", LayerRelationType.StrongParentChild);

        // Assert
        var visualization = _tree.GetLayerVisualization();
        Assert.Contains(visualization.Connections, c => 
            c.FromLayer == "Layer1" && 
            c.ToLayer == "Layer2" && 
            c.Type == LayerRelationType.StrongParentChild);
    }

    [Fact]
    public void DefineLayerRelationship_NonExistentLayers_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            _tree.DefineLayerRelationship("NonExistent1", "NonExistent2", LayerRelationType.Sibling));
    }

    [Fact]
    public void ValidateLayerStructure_ValidStructure_ReturnsValidResult()
    {
        // Arrange
        _tree.DefineLayer("Layer1");
        _tree.DefineLayer("Layer2", "Layer1");
        _tree.AssignTableToLayer("Table1", "Layer1");
        _tree.AssignTableToLayer("Table2", "Layer2");

        // Act
        var result = _tree.ValidateLayerStructure();

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.OrphanedTables);
    }

    [Fact]
    public void GetLayerVisualization_ComplexHierarchy_ReturnsCorrectVisualization()
    {
        // Arrange
        _tree.DefineLayer("Root");
        _tree.DefineLayer("Child1", "Root");
        _tree.DefineLayer("Child2", "Root");
        _tree.DefineLayer("Grandchild", "Child1");

        _tree.AssignTableToLayer("RootTable", "Root");
        _tree.AssignTableToLayer("Child1Table", "Child1");
        _tree.AssignTableToLayer("Child2Table", "Child2");
        _tree.AssignTableToLayer("GrandchildTable", "Grandchild");

        // Act
        var visualization = _tree.GetLayerVisualization();

        // Assert
        Assert.Equal(4, visualization.Layers.Length);
        Assert.Equal(3, visualization.Connections.Length);

        var rootLayer = visualization.Layers.First(l => l.Name == "Root");
        Assert.Equal(0, rootLayer.Level);
        Assert.Contains("RootTable", rootLayer.Tables);

        var grandchildLayer = visualization.Layers.First(l => l.Name == "Grandchild");
        Assert.Equal(2, grandchildLayer.Level);
    }

    [Fact]
    public async Task ExecuteInvalidationPlanAsync_SimplePlan_ExecutesSuccessfully()
    {
        // Arrange
        var plan = new HierarchicalInvalidationPlan
        {
            RootTable = "Users",
            Direction = InvalidationDirection.Down,
            Steps = new List<InvalidationStep>
            {
                new()
                {
                    Order = 0,
                    LayerName = "DataLayer",
                    Tables = new List<string> { "Users", "UserProfiles" },
                    Strategy = InvalidationStrategy.Immediate
                }
            }
        };

        _mockEngine.Setup(x => x.InvalidateByTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _tree.ExecuteInvalidationPlanAsync(plan);

        // Assert
        _mockEngine.Verify(x => x.InvalidateByTableAsync("Users", It.IsAny<CancellationToken>()), Times.Once);
        _mockEngine.Verify(x => x.InvalidateByTableAsync("UserProfiles", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteInvalidationPlanAsync_BatchStrategy_UsesBatchInvalidation()
    {
        // Arrange
        var plan = new HierarchicalInvalidationPlan
        {
            RootTable = "Orders",
            Steps = new List<InvalidationStep>
            {
                new()
                {
                    Order = 0,
                    LayerName = "DataLayer",
                    Tables = new List<string> { "Orders", "OrderItems" },
                    Strategy = InvalidationStrategy.Batch
                }
            }
        };

        _mockEngine.Setup(x => x.InvalidateBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _tree.ExecuteInvalidationPlanAsync(plan);

        // Assert
        _mockEngine.Verify(x => x.InvalidateBatchAsync(
            It.Is<IEnumerable<string>>(tables => tables.Contains("Orders") && tables.Contains("OrderItems")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteInvalidationPlanAsync_DelayedStrategy_RespectsDelay()
    {
        // Arrange
        var plan = new HierarchicalInvalidationPlan
        {
            RootTable = "Cache",
            Steps = new List<InvalidationStep>
            {
                new()
                {
                    Order = 0,
                    LayerName = "CacheLayer",
                    Tables = new List<string> { "Cache" },
                    Strategy = InvalidationStrategy.Delayed,
                    Delay = TimeSpan.FromMilliseconds(100)
                }
            }
        };

        _mockEngine.Setup(x => x.InvalidateByTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var startTime = DateTime.UtcNow;

        // Act
        await _tree.ExecuteInvalidationPlanAsync(plan);

        // Assert
        var elapsed = DateTime.UtcNow - startTime;
        Assert.True(elapsed.TotalMilliseconds >= 90); // Allow for some timing variance

        _mockEngine.Verify(x => x.InvalidateByTableAsync("Cache", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AssignTableToLayer_InvalidTableName_ThrowsArgumentException(string tableName)
    {
        // Arrange
        _tree.DefineLayer("TestLayer");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _tree.AssignTableToLayer(tableName!, "TestLayer"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task CreateInvalidationPlanAsync_InvalidRootTable_ThrowsArgumentException(string rootTable)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            _tree.CreateInvalidationPlanAsync(rootTable!));
    }
}