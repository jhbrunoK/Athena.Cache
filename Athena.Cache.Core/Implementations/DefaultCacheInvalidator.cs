﻿using Athena.Cache.Core.Abstractions;
using Athena.Cache.Core.Configuration;

namespace Athena.Cache.Core.Implementations;

/// <summary>
/// 기본 캐시 무효화 관리자 구현
/// </summary>
public class DefaultCacheInvalidator(
    IAthenaCache cache,
    ICacheKeyGenerator keyGenerator,
    AthenaCacheOptions options,
    ILogger<DefaultCacheInvalidator> logger)
    : ICacheInvalidator
{
    /// <summary>
    /// 테이블 변경 시 연관된 모든 캐시 무효화
    /// </summary>
    public async Task InvalidateAsync(string tableName, CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTime.UtcNow;

            // 테이블 추적 키로 연결된 모든 캐시 키들 조회
            var trackedKeys = await GetTrackedKeysAsync(tableName, cancellationToken);
            var keysToInvalidate = trackedKeys.ToList();

            if (keysToInvalidate.Count == 0)
            {
                if (options.Logging.LogInvalidation)
                {
                    logger.LogInformation("No cached keys found for table '{TableName}'", tableName);
                }
                return;
            }

            // 각 캐시 키 삭제
            var invalidatedCount = 0;
            foreach (var key in keysToInvalidate)
            {
                try
                {
                    await cache.RemoveAsync(key, cancellationToken);
                    invalidatedCount++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to invalidate cache key '{CacheKey}'", key);
                }
            }

            // 테이블 추적 키도 초기화
            var trackingKey = keyGenerator.GenerateTableTrackingKey(tableName);
            await cache.RemoveAsync(trackingKey, cancellationToken);

            var duration = DateTime.UtcNow - startTime;

            if (options.Logging.LogInvalidation)
            {
                logger.LogInformation(
                    "Invalidated {InvalidatedCount}/{TotalCount} cache keys for table '{TableName}' in {Duration}ms",
                    invalidatedCount, keysToInvalidate.Count, tableName, duration.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            if (options.ErrorHandling.SilentFallback)
            {
                logger.LogError(ex, "Failed to invalidate cache for table '{TableName}'", tableName);

                if (options.ErrorHandling.CustomErrorHandler != null)
                {
                    await options.ErrorHandling.CustomErrorHandler(ex);
                }
            }
            else
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 패턴에 맞는 캐시들 무효화
    /// </summary>
    public async Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTime.UtcNow;

            // 패턴으로 캐시 키 삭제
            await cache.RemoveByPatternAsync(pattern, cancellationToken);

            var duration = DateTime.UtcNow - startTime;

            if (options.Logging.LogInvalidation)
            {
                logger.LogInformation(
                    "Invalidated cache keys matching pattern '{Pattern}' in {Duration}ms",
                    pattern, duration.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            if (options.ErrorHandling.SilentFallback)
            {
                logger.LogError(ex, "Failed to invalidate cache by pattern '{Pattern}'", pattern);

                if (options.ErrorHandling.CustomErrorHandler != null)
                {
                    await options.ErrorHandling.CustomErrorHandler(ex);
                }
            }
            else
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 캐시 키를 테이블과 연결하여 추적
    /// Redis Set을 이용하여 테이블별로 관련 캐시 키들을 저장
    /// </summary>
    public async Task TrackCacheKeyAsync(string tableName, string cacheKey, CancellationToken cancellationToken = default)
    {
        await TrackCacheKeyAsync([tableName], cacheKey, cancellationToken);
    }

    /// <summary>
    /// 여러 테이블과 연결하여 추적
    /// </summary>
    public async Task TrackCacheKeyAsync(string[] tableNames, string cacheKey, CancellationToken cancellationToken = default)
    {
        if (tableNames == null || tableNames.Length == 0) return;

        try
        {
            foreach (var tableName in tableNames)
            {
                var trackingKey = keyGenerator.GenerateTableTrackingKey(tableName);

                // Redis에서는 Set 자료구조 사용, MemoryCache에서는 HashSet으로 구현
                await AddToTrackingSetAsync(trackingKey, cacheKey, cancellationToken);
            }

            if (options.Logging.LogInvalidation)
            {
                logger.LogDebug(
                    "Tracked cache key '{CacheKey}' for tables [{Tables}]",
                    cacheKey, string.Join(", ", tableNames));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to track cache key '{CacheKey}' for tables [{Tables}]",
                cacheKey, string.Join(", ", tableNames));
        }
    }

    /// <summary>
    /// 테이블과 연결된 모든 캐시 키 조회
    /// </summary>
    public async Task<IEnumerable<string>> GetTrackedKeysAsync(string tableName, CancellationToken cancellationToken = default)
    {
        try
        {
            var trackingKey = keyGenerator.GenerateTableTrackingKey(tableName);
            var trackedKeys = await GetFromTrackingSetAsync(trackingKey, cancellationToken);

            return trackedKeys ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get tracked keys for table '{TableName}'", tableName);
            return [];
        }
    }

    /// <summary>
    /// 관련 테이블들과 함께 연쇄 무효화
    /// </summary>
    public async Task InvalidateWithRelatedAsync(string tableName, string[] relatedTables, int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        var processedTables = new HashSet<string>();
        await InvalidateRecursiveAsync(tableName, relatedTables, maxDepth, 0, processedTables, cancellationToken);
    }

    /// <summary>
    /// 재귀적 무효화 (순환 참조 방지)
    /// </summary>
    private async Task InvalidateRecursiveAsync(
        string tableName,
        string[] relatedTables,
        int maxDepth,
        int currentDepth,
        HashSet<string> processedTables,
        CancellationToken cancellationToken)
    {
        // 이미 처리된 테이블이거나 최대 깊이 초과 시 중단
        if (processedTables.Contains(tableName) || currentDepth >= maxDepth)
        {
            return;
        }

        processedTables.Add(tableName);

        // 현재 테이블 무효화
        await InvalidateAsync(tableName, cancellationToken);

        if (options.Logging.LogInvalidation)
        {
            logger.LogDebug("Invalidated table '{TableName}' at depth {Depth}", tableName, currentDepth);
        }

        // 관련 테이블들 재귀적으로 무효화
        if (relatedTables != null && relatedTables.Length > 0)
        {
            foreach (var relatedTable in relatedTables)
            {
                await InvalidateRecursiveAsync(relatedTable, [], maxDepth, currentDepth + 1, processedTables, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 추적 Set에 캐시 키 추가 (구현체별로 오버라이드)
    /// </summary>
    private async Task AddToTrackingSetAsync(string trackingKey, string cacheKey, CancellationToken cancellationToken)
    {
        // 기본 구현: HashSet으로 관리
        var existingSet = await cache.GetAsync<HashSet<string>>(trackingKey, cancellationToken) ?? [];
        existingSet.Add(cacheKey);

        // 만료 시간 설정 (기본 설정의 2배로 설정하여 캐시보다 오래 남아있게 함)
        var expiration = TimeSpan.FromMinutes(options.DefaultExpirationMinutes * 2);
        await cache.SetAsync(trackingKey, existingSet, expiration, cancellationToken);
    }

    /// <summary>
    /// 추적 Set에서 캐시 키들 조회 (구현체별로 오버라이드)
    /// </summary>
    private async Task<IEnumerable<string>?> GetFromTrackingSetAsync(string trackingKey, CancellationToken cancellationToken)
    {
        var set = await cache.GetAsync<HashSet<string>>(trackingKey, cancellationToken);
        return set?.AsEnumerable();
    }

    /// <summary>
    /// 여러 테이블을 배치로 무효화 (성능 최적화)
    /// </summary>
    public async Task InvalidateBatchAsync(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
    {
        if (tableNames == null) return;

        var tables = tableNames.ToList();
        if (tables.Count == 0) return;

        try
        {
            var startTime = DateTime.UtcNow;
            
            // 모든 테이블의 추적 키들을 동시에 조회
            var trackingTasks = tables.Select(tableName => GetTrackedKeysAsync(tableName, cancellationToken));
            var allTrackedKeysResults = await Task.WhenAll(trackingTasks);
            
            // 중복 제거하여 무효화할 키 목록 생성
            var keysToInvalidate = allTrackedKeysResults
                .SelectMany(keys => keys)
                .Distinct()
                .ToList();

            if (keysToInvalidate.Count == 0)
            {
                if (options.Logging.LogInvalidation)
                {
                    logger.LogInformation("No cached keys found for tables '{Tables}'", string.Join(", ", tables));
                }
                return;
            }

            // 배치로 키들을 무효화 (병렬 처리)
            var batchSize = Math.Min(50, Environment.ProcessorCount * 2); // CPU 코어 수에 따른 배치 크기
            var batches = keysToInvalidate
                .Select((key, index) => new { key, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.key).ToList())
                .ToList();

            var invalidatedCount = 0;
            foreach (var batch in batches)
            {
                var batchTasks = batch.Select(async key =>
                {
                    try
                    {
                        await cache.RemoveAsync(key, cancellationToken);
                        return 1;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to invalidate cache key '{CacheKey}'", key);
                        return 0;
                    }
                });
                
                var batchResults = await Task.WhenAll(batchTasks);
                invalidatedCount += batchResults.Sum();
            }

            // 테이블 추적 키들도 정리
            var trackingKeyTasks = tables.Select(tableName => 
                cache.RemoveAsync(keyGenerator.GenerateTableTrackingKey(tableName), cancellationToken));
            await Task.WhenAll(trackingKeyTasks);

            var elapsed = DateTime.UtcNow - startTime;

            if (options.Logging.LogInvalidation)
            {
                logger.LogInformation(
                    "Batch invalidated {InvalidatedCount} cache keys for {TableCount} tables in {ElapsedMs}ms",
                    invalidatedCount, tables.Count, elapsed.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during batch invalidation for tables '{Tables}'", string.Join(", ", tables));
            
            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 여러 패턴을 배치로 무효화 (성능 최적화)
    /// </summary>
    public async Task InvalidateByPatternBatchAsync(IEnumerable<string> patterns, CancellationToken cancellationToken = default)
    {
        if (patterns == null) return;

        var patternList = patterns.ToList();
        if (patternList.Count == 0) return;

        try
        {
            var startTime = DateTime.UtcNow;
            
            // 모든 패턴에 대해 병렬로 무효화 실행
            var invalidationTasks = patternList.Select(async pattern =>
            {
                try
                {
                    await cache.RemoveByPatternAsync(pattern, cancellationToken);
                    return 1;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to invalidate cache keys with pattern '{Pattern}'", pattern);
                    return 0;
                }
            });

            var results = await Task.WhenAll(invalidationTasks);
            var successCount = results.Sum();
            
            var elapsed = DateTime.UtcNow - startTime;

            if (options.Logging.LogInvalidation)
            {
                logger.LogInformation(
                    "Batch pattern invalidation completed: {SuccessCount}/{TotalCount} patterns processed in {ElapsedMs}ms",
                    successCount, patternList.Count, elapsed.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during batch pattern invalidation");
            
            if (!options.ErrorHandling.SilentFallback)
            {
                throw;
            }
        }
    }
}