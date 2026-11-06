using Azure.Storage.Blobs;
using MestoBeach.Sessions.Application.Commands;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Sessions.Infrastructure.Hubs;
using MestoBeach.Sessions.Infrastructure.Persistence;
using MestoBeach.Sessions.Infrastructure.Persistence.Repositories;
using MestoBeach.Sessions.Infrastructure.Services;
using MestoBeach.Shared.Behaviors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MestoBeach.Sessions.Infrastructure;

public static class SessionsModule
{
    public static IServiceCollection AddSessionsModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        // DbContext
        services.AddDbContext<SessionsDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("Postgres"),
                b => b.MigrationsAssembly("MestoBeach.Sessions.Infrastructure")));

        // Repositories
        services.AddScoped<ITableRepository, TableRepository>();
        services.AddScoped<IFloorRepository, FloorRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();

        // Cache & PIN (Redis — IConnectionMultiplexer registered in Program.cs)
        services.AddScoped<ISessionCacheService, SessionCacheService>();
        services.AddScoped<ISessionPinService, SessionPinService>();

        // Token services (singletons — stateless, load RSA key once)
        services.AddSingleton<ISessionTokenService, SessionTokenService>();
        services.AddSingleton<IStaffJwtValidator, StaffJwtValidator>();

        // Tenant settings
        services.AddScoped<ITenantSettingsService, TenantSettingsService>();
        services.AddScoped<IShiftReader, ShiftReaderService>();
        services.AddScoped<IStationReadService, StationReadService>();

        // QR + Blob
        services.AddSingleton<IMenuUrlProvider, MenuUrlProvider>();
        services.AddSingleton<IQrCodeService, QrCodeService>();
        services.AddSingleton(_ =>
        {
            var connStr = config["Sessions:BlobConnectionString"]
                ?? config.GetConnectionString("BlobStorage")
                ?? "UseDevelopmentStorage=true";
            return new BlobServiceClient(connStr);
        });
        services.AddScoped<IBlobLogoService, BlobLogoService>();

        // SignalR notifier
        services.AddScoped<ISignalRNotifier, SignalRNotifier>();
        services.AddScoped<IWaiterCallService, WaiterCallService>();

        // Orders stub (replaced by Orders module in Sprint 4)
        services.AddScoped<IOrderSummaryService, OrderSummaryStub>();

        // SignalR hub
        services.AddSignalR();

        // MediatR handlers from Application layer
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(OpenSessionCommand).Assembly));

        // FluentValidation validators + validation pipeline behavior
        services.AddModuleValidation(typeof(OpenSessionCommand).Assembly);

        return services;
    }
}
