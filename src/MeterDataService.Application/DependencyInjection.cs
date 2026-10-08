using MeterDataService.Application.GapFilling;
using MeterDataService.Application.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeterDataService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Endpoint and background service must share one queue and one status store.
        services.AddSingleton(_ => new ImportChannel());
        services.AddSingleton<ImportJobTracker>();
        services.AddSingleton(_ => new ValidationEngine());
        services.AddSingleton<GapFillingService>();
        services.AddHostedService<ImportBackgroundService>();
        return services;
    }
}
