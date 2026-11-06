using MestoBeach.Payments.Application.Commands;
using MestoBeach.Payments.Domain.Repositories;
using MestoBeach.Payments.Infrastructure.Persistence;
using MestoBeach.Payments.Infrastructure.Persistence.Repositories;
using MestoBeach.Payments.Infrastructure.Services;
using MestoBeach.Payments.Application.Common;
using MestoBeach.Shared.Behaviors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MestoBeach.Payments.Infrastructure;

public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        // ── DbContext ────────────────────────────────────────────────────────
        services.AddDbContext<PaymentsDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("Postgres"),
                b => b.MigrationsAssembly("MestoBeach.Payments.Infrastructure")));

        // ── Repositories ─────────────────────────────────────────────────────
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentConfigRepository, PaymentConfigRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();

        // ── Cross-module services ─────────────────────────────────────────────
        services.AddScoped<ISessionCloser, SessionCloserService>();
        services.AddScoped<ISessionReader, SessionReaderService>();
        services.AddScoped<ISessionOrderReader, SessionOrderReaderService>();

        // ── MediatR handlers from Application layer ───────────────────────────
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(RegisterCashPaymentCommand).Assembly));

        // FluentValidation validators + validation pipeline behavior
        services.AddModuleValidation(typeof(RegisterCashPaymentCommand).Assembly);

        return services;
    }
}
