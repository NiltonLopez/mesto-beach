using Microsoft.EntityFrameworkCore;
using MestoBeach.Orders.Domain.Entities;

namespace MestoBeach.Orders.Infrastructure.Persistence;

public sealed class OrdersDbContext : DbContext
{
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<KdsStation> KdsStations => Set<KdsStation>();
    /// <summary>Viajes de entrega de los runners (ADR-027).</summary>
    public DbSet<DeliveryTrip> DeliveryTrips => Set<DeliveryTrip>();

    // Read-only cross-module projections (Identity)
    public DbSet<SessionReadModel> Sessions => Set<SessionReadModel>();
    public DbSet<SessionParticipantReadModel> Participants => Set<SessionParticipantReadModel>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
    }
}

public sealed class SessionReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    /// <summary>PRD-019: mesa de la sesión; lleva a table_layouts del modo activo (coordenadas y barra).</summary>
    public Guid TableId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool PinRequired { get; set; }
}

public sealed class SessionParticipantReadModel
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public Guid? CustomerAccountId { get; set; }
    public string? DisplayName { get; set; }
    /// <summary>session_participants.pin_validated — la verdad del PIN (ADR-040). Solo lectura.</summary>
    public bool PinValidated { get; set; }
}
