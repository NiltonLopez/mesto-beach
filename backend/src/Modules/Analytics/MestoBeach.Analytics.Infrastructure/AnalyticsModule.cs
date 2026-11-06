using MestoBeach.Analytics.Application.Common;
using MestoBeach.Analytics.Application.Queries;
using MestoBeach.Analytics.Infrastructure.Persistence;
using MestoBeach.Analytics.Infrastructure.Repositories;
using MestoBeach.Shared.Behaviors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MestoBeach.Analytics.Infrastructure;

public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        // ── DbContext (read-only projections, no migrations) ─────────────────
        services.AddDbContext<AnalyticsDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("Postgres")));

        // ── Repository ───────────────────────────────────────────────────────
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

        // ── MediatR handlers from Application layer ───────────────────────────
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GetSalesSummaryQuery).Assembly));

        // FluentValidation validators + validation pipeline behavior
        services.AddModuleValidation(typeof(GetSalesSummaryQuery).Assembly);

        return services;
    }
}
