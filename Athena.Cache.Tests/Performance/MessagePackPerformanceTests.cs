using Athena.Cache.Core.Configuration;
using Athena.Cache.Core.Implementations;
using Athena.Cache.Core.Middleware;
using FluentAssertions;
using MessagePack;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using System.Text.Json;

namespace Athena.Cache.Tests.Performance;

/// <summary>
/// MessagePack vs JSON 성능 비교 테스트
/// </summary>
public class MessagePackPerformanceTests
{
    [MessagePackObject]
    public class TestData
    {
        [Key(0)]
        public int Id { get; set; }
        
        [Key(1)]
        public string Name { get; set; } = string.Empty;
        
        [Key(2)]
        public DateTime CreatedAt { get; set; }
        
        [Key(3)]
        public List<string> Tags { get; set; } = new();
        
        [Key(4)]
        public Dictionary<string, object> Properties { get; set; } = new();
    }

    [Fact]
    public async Task MessagePack_vs_JSON_SerializationPerformance()
    {
        // Arrange
        const int iterations = 1000;
        var testData = new TestData
        {
            Id = 12345,
            Name = "Performance Test Data",
            CreatedAt = DateTime.UtcNow,
            Tags = new List<string> { "performance", "test", "cache", "serialization" },
            Properties = new Dictionary<string, object>
            {
                { "category", "electronics" },
                { "price", 299.99m },
                { "inStock", true },
                { "ratings", new[] { 4.5, 4.8, 4.2, 4.9 } }
            }
        };

        // MessagePack 직렬화 성능 측정
        var stopwatch = Stopwatch.StartNew();
        byte[] messagePackData = null!;
        
        for (int i = 0; i < iterations; i++)
        {
            messagePackData = MessagePackSerializer.Serialize(testData);
        }
        
        var messagePackSerializeTime = stopwatch.ElapsedMilliseconds;
        
        // MessagePack 역직렬화 성능 측정
        stopwatch.Restart();
        TestData messagePackResult = null!;
        
        for (int i = 0; i < iterations; i++)
        {
            messagePackResult = MessagePackSerializer.Deserialize<TestData>(messagePackData);
        }
        
        var messagePackDeserializeTime = stopwatch.ElapsedMilliseconds;

        // JSON 직렬화 성능 측정
        stopwatch.Restart();
        string jsonData = null!;
        
        for (int i = 0; i < iterations; i++)
        {
            jsonData = JsonSerializer.Serialize(testData);
        }
        
        var jsonSerializeTime = stopwatch.ElapsedMilliseconds;
        
        // JSON 역직렬화 성능 측정
        stopwatch.Restart();
        TestData jsonResult = null!;
        
        for (int i = 0; i < iterations; i++)
        {
            jsonResult = JsonSerializer.Deserialize<TestData>(jsonData)!;
        }
        
        var jsonDeserializeTime = stopwatch.ElapsedMilliseconds;

        // 결과 출력
        var messagePackSize = messagePackData.Length;
        var jsonSize = System.Text.Encoding.UTF8.GetByteCount(jsonData);
        
        Console.WriteLine($"=== 성능 비교 결과 ({iterations}회 반복) ===");
        Console.WriteLine($"MessagePack - 직렬화: {messagePackSerializeTime}ms, 역직렬화: {messagePackDeserializeTime}ms, 크기: {messagePackSize} bytes");
        Console.WriteLine($"JSON - 직렬화: {jsonSerializeTime}ms, 역직렬화: {jsonDeserializeTime}ms, 크기: {jsonSize} bytes");
        Console.WriteLine($"성능 향상 - 직렬화: {(double)jsonSerializeTime / messagePackSerializeTime:F1}x, 역직렬화: {(double)jsonDeserializeTime / messagePackDeserializeTime:F1}x");
        Console.WriteLine($"크기 절약: {(1 - (double)messagePackSize / jsonSize):P1}");

        // 성능 검증
        messagePackSerializeTime.Should().BeLessThan(jsonSerializeTime, "MessagePack 직렬화가 더 빨라야 함");
        messagePackDeserializeTime.Should().BeLessThan(jsonDeserializeTime, "MessagePack 역직렬화가 더 빨라야 함");
        messagePackSize.Should().BeLessThan(jsonSize, "MessagePack이 더 작은 크기를 가져야 함");
        
        // 데이터 정확성 검증
        messagePackResult.Id.Should().Be(testData.Id);
        messagePackResult.Name.Should().Be(testData.Name);
        messagePackResult.Tags.Should().BeEquivalentTo(testData.Tags);
    }

    [Fact]
    public async Task CacheProvider_MessagePack_vs_JSON_Performance()
    {
        // Arrange
        const int operations = 500;
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var mockLogger = new Mock<ILogger<MemoryCacheProvider>>();
        var options = new AthenaCacheOptions { DefaultExpirationMinutes = 30 };
        var cacheProvider = new MemoryCacheProvider(memoryCache, options, mockLogger.Object);

        var testData = Enumerable.Range(1, operations)
            .Select(i => new TestData
            {
                Id = i,
                Name = $"Test Item {i}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                Tags = new List<string> { $"tag{i}", "performance", "test" },
                Properties = new Dictionary<string, object>
                {
                    { "index", i },
                    { "isEven", i % 2 == 0 },
                    { "value", i * 1.5 }
                }
            })
            .ToDictionary(x => $"test_key_{x.Id}", x => x);

        // 캐시 저장 성능 측정
        var stopwatch = Stopwatch.StartNew();
        
        foreach (var kvp in testData)
        {
            await cacheProvider.SetAsync(kvp.Key, kvp.Value);
        }
        
        var setTime = stopwatch.ElapsedMilliseconds;
        
        // 캐시 조회 성능 측정
        stopwatch.Restart();
        var retrievedData = new List<TestData>();
        
        foreach (var key in testData.Keys)
        {
            var cached = await cacheProvider.GetAsync<TestData>(key);
            if (cached != null)
            {
                retrievedData.Add(cached);
            }
        }
        
        var getTime = stopwatch.ElapsedMilliseconds;

        // 결과 출력 및 검증
        Console.WriteLine($"=== 캐시 성능 테스트 결과 ({operations}개 항목) ===");
        Console.WriteLine($"저장 시간: {setTime}ms (평균 {(double)setTime / operations:F2}ms/item)");
        Console.WriteLine($"조회 시간: {getTime}ms (평균 {(double)getTime / operations:F2}ms/item)");
        
        // 성능 목표 검증
        setTime.Should().BeLessThan(2000, "저장 작업이 2초 미만이어야 함");
        getTime.Should().BeLessThan(1000, "조회 작업이 1초 미만이어야 함");
        retrievedData.Should().HaveCount(operations, "모든 데이터가 정상적으로 캐시되고 조회되어야 함");
        
        // 데이터 무결성 검증
        var firstItem = retrievedData.First();
        firstItem.Should().NotBeNull();
        firstItem.Properties.Should().ContainKey("index");
    }

    [Fact]
    public async Task CachedResponse_MessagePack_Serialization()
    {
        // Arrange
        const int iterations = 100;
        var cachedResponse = new CachedResponse
        {
            StatusCode = 200,
            ContentType = "application/json",
            Content = """{"message": "Hello World", "timestamp": "2024-01-01T00:00:00Z", "data": [1,2,3,4,5]}""",
            Headers = new Dictionary<string, string>
            {
                { "Cache-Control", "public, max-age=3600" },
                { "Content-Encoding", "gzip" },
                { "X-Custom-Header", "custom-value" }
            },
            CachedAt = DateTime.UtcNow
        };

        // MessagePack 직렬화 성능 측정
        var stopwatch = Stopwatch.StartNew();
        byte[] serializedData = null!;
        
        for (int i = 0; i < iterations; i++)
        {
            serializedData = MessagePackSerializer.Serialize(cachedResponse);
        }
        
        var serializeTime = stopwatch.ElapsedMilliseconds;
        
        // MessagePack 역직렬화 성능 측정
        stopwatch.Restart();
        CachedResponse deserializedResponse = null!;
        
        for (int i = 0; i < iterations; i++)
        {
            deserializedResponse = MessagePackSerializer.Deserialize<CachedResponse>(serializedData);
        }
        
        var deserializeTime = stopwatch.ElapsedMilliseconds;

        // 결과 출력 및 검증
        Console.WriteLine($"=== CachedResponse 직렬화 성능 ({iterations}회) ===");
        Console.WriteLine($"직렬화: {serializeTime}ms, 역직렬화: {deserializeTime}ms, 크기: {serializedData.Length} bytes");
        
        // 성능 목표
        serializeTime.Should().BeLessThan(100, "CachedResponse 직렬화가 빨라야 함");
        deserializeTime.Should().BeLessThan(100, "CachedResponse 역직렬화가 빨라야 함");
        
        // 데이터 무결성 검증
        deserializedResponse.StatusCode.Should().Be(cachedResponse.StatusCode);
        deserializedResponse.ContentType.Should().Be(cachedResponse.ContentType);
        deserializedResponse.Content.Should().Be(cachedResponse.Content);
        deserializedResponse.Headers.Should().BeEquivalentTo(cachedResponse.Headers);
        deserializedResponse.CachedAt.Should().BeCloseTo(cachedResponse.CachedAt, TimeSpan.FromSeconds(1));
    }
}