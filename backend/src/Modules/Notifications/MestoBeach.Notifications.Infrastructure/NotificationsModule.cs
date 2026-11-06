using MestoBeach.Notifications.Application.Commands;
using MestoBeach.Notifications.Application.Common;
using MestoBeach.Notifications.Infrastructure.Hubs;
using MestoBeach.Notifications.Infrastructure.Persistence;
using MestoBeach.Notifications.Infrastructure.Services;
using MestoBeach.Shared.Behaviors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MestoBeach.Notifications.Infrastructure;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        // ── DbContext ────────────────────────────────────────────────────────
        services.AddDbContext<NotificationsDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("Postgres"),
                b => b.MigrationsAssembly("MestoBeach.Notifications.Infrastructure")));

        // ── Email sender ─────────────────────────────────────────────────────
        services.Configure<MailKitOptions>(config.GetSection("MailKit"));
        services.AddScoped<IEmailSender, MailKitEmailSender>();

        // ── Broadcast service (uses IHubContext<RealtimeHub>) ─────────────────
        services.AddScoped<IBroadcastService, BroadcastService>();

        // ── Maintenance service (uses Redis) ─────────────────────────────────
        services.AddScoped<IMaintenanceService, MaintenanceService>();

        // ── Email template repository ─────────────────────────────────────────
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();

        // ── MediatR handlers from Application layer ───────────────────────────
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SendEmailCommand).Assembly));

        // FluentValidation validators + validation pipeline behavior
        services.AddModuleValidation(typeof(SendEmailCommand).Assembly);

        return services;
    }
}
