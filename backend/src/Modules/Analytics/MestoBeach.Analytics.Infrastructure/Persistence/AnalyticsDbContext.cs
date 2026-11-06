using Microsoft.EntityFrameworkCore;

namespace MestoBeach.Analytics.Infrastructure.Persistence;

public sealed class AnalyticsDbContext : DbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : base(options) { }

    // ── All sets are read-only cross-module projections (no migrations) ─────────
    public DbSet<SessionProjection> Sessions => Set<SessionProjection>();
    public DbSet<OrderProjection> Orders => Set<OrderProjection>();
    public DbSet<OrderItemProjection> OrderItems => Set<OrderItemProjection>();
    public DbSet<PaymentProjection> Payments => Set<PaymentProjection>();
    public DbSet<KdsStationProjection> KdsStations => Set<KdsStationProjection>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(typeof(AnalyticsDbContext).Assembly);
    }
}

// ── Cross-module read models ──────────────────────────────────────────────────

public sealed class SessionProjection
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public sealed class OrderProjection
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid TenantId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public Guid? StationId { get; set; }
}

public sealed class OrderItemProjection
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string CatalogItemId { get; set; } = string.Empty;
    public string ItemNameSnapshot { get; set; } = string.Empty;
    public int ItemPriceSnapshotCents { get; set; }
    public int Quantity { get; set; }
}

public sealed class PaymentProjection
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SessionId { get; set; }
    public int AmountCents { get; set; }
    public int TipCents { get; set; }
    public int TotalCents { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class KdsStationProjection
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
}
