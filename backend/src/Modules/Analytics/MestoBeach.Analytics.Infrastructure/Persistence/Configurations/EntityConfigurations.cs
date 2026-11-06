using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MestoBeach.Analytics.Infrastructure.Persistence.Configurations;

public sealed class SessionProjectionConfiguration : IEntityTypeConfiguration<SessionProjection>
{
    public void Configure(EntityTypeBuilder<SessionProjection> b)
    {
        b.ToTable("sessions", t => t.ExcludeFromMigrations());
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.Status).HasColumnName("status").HasMaxLength(20);
        b.Property(s => s.CreatedAt).HasColumnName("created_at");
        b.Property(s => s.ClosedAt).HasColumnName("closed_at");
    }
}

public sealed class OrderProjectionConfiguration : IEntityTypeConfiguration<OrderProjection>
{
    public void Configure(EntityTypeBuilder<OrderProjection> b)
    {
        b.ToTable("orders", t => t.ExcludeFromMigrations());
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).HasColumnName("id");
        b.Property(o => o.SessionId).HasColumnName("session_id");
        b.Property(o => o.TenantId).HasColumnName("tenant_id");
        b.Property(o => o.Status).HasColumnName("status").HasMaxLength(20);
        b.Property(o => o.CreatedAt).HasColumnName("created_at");
        b.Property(o => o.ConfirmedAt).HasColumnName("confirmed_at");
        b.Property(o => o.ReadyAt).HasColumnName("ready_at");
        b.Property(o => o.StationId).HasColumnName("station_id");
    }
}

public sealed class OrderItemProjectionConfiguration : IEntityTypeConfiguration<OrderItemProjection>
{
    public void Configure(EntityTypeBuilder<OrderItemProjection> b)
    {
        b.ToTable("order_items", t => t.ExcludeFromMigrations());
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).HasColumnName("id");
        b.Property(i => i.OrderId).HasColumnName("order_id");
        b.Property(i => i.CatalogItemId).HasColumnName("catalog_item_id").HasMaxLength(24);
        b.Property(i => i.ItemNameSnapshot).HasColumnName("item_name_snapshot").HasMaxLength(200);
        b.Property(i => i.ItemPriceSnapshotCents).HasColumnName("item_price_snapshot_cents");
        b.Property(i => i.Quantity).HasColumnName("quantity");
    }
}

public sealed class PaymentProjectionConfiguration : IEntityTypeConfiguration<PaymentProjection>
{
    public void Configure(EntityTypeBuilder<PaymentProjection> b)
    {
        b.ToTable("payments", t => t.ExcludeFromMigrations());
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.TenantId).HasColumnName("tenant_id");
        b.Property(p => p.SessionId).HasColumnName("session_id");
        b.Property(p => p.AmountCents).HasColumnName("amount_cents");
        b.Property(p => p.TipCents).HasColumnName("tip_cents");
        b.Property(p => p.TotalCents).HasColumnName("total_cents");
        b.Property(p => p.Status).HasColumnName("status").HasMaxLength(20);
        b.Property(p => p.CreatedAt).HasColumnName("created_at");
        b.Property(p => p.CompletedAt).HasColumnName("completed_at");
    }
}

public sealed class KdsStationProjectionConfiguration : IEntityTypeConfiguration<KdsStationProjection>
{
    public void Configure(EntityTypeBuilder<KdsStationProjection> b)
    {
        b.ToTable("kds_stations", t => t.ExcludeFromMigrations());
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.Name).HasColumnName("name").HasMaxLength(100);
    }
}
