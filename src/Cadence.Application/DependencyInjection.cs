using Cadence.Application.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Handlers are scoped rather than singletons: each one closes over the
    /// repositories and unit of work of a single request, and a singleton would
    /// capture one request's DbContext for the lifetime of the process.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<RegisterAthleteHandler>();
        services.AddScoped<AuthenticateAthleteHandler>();
        services.AddScoped<ImportActivityHandler>();
        services.AddScoped<ProcessActivityHandler>();
        services.AddScoped<ListActivitiesHandler>();
        services.AddScoped<GetActivityDetailHandler>();
        services.AddScoped<GetTimeSeriesHandler>();
        services.AddScoped<GetTrendsHandler>();
        services.AddScoped<FindNearbyActivitiesHandler>();
        services.AddScoped<GenerateCoachingReportHandler>();
        services.AddScoped<GetLatestCoachingReportHandler>();
        services.AddScoped<GetAthleteHandler>();
        services.AddScoped<UpdateAthleteProfileHandler>();
        services.AddScoped<DeleteActivityHandler>();

        return services;
    }
}
