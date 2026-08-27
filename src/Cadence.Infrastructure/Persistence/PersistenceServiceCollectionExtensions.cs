using Cadence.Application.Abstractions;
using Cadence.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<CadenceDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.UseNetTopologySuite()));

        // The same scoped DbContext instance backs the unit of work and every repository,
        // so a handler that touches two repositories still commits once.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CadenceDbContext>());
        services.AddScoped<IAthleteRepository, AthleteRepository>();
        services.AddScoped<IActivityRepository, ActivityRepository>();
        services.AddScoped<ICoachingReportRepository, CoachingReportRepository>();

        return services;
    }
}
