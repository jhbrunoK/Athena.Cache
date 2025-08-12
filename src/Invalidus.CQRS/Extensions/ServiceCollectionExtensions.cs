using Invalidus.Core.Abstractions;
using Invalidus.CQRS.Abstractions;
using Invalidus.CQRS.Implementations;

namespace Invalidus.CQRS.Extensions;

/// <summary>
/// Invalidus CQRS 시스템 DI 확장 메서드
/// Dependency injection extensions for Invalidus CQRS system
/// </summary>
public static class ServiceCollectionExtensions
{
    #region Basic CQRS Registration

    /// <summary>
    /// Invalidus CQRS 시스템을 서비스 컬렉션에 추가
    /// Add Invalidus CQRS system to the service collection
    /// </summary>
    public static IServiceCollection AddInvalidusCQRS(
        this IServiceCollection services,
        Action<InvalidusCQRSOptions>? configureOptions = null)
    {
        // Configure options
        services.Configure<InvalidusCQRSOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        // Register core CQRS services
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IEventPublisher, InMemoryEventPublisher>();
        services.AddSingleton<IEventStore, InMemoryEventStore>();
        services.AddSingleton<IReadModelManager, ReadModelManager>();
        
        return services;
    }

    /// <summary>
    /// 구성 파일에서 CQRS 설정을 읽어서 추가
    /// Add CQRS with configuration from settings
    /// </summary>
    public static IServiceCollection AddInvalidusCQRSFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Invalidus:CQRS")
    {
        services.Configure<InvalidusCQRSOptions>(
            configuration.GetSection(sectionName));

        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IEventPublisher, InMemoryEventPublisher>();
        services.AddSingleton<IEventStore, InMemoryEventStore>();
        services.AddSingleton<IReadModelManager, ReadModelManager>();
        
        return services;
    }

    #endregion

    #region Environment-Specific Configurations

    /// <summary>
    /// 개발 환경용 CQRS 설정
    /// Development environment CQRS configuration
    /// </summary>
    public static IServiceCollection AddInvalidusCQRSDevelopment(
        this IServiceCollection services)
    {
        return services.AddInvalidusCQRS(options =>
        {
            options.EnableEventSourcing = true;
            options.EnableProjectionManagement = true;
            options.CommandTimeout = TimeSpan.FromMinutes(2);
            options.EventBatchSize = 100;
            options.MaxRetryAttempts = 3;
            options.EnableDetailedLogging = true;
        });
    }

    /// <summary>
    /// 프로덕션 환경용 CQRS 설정
    /// Production environment CQRS configuration
    /// </summary>
    public static IServiceCollection AddInvalidusCQRSProduction(
        this IServiceCollection services)
    {
        return services.AddInvalidusCQRS(options =>
        {
            options.EnableEventSourcing = true;
            options.EnableProjectionManagement = true;
            options.CommandTimeout = TimeSpan.FromMinutes(5);
            options.EventBatchSize = 500;
            options.MaxRetryAttempts = 5;
            options.EnableDetailedLogging = false;
            options.EnableEventStoreCleanup = true;
            options.EventStoreCleanupInterval = TimeSpan.FromHours(24);
        });
    }

    /// <summary>
    /// 고성능 환경용 CQRS 설정 (최소한의 오버헤드)
    /// High-performance environment CQRS (minimal overhead)
    /// </summary>
    public static IServiceCollection AddInvalidusCQRSHighPerformance(
        this IServiceCollection services)
    {
        return services.AddInvalidusCQRS(options =>
        {
            options.EnableEventSourcing = false; // Disable event sourcing for performance
            options.EnableProjectionManagement = false;
            options.CommandTimeout = TimeSpan.FromSeconds(30);
            options.EventBatchSize = 1000;
            options.MaxRetryAttempts = 1;
            options.EnableDetailedLogging = false;
        });
    }

    #endregion

    #region Command Handler Registration

    /// <summary>
    /// 명령 핸들러 등록
    /// Register command handler
    /// </summary>
    public static IServiceCollection AddCommandHandler<TCommand, THandler>(this IServiceCollection services)
        where TCommand : IInvalidationCommand
        where THandler : class, ICommandHandler<TCommand>
    {
        services.AddTransient<ICommandHandler<TCommand>, THandler>();
        return services;
    }

    /// <summary>
    /// 어셈블리에서 모든 명령 핸들러 자동 등록
    /// Auto-register all command handlers from assembly
    /// </summary>
    public static IServiceCollection AddCommandHandlersFromAssembly(
        this IServiceCollection services,
        System.Reflection.Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && 
                       t.GetInterfaces().Any(i => i.IsGenericType && 
                                                 i.GetGenericTypeDefinition() == typeof(ICommandHandler<>)))
            .ToList();

