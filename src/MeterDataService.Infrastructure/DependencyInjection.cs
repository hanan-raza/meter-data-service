using MeterDataService.Application.Aggregation;
using MeterDataService.Application.Import;
using MeterDataService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeterDataService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<MeterDataDbContext>(options => options.UseMeterDataStore(connectionString));
        services.AddScoped<IMeasurementSeriesRepository, MeasurementSeriesRepository>();
        services.AddScoped<IAggregationRepository, AggregationRepository>();
        return services;
    }

    /// <summary>Single place for provider settings so the app, design-time tooling and tests build identical models.</summary>
    public static TBuilder UseMeterDataStore<TBuilder>(this TBuilder options, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();
        return options;
    }
}
