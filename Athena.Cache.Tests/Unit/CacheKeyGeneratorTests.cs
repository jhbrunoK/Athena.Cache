using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Implementations;
using FluentAssertions;

namespace Athena.Cache.Tests.Unit;

public class CacheKeyGeneratorTests
{
    private readonly DefaultCacheKeyGenerator _keyGenerator;

    public CacheKeyGeneratorTests()
    {
        var options = new AthenaCacheOptions
        {
            Namespace = "TestApp",
            VersionKey = "v1.0"
        };
        _keyGenerator = new DefaultCacheKeyGenerator(options);
    }

    [Fact]
    public void GenerateKey_WithParameters_ShouldCreateConsistentKey()
    {
        // Arrange
        var parameters = new Dictionary<string, object?>
        {
            { "name", "john" },
            { "age", 30 },
            { "active", true }
        };

        // Act
        var key1 = _keyGenerator.GenerateKey("UsersController", "GetUsers", parameters);
        var key2 = _keyGenerator.GenerateKey("UsersController", "GetUsers", parameters);

        // Assert
        key1.Should().Be(key2);
        key1.Should().StartWith("TestApp_v1.0_Users_GetUsers_");
        key1.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateKey_DifferentParameterOrder_ShouldCreateSameKey()
    {
        // Arrange
        var parameters1 = new Dictionary<string, object?>
        {
            { "name", "john" },
            { "age", 30 }
        };

        var parameters2 = new Dictionary<string, object?>
        {
            { "age", 30 },
            { "name", "john" }
        };

        // Act
        var key1 = _keyGenerator.GenerateKey("UsersController", "GetUsers", parameters1);
        var key2 = _keyGenerator.GenerateKey("UsersController", "GetUsers", parameters2);

        // Assert
        key1.Should().Be(key2);
    }

    [Fact]
    public void GenerateKey_WithoutControllerSuffix_ShouldWork()
    {
        // Arrange
        var parameters = new Dictionary<string, object?> { { "id", 1 } };

        // Act
        var key = _keyGenerator.GenerateKey("Users", "GetById", parameters);

        // Assert
        key.Should().StartWith("TestApp_v1.0_Users_GetById_");
    }

    [Fact]
    public void GenerateKey_WithNoParameters_ShouldCreateKeyWithoutHash()
    {
        // Act
        var key = _keyGenerator.GenerateKey("UsersController", "GetAll");

        // Assert
        key.Should().Be("TestApp_v1.0_Users_GetAll");
    }

    [Fact]
    public void GenerateKey_WithNullAndEmptyParameters_ShouldIgnoreThem()
    {
        // Arrange
        var parametersWithNulls = new Dictionary<string, object?>
        {
            { "name", "john" },
            { "age", null },
            { "active", true },
            { "description", "" },
            { "tags", Array.Empty<string>() }
        };

        var parametersWithoutNulls = new Dictionary<string, object?>
        {
            { "name", "john" },
            { "active", true }
        };

        // Act
        var key1 = _keyGenerator.GenerateKey("UsersController", "GetUsers", parametersWithNulls);
        var key2 = _keyGenerator.GenerateKey("UsersController", "GetUsers", parametersWithoutNulls);

        // Assert
        key1.Should().Be(key2);
    }

    [Fact]
    public void GenerateTableTrackingKey_ShouldCreateCorrectFormat()
    {
        // Act
        var key = _keyGenerator.GenerateTableTrackingKey("Users");

        // Assert
        key.Should().Be("TestApp_v1.0_table_Users");
    }

    [Fact]
    public void GenerateParameterHash_WithNullParameters_ShouldReturnEmpty()
    {
        // Act
        var hash1 = _keyGenerator.GenerateParameterHash(null);
        var hash2 = _keyGenerator.GenerateParameterHash(new Dictionary<string, object?>());

        // Assert
        hash1.Should().BeEmpty();
        hash2.Should().BeEmpty();
    }

    [Fact]
    public void GenerateParameterHash_WithSameParameters_ShouldGenerateSameHash()
    {
        // Arrange
        var parameters = new Dictionary<string, object?>
        {
            { "name", "john" },
            { "age", 30 },
            { "active", true }
        };

        // Act
        var hash1 = _keyGenerator.GenerateParameterHash(parameters);
        var hash2 = _keyGenerator.GenerateParameterHash(parameters);

        // Assert
        hash1.Should().Be(hash2);
        hash1.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateParameterHash_WithDifferentValues_ShouldGenerateDifferentHash()
    {
        // Arrange
        var parameters1 = new Dictionary<string, object?> { { "name", "john" } };
        var parameters2 = new Dictionary<string, object?> { { "name", "jane" } };

        // Act
        var hash1 = _keyGenerator.GenerateParameterHash(parameters1);
        var hash2 = _keyGenerator.GenerateParameterHash(parameters2);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Theory]
    [InlineData("  john  ", "john")]
    [InlineData("JOHN", "JOHN")]
    public void GenerateParameterHash_ShouldNormalizeStringValues(string input, string expected)
    {
        // Arrange
        var parameters1 = new Dictionary<string, object?> { { "name", input } };
        var parameters2 = new Dictionary<string, object?> { { "name", expected } };

        // Act
        var hash1 = _keyGenerator.GenerateParameterHash(parameters1);
        var hash2 = _keyGenerator.GenerateParameterHash(parameters2);

        // Assert
        if (input.Trim() == expected)
        {
            hash1.Should().Be(hash2);
        }
        else
        {
            hash1.Should().NotBe(hash2);
        }
    }
}