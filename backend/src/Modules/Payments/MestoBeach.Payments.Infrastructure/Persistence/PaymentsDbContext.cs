using MestoBeach.Payments.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MestoBeach.Payments.Infrastructure.Persistence;

public sealed class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : base(options) { }

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<TenantPaymentConfig> PaymentConfigs => Set<TenantPaymentConfig>();
    public DbSet<Shift> Shifts => Set<Shift>();

    // ── Cross-module read projections (ExcludeFromMigrations) ─────────────────
    public DbSet<SessionProjection> Sessions => Set<SessionProjection>();
    public DbSet<OrderProjection> Orders => Set<OrderProjection>();
    public DbSet<OrderItemProjection> OrderItems => Set<OrderItemProjection>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
    }
}

// ── Cross-module read models ──────────────────────────────────────────────────

public sealed class SessionProjection
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string TableCode { get; set; } = string.Empty;
}

public sealed class OrderProjection
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid TenantId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class OrderItemProjection
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public int ItemPriceSnapshotCents { get; set; }
    public int Quantity { get; set; }
}
