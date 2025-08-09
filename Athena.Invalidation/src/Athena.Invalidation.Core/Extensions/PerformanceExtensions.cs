using Athena.Invalidation.Core.Abstractions;
using Athena.Invalidation.Core.Performance;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Athena.Invalidation.Core.Extensions;

/// <summary>
/// 성능 최적화 확장 메서드
/// </summary>
public static class PerformanceExtensions
{
    /// <summary>
    /// 백그라운드 무효화 큐 및 프로세서 추가
    /// </summary>
    public static IServiceCollection AddBackgroundInvalidationProcessing(
        this IServiceCollection services,
        Action<BackgroundQueueOptions>? configureQueue = null,
        Action<BackgroundProcessorOptions>? configureProcessor = null)
    {
        if (configureQueue != null)
        {
            services.Configure(configureQueue);
        }
        
        if (configureProcessor != null)
        {
            services.Configure(configureProcessor);
        }

        // 백그라운드 큐 및 프로세서 등록
        services.TryAddSingleton<IBackgroundInvalidationQueue, BackgroundInvalidationQueue>();
        services.AddHostedService<BackgroundInvalidationProcessor>();

        return services;
    }

    /// <summary>
    /// 배치 최적화된 무효화 엔진 데코레이터 추가
    /// </summary>
    public static IServiceCollection AddBatchOptimization(
        this IServiceCollection services,
        Action<BatchOptimizationOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 기존 IInvalidationEngine을 배치 최적화 데코레이터로 감싸기
        services.Decorate<IInvalidationEngine, BatchOptimizedInvalidationEngine>();

        return services;
    }

    /// <summary>
    /// 전체 성능 최적화 스택 추가 (백그라운드 처리 + 배치 최적화)
    /// </summary>
    public static IServiceCollection AddInvalidationPerformanceOptimization(
        this IServiceCollection services,
        Action<BackgroundQueueOptions>? configureQueue = null,
        Action<BackgroundProcessorOptions>? configureProcessor = null,
        Action<BatchOptimizationOptions>? configureBatch = null)
    {
        return services
            .AddBackgroundInvalidationProcessing(configureQueue, configureProcessor)
            .AddBatchOptimization(configureBatch);
    }

    /// <summary>
    /// 고성능 환경을 위한 사전 구성된 성능 최적화
    /// </summary>
    public static IServiceCollection AddHighPerformanceInvalidation(this IServiceCollection services)
    {
        return services.AddInvalidationPerformanceOptimization(
            queue => 
            {
                queue.MaxQueueSize = 50000;
                queue.MaxBatchSize = 500;
                queue.BatchWaitTime = TimeSpan.FromMilliseconds(100);
            },
            processor => 
            {
                processor.MaxConcurrentJobs = Environment.ProcessorCount * 2;
                processor.MaxBatchSize = 500;
                processor.BatchWaitTime = TimeSpan.FromMilliseconds(100);
                processor.EnableBatchOptimization = true;
            },
            batch => 
            {
                batch.ImmediateProcessingThreshold = 50;
                batch.ImmediateBatchSize = 50;
                batch.ImmediateBatchInterval = TimeSpan.FromMilliseconds(100);
                batch.EnableImmediateBatching = true;
            }
        );
    }

    /// <summary>
    /// 저지연 환경을 위한 사전 구성된 성능 최적화
    /// </summary>
    public static IServiceCollection AddLowLatencyInvalidation(this IServiceCollection services)
    {
        return services.AddInvalidationPerformanceOptimization(
            queue => 
            {
                queue.MaxQueueSize = 10000;
                queue.MaxBatchSize = 50;
                queue.BatchWaitTime = TimeSpan.FromMilliseconds(10);
            },
            processor => 
            {
                processor.MaxConcurrentJobs = Environment.ProcessorCount * 4;
                processor.MaxBatchSize = 50;
                processor.BatchWaitTime = TimeSpan.FromMilliseconds(10);
                processor.EnableBatchOptimization = false; // 저지연을 위해 배치 최적화 비활성화
            },
            batch => 
            {
                batch.ImmediateProcessingThreshold = 10;
                batch.ImmediateBatchSize = 10;
                batch.ImmediateBatchInterval = TimeSpan.FromMilliseconds(50);
                batch.EnableImmediateBatching = true;
            }
        );
    }

    /// <summary>
    /// 메모리 효율성을 위한 사전 구성된 성능 최적화
    /// </summary>
    public static IServiceCollection AddMemoryEfficientInvalidation(this IServiceCollection services)
    {
        return services.AddInvalidationPerformanceOptimization(
            queue => 
            {
                queue.MaxQueueSize = 5000;
                queue.MaxBatchSize = 200;
                queue.BatchWaitTime = TimeSpan.FromSeconds(2);
            },
            processor => 
            {
                processor.MaxConcurrentJobs = Environment.ProcessorCount;
                processor.MaxBatchSize = 200;
                processor.BatchWaitTime = TimeSpan.FromSeconds(2);
                processor.EnableBatchOptimization = true;
            },
            batch => 
            {
                batch.ImmediateProcessingThreshold = 200;
                batch.ImmediateBatchSize = 100;
                batch.ImmediateBatchInterval = TimeSpan.FromSeconds(1);
                batch.EnableImmediateBatching = true;
            }
        );
    }
}

/// <summary>
/// 성능 통계 확장
/// </summary>
public static class PerformanceStatisticsExtensions
{
    /// <summary>
    /// 백그라운드 프로세서 통계 조회
    /// </summary>
    public static Dictionary<string, ProcessingStatistics> GetBackgroundProcessorStatistics(this IServiceProvider services)
    {
        var processor = services.GetService<BackgroundInvalidationProcessor>();
        return processor?.GetAllStatistics() ?? new Dictionary<string, ProcessingStatistics>();
    }

    /// <summary>
    /// 큐 상태 정보 조회
    /// </summary>
    public static QueueStatistics GetQueueStatistics(this IServiceProvider services)
    {
        var queue = services.GetService<IBackgroundInvalidationQueue>();
        if (queue == null)
        {
            return new QueueStatistics();
        }

        return new QueueStatistics
        {
            QueueCount = queue.Count,
            IsEmpty = queue.IsEmpty
        };
    }
}

/// <summary>
/// 큐 통계 정보
/// </summary>
public class QueueStatistics
{
    public int QueueCount { get; set; }
    public bool IsEmpty { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}