using Athena.Cache.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Athena.Cache.Tests.Utilities;

/// <summary>
/// 테스트용 유틸리티 클래스
/// </summary>
public static class TestUtilities
{
    /// <summary>
    /// Mock ActionExecutingContext 생성
    /// </summary>
    public static ActionExecutingContext CreateMockActionContext(
        string controllerName = "TestController",
        string actionName = "TestAction",
        Dictionary<string, object?>? parameters = null)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        var controllerDescriptor = new ControllerActionDescriptor
        {
            ControllerName = controllerName.Replace("Controller", ""),
            ActionName = actionName,
            FilterDescriptors = new List<FilterDescriptor>()
        };

        var routeValues = new Dictionary<string, string?>
        {
            ["controller"] = controllerDescriptor.ControllerName,
            ["action"] = controllerDescriptor.ActionName
        };

        var actionContext = new ActionContext(httpContext, new(), controllerDescriptor)
        {
            RouteData = { Values = { ["action"] = actionName } }
        };

        var actionArguments = parameters ?? new Dictionary<string, object?>();

        var controller = new Mock<ControllerBase>().Object;

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            actionArguments,
            controller);
    }

    /// <summary>
    /// 테스트용 캐시 설정 생성
    /// </summary>
    public static CacheConfiguration CreateTestCacheConfiguration(
        string controller = "TestController",
        string action = "TestAction",
        int expirationMinutes = 30,
        params string[] invalidationTables)
    {
        return new CacheConfiguration
        {
            Controller = controller,
            Action = action,
            Enabled = true,
            ExpirationMinutes = expirationMinutes,
            InvalidationRules = invalidationTables.Select(table => new TableInvalidationRule
            {
                TableName = table,
                InvalidationType = Core.Enums.InvalidationType.All
            }).ToList()
        };
    }

    /// <summary>
    /// 테스트 데이터 생성
    /// </summary>
    public static IEnumerable<T> GenerateTestData<T>(int count, Func<int, T> factory)
    {
        return Enumerable.Range(1, count).Select(factory);
    }

    /// <summary>
    /// 비동기 작업의 완료 대기 (타임아웃 포함)
    /// </summary>
    public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
    {
        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            throw new TimeoutException($"작업이 {timeout.TotalMilliseconds}ms 내에 완료되지 않았습니다.");
        }

        return await task;
    }

    /// <summary>
    /// 메모리 사용량 측정
    /// </summary>
    public static long MeasureMemoryUsage(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memoryBefore = GC.GetTotalMemory(false);

        action();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memoryAfter = GC.GetTotalMemory(false);

        return memoryAfter - memoryBefore;
    }
}