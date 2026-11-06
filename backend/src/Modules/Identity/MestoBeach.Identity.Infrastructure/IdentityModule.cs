using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MestoBeach.Identity.Application.Auth.Platform.Commands;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Identity.Infrastructure.Configuration;
using MestoBeach.Identity.Infrastructure.Persistence;
using MestoBeach.Identity.Infrastructure.Persistence.Repositories;
using MestoBeach.Identity.Infrastructure.Redis;
using MestoBeach.Identity.Infrastructure.Services;
using MestoBeach.Shared.Behaviors;
using StackExchange.Redis;

namespace MestoBeach.Identity.Infrastructure;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        // Options
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        services.Configure<TotpOptions>(config.GetSection(TotpOptions.SectionName));
        services.Configure<GoogleAuthOptions>(config.GetSection(GoogleAuthOptions.SectionName));
        services.Configure<FrontendOptions>(config.GetSection(FrontendOptions.SectionName));
        services.Configure<PlatformAuthOptions>(config.GetSection(PlatformAuthOptions.SectionName));

        // DbContext
        services.AddDbContext<IdentityDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("Postgres"),
                b => b.MigrationsAssembly("MestoBeach.Identity.Infrastructure")));

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserMembershipRepository, UserMembershipRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<MestoBeach.Shared.Plans.IPlanEntitlementsReader, Services.PlanEntitlementsReader>();
        services.AddScoped<ICustomerAccountRepository, CustomerAccountRepository>();
        services.AddScoped<IPlatformUserRepository, PlatformUserRepository>();
        services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();

        // Lectura cruzada para validar las asignaciones (SDD-001 v2.0): sectores viven en
        // Sessions, estaciones y viajes en Orders. Ver AssignmentReadModels.cs.
        services.AddScoped<IFloorReadService, FloorReadService>();
        services.AddScoped<IStationReadService, StationReadService>();
        services.AddScoped<IDeliveryTripReadService, DeliveryTripReadService>();

        // Modo del local (ADR-036, SDD-001 §7). Implementación única del proveedor, Scoped.
        services.AddScoped<MestoBeach.Shared.Venue.IVenueModeProvider, Venue.VenueModeProvider>();
        services.AddScoped<MestoBeach.Identity.Application.Venue.IVenueStore, Venue.VenueStore>();
        services.AddScoped<MestoBeach.Identity.Application.Venue.IVenueModeCache, Venue.VenueModeCache>();

        // Services
        services.AddSingleton<IJwtService, JwtService>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddSingleton<IPasswordGenerator, PasswordGenerator>();
        services.AddScoped<ITotpService, TotpService>();
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();

        // Redis services (scoped because they use IDatabase which is thread-safe but scoped is cleaner)
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IJwtBlacklistService, JwtBlacklistService>();
        services.AddScoped<ILoginAttemptService, LoginAttemptService>();
        services.AddScoped<IGlobalInvalidationService, GlobalInvalidationService>();
        services.AddScoped<IActivationTokenService, ActivationTokenService>();
        services.AddScoped<IPasswordResetTokenService, PasswordResetTokenService>();
        services.AddScoped<IDeviceSessionService, DeviceSessionService>();
        services.AddScoped<IImpersonationSessionService, ImpersonationSessionService>();
        // Estación del runner leída de la base en cada petición (SDD-001 v2.1 §7, ADR-038).
        services.AddScoped<IStaffStationResolver, StaffStationResolver>();

        // Audit + URL helpers
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IFrontendUrlService, FrontendUrlService>();
        services.AddScoped<IEmailNotificationService, EmailNotificationService>();

        // MediatR handlers from Application layer
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(
                typeof(MestoBeach.Identity.Application.Auth.Staff.Commands.LoginStaffCommand).Assembly));

        // FluentValidation validators + validation pipeline behavior
        services.AddModuleValidation(
            typeof(MestoBeach.Identity.Application.Auth.Staff.Commands.LoginStaffCommand).Assembly);

        return services;
    }
}
