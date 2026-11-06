using MestoBeach.Orders.Application.Commands;
using MestoBeach.Orders.Application.Common;
using MestoBeach.Orders.Domain.Repositories;
using MestoBeach.Orders.Infrastructure.Configuration;
using MestoBeach.Orders.Infrastructure.Hubs;
using MestoBeach.Orders.Infrastructure.Persistence;
using MestoBeach.Orders.Infrastructure.Persistence.Repositories;
using MestoBeach.Orders.Infrastructure.Services;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Shared.Behaviors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MestoBeach.Orders.Infrastructure;

public static class OrdersModule
{
    private const string LocalMongoFallback =
        "mongodb://mestobeach:mestobeach_dev@localhost:27017/mestobeach_catalog?authSource=admin";

    public static IServiceCollection AddOrdersModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        // ── Configuration ────────────────────────────────────────────────────
        services.Configure<OrdersCatalogOptions>(
            config.GetSection(OrdersCatalogOptions.SectionName));

        // ── DbContext ────────────────────────────────────────────────────────
        services.AddDbContext<OrdersDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("Postgres"),
                b => b.MigrationsAssembly("MestoBeach.Orders.Infrastructure")));

        // ── MongoDB client (shared if already registered, else new) ───────────
        // IMongoClient is registered by Catalog module; add only if not present
        if (!services.Any(d => d.ServiceType == typeof(IMongoClient)))
        {
            var mongoConnStr = config["Catalog:MongoConnectionString"]
                ?? config.GetConnectionString("MongoDB")
                ?? LocalMongoFallback;
            services.AddSingleton<IMongoClient>(new MongoClient(mongoConnStr));
        }

        // ── Repositories ─────────────────────────────────────────────────────
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IKdsStationRepository, KdsStationRepository>();
        // Ciclo de vida por ítem y viajes de entrega (ADR-025 / ADR-027).
        services.AddScoped<IOrderItemRepository, OrderItemRepository>();
        services.AddScoped<IDeliveryTripRepository, DeliveryTripRepository>();

        // ── Application services ─────────────────────────────────────────────
        services.AddScoped<ICatalogValidationService, CatalogValidationService>();
        services.AddScoped<IPinValidationService, PinValidationService>();
        services.AddScoped<ISessionReadService, SessionReadService>();

        // ── SignalR ──────────────────────────────────────────────────────────
        // SignalR is already added by Sessions module; adding twice is safe
        services.AddSignalR();
        services.AddScoped<IOrderSignalRNotifier, OrderSignalRNotifier>();

        // ── Cross-module: replace Sessions stub with real implementation ──────
        // Last registration wins; Orders overrides the SessionsModule stub.
        services.AddScoped<IOrderSummaryService, OrderSummaryService>();

        // ── MediatR handlers from Application layer ───────────────────────────
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(CreateOrderCommand).Assembly));

        // FluentValidation validators + validation pipeline behavior
        services.AddModuleValidation(typeof(CreateOrderCommand).Assembly);

        return services;
    }
}
