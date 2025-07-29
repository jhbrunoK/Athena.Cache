using Microsoft.Extensions.DependencyInjection;

namespace Athena.Cache.Monitoring.Extensions;

public static class ServiceCollectionSignalRExtensions
{
    public static IServiceCollection TryAddSignalR(this IServiceCollection services)
    {
        // SignalR 서비스가 이미 등록되었는지 확인
        if (!services.Any(x => x.ServiceType.Name.Contains("SignalR")))
        {
            services.AddSignalR();
        }
        return services;
    }
}