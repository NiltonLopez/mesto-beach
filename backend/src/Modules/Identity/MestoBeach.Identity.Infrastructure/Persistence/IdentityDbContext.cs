using Microsoft.EntityFrameworkCore;
using MestoBeach.Identity.Domain.Entities;

namespace MestoBeach.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserMembership> Memberships => Set<UserMembership>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<PlatformUserRole> PlatformUserRoles => Set<PlatformUserRole>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    // Proyecciones de solo lectura hacia otros módulos, para validar las asignaciones de
    // Mesto Beach (SDD-001 v2.0). Ver AssignmentReadModels.cs.
    public DbSet<FloorReadModel> Floors => Set<FloorReadModel>();
    public DbSet<KdsStationReadModel> KdsStations => Set<KdsStationReadModel>();
    public DbSet<DeliveryTripReadModel> DeliveryTrips => Set<DeliveryTripReadModel>();

    // Modo del local (ADR-036, SDD-001 §7). Ver VenueReadModels.cs.
    public DbSet<VenueModeChange> VenueModeChanges => Set<VenueModeChange>();
    public DbSet<SessionReadModel> Sessions => Set<SessionReadModel>();
    public DbSet<OrderReadModel> Orders => Set<OrderReadModel>();
    public DbSet<OrderItemReadModel> OrderItems => Set<OrderItemReadModel>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
