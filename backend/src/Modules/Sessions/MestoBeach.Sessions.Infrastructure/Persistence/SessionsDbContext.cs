using Microsoft.EntityFrameworkCore;
using MestoBeach.Sessions.Domain.Entities;

namespace MestoBeach.Sessions.Infrastructure.Persistence;

public sealed class SessionsDbContext : DbContext
{
    public SessionsDbContext(DbContextOptions<SessionsDbContext> options) : base(options) { }

    public DbSet<DiningTable> Tables => Set<DiningTable>();
    public DbSet<Floor> Floors => Set<Floor>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionParticipant> Participants => Set<SessionParticipant>();
    public DbSet<TableLayout> TableLayouts => Set<TableLayout>();

    // Read-only projection of kds_stations (owned by Orders): validates the bar of a table (ADR-038).
    public DbSet<KdsStationReadModel> KdsStations => Set<KdsStationReadModel>();

    // Read-only projections from Identity tables (shared DB)
    public DbSet<TenantReadModel> Tenants => Set<TenantReadModel>();
    public DbSet<StaffReadModel> Staff => Set<StaffReadModel>();
    // Read-only projection of the shifts table owned by Payments module
    public DbSet<ShiftReadModel> Shifts => Set<ShiftReadModel>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(typeof(SessionsDbContext).Assembly);
    }
}

/// <summary>Read-only projection of the <c>tenants</c> table owned by Identity module.</summary>
public sealed class TenantReadModel
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public bool PinRequired { get; set; }
}

/// <summary>Read-only projection of the <c>users</c> table (global staff identity) owned by Identity module.</summary>
public sealed class StaffReadModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Read-only projection of the <c>shifts</c> table owned by Payments module.</summary>
public sealed class ShiftReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Proyección de solo lectura de <c>kds_stations</c> (propiedad de Orders). Sessions la usa para
/// validar el <c>stationId</c> de <c>PATCH /tables/layout</c> (perfil BAR y encendida en el modo).
/// </summary>
public sealed class KdsStationReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FlowProfile { get; set; } = "FULL_PASS";
    public bool IsActive { get; set; }
    public List<string> ActiveIn { get; set; } = new();
}