        foreach (var handlerType in handlerTypes)
        {
            var interfaces = handlerType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<>));

            foreach (var interfaceType in interfaces)
            {
                services.AddTransient(interfaceType, handlerType);
            }
        }

        return services;
    }

    #endregion

    #region Event Handler Registration

    /// <summary>
    /// 이벤트 핸들러 등록
    /// Register event handler
    /// </summary>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IInvalidationEvent
        where THandler : class, IEventHandler<TEvent>
    {
        services.AddTransient<IEventHandler<TEvent>, THandler>();
        return services;
    }

    /// <summary>
    /// 어셈블리에서 모든 이벤트 핸들러 자동 등록
    /// Auto-register all event handlers from assembly
    /// </summary>
    public static IServiceCollection AddEventHandlersFromAssembly(
        this IServiceCollection services,
        System.Reflection.Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && 
                       t.GetInterfaces().Any(i => i.IsGenericType && 
                                                 i.GetGenericTypeDefinition() == typeof(IEventHandler<>)))
            .ToList();

        foreach (var handlerType in handlerTypes)
        {
            var interfaces = handlerType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>));

            foreach (var interfaceType in interfaces)
            {
                services.AddTransient(interfaceType, handlerType);
            }
        }

        return services;
    }

    #endregion

    #region Projection Management

    /// <summary>
    /// Projection 관리자와 함께 CQRS 추가
    /// Add CQRS with projection manager
    /// </summary>
    public static IServiceCollection AddInvalidusCQRSWithProjections(
        this IServiceCollection services,
        Action<InvalidusCQRSOptions>? configureOptions = null)
    {
        services.AddInvalidusCQRS(options =>
        {
            options.EnableProjectionManagement = true;
            configureOptions?.Invoke(options);
        });

        services.AddSingleton<IProjectionManager, ProjectionManager>();
        services.AddHostedService<ProjectionBackgroundService>();

        return services;
    }

    /// <summary>
    /// Projection 처리기 등록
    /// Register projection processor
    /// </summary>
    public static IServiceCollection AddProjectionProcessor<TProcessor>(this IServiceCollection services)
        where TProcessor : class, IProjectionProcessor
    {
        services.AddTransient<IProjectionProcessor, TProcessor>();
        return services;
    }

    #endregion

    #region Event Store Configuration

    /// <summary>
    /// 외부 이벤트 스토어와 함께 CQRS 추가
    /// Add CQRS with external event store
    /// </summary>
    public static IServiceCollection AddInvalidusCQRSWithEventStore<TEventStore>(
        this IServiceCollection services,
        Action<InvalidusCQRSOptions>? configureOptions = null)
        where TEventStore : class, IEventStore
    {
        services.AddInvalidusCQRS(configureOptions);
        
        // Replace default event store
        // Remove existing registrations (simplified approach)
        var existingRegistration = services.FirstOrDefault(s => s.ServiceType == typeof(IEventStore));
        if (existingRegistration != null)
        {
            services.Remove(existingRegistration);
        }
        services.AddSingleton<IEventStore, TEventStore>();

        return services;
    }

    #endregion

    #region Advanced Features

    /// <summary>
    /// Read Model 의존성 자동 등록
    /// Auto-register read model dependencies
    /// </summary>
    public static IServiceCollection AddReadModelDependencies(
        this IServiceCollection services,
        Action<ReadModelDependencyBuilder> configureDependencies)
    {
        var builder = new ReadModelDependencyBuilder();
        configureDependencies(builder);

        services.AddSingleton(builder.Build());
        services.AddHostedService<ReadModelDependencyInitializerService>();

        return services;
    }

    /// <summary>
    /// CQRS 이벤트 구독자 등록
    /// Register CQRS event subscriber
    /// </summary>
    public static IServiceCollection AddInvalidusCQRSSubscriber(
        this IServiceCollection services,
        Action<InvalidusCQRSOptions>? configureOptions = null)
    {
        services.AddInvalidusCQRS(configureOptions);
        services.AddSingleton<IEventSubscriber, InMemoryEventSubscriber>();
        services.AddHostedService<EventSubscriptionService>();

        return services;
    }

    #endregion
}

/// <summary>
/// Invalidus CQRS 설정 옵션
/// Invalidus CQRS configuration options
/// </summary>
public class InvalidusCQRSOptions
{
    /// <summary>이벤트 소싱 활성화</summary>
    public bool EnableEventSourcing { get; set; } = true;
    
    /// <summary>Projection 관리 활성화</summary>
    public bool EnableProjectionManagement { get; set; } = true;
    
    /// <summary>명령 처리 타임아웃</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(1);
    
    /// <summary>이벤트 배치 크기</summary>
    public int EventBatchSize { get; set; } = 100;
    
    /// <summary>최대 재시도 횟수</summary>
    public int MaxRetryAttempts { get; set; } = 3;
    
    /// <summary>상세 로깅 활성화</summary>
    public bool EnableDetailedLogging { get; set; } = false;
    
    /// <summary>이벤트 스토어 정리 활성화</summary>
    public bool EnableEventStoreCleanup { get; set; } = false;
    
    /// <summary>이벤트 스토어 정리 간격</summary>
    public TimeSpan EventStoreCleanupInterval { get; set; } = TimeSpan.FromDays(7);
    
    /// <summary>이벤트 보관 기간</summary>
    public TimeSpan EventRetentionPeriod { get; set; } = TimeSpan.FromDays(30);
}

/// <summary>
/// Read Model 의존성 빌더
/// Read model dependency builder
/// </summary>
public class ReadModelDependencyBuilder
{
    private readonly List<ReadModelDependency> _dependencies = new();

    public ReadModelDependencyBuilder AddDependency(
        string readModelType,
        string dependsOnEntity,
        IEnumerable<string>? properties = null,
        InvalidationStrategy strategy = InvalidationStrategy.Immediate,
        TimeSpan? delay = null)
    {
        _dependencies.Add(new ReadModelDependency
        {
            ReadModelType = readModelType,
            DependsOnEntity = dependsOnEntity,
            Properties = properties?.ToList(),
            Strategy = strategy,
            Delay = delay,
            CascadeInvalidation = true
        });

        return this;
    }

    internal List<ReadModelDependency> Build() => _dependencies;
}