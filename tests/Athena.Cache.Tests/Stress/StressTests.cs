using Athena.Cache.Core.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Athena.Cache.Core.Extensions;

namespace Athena.Cache.Tests.Stress;

public class StressTests
{
    [Fact]
    public async Task ConcurrentOperations_ShouldHandleHighLoad()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAthenaCacheComplete();

        var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<IAthenaCache>();

        const int concurrentOperations = 1000;
        const int operationsPerTask = 10;

        var results = new ConcurrentBag<bool>();
        var random = new Random();

        // Act - 동시 읽기/쓰기 작업
        var tasks = Enumerable.Range(0, concurrentOperations).Select(async taskId =>
        {
            try
            {
                for (var i = 0; i < operationsPerTask; i++)
                {
                    var key = $"stress_key_{taskId}_{i}";
                    var value = $"stress_value_{taskId}_{i}_{random.Next()}";

                    // 쓰기 작업
                    await cache.SetAsync(key, value);

                    // 읽기 작업
                    var retrieved = await cache.GetAsync<string>(key);

                    // 검증
                    var success = retrieved == value;
                    results.Add(success);

                    // 일부 키 삭제
                    if (i % 3 == 0)
                    {
                        await cache.RemoveAsync(key);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        });

        var completedTasks = await Task.WhenAll(tasks);

        // Assert
        completedTasks.All(success => success).Should().BeTrue();

        var successfulOperations = results.Count(success => success);
        var totalOperations = concurrentOperations * operationsPerTask;

        successfulOperations.Should().Be(totalOperations);
    }

    [Fact]
    public async Task HighVolumeInvalidation_ShouldBeStable()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAthenaCacheComplete();

        var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<IAthenaCache>();
        var invalidator = serviceProvider.GetRequiredService<ICacheInvalidator>();

        const int tableCount = 50;
        const int keysPerTable = 100;

        // 대량의 캐시 데이터 생성
        var tableNames = Enumerable.Range(1, tableCount).Select(i => $"Table_{i}").ToArray();

        foreach (var tableName in tableNames)
        {
            var keys = Enumerable.Range(1, keysPerTable).Select(j => $"{tableName}_key_{j}");

            foreach (var key in keys)
            {
                await cache.SetAsync(key, $"value_for_{key}");
                await invalidator.TrackCacheKeyAsync(tableName, key);
            }
        }

        // Act - 동시 무효화
        var invalidationTasks = tableNames.Select(async tableName =>
        {
            try
            {
                await invalidator.InvalidateAsync(tableName);
                return true;
            }
            catch
            {
                return false;
            }
        });

        var results = await Task.WhenAll(invalidationTasks);

        // Assert
        results.All(success => success).Should().BeTrue();

        // 무효화 후 확인
        var remainingKeysCount = 0;
        foreach (var tableName in tableNames)
        {
            var trackedKeys = await invalidator.GetTrackedKeysAsync(tableName);
            remainingKeysCount += trackedKeys.Count();
        }

        remainingKeysCount.Should().Be(0);
    }
}