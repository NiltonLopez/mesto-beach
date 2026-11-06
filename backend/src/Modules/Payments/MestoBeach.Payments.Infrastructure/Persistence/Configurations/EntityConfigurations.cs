using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MestoBeach.Payments.Domain.Entities;
using MestoBeach.Payments.Domain.Enums;
using MestoBeach.Payments.Infrastructure.Persistence;
using System.Text.Json;

namespace MestoBeach.Payments.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.TenantId).HasColumnName("tenant_id");
        b.Property(p => p.SessionId).HasColumnName("session_id");
        b.Property(p => p.Method).HasColumnName("method")
            .HasConversion(v => v.ToString(), v => Enum.Parse<PaymentMethod>(v));
        b.Property(p => p.AmountCents).HasColumnName("amount_cents");
        b.Property(p => p.TipCents).HasColumnName("tip_cents").HasDefaultValue(0);
        b.Property(p => p.TotalCents).HasColumnName("total_cents");
        b.Property(p => p.CommissionCents).HasColumnName("commission_cents").HasDefaultValue(0);
        b.Property(p => p.GatewayReference).HasColumnName("gateway_reference").HasMaxLength(200);
        b.Property(p => p.GatewayType).HasColumnName("gateway_type").HasMaxLength(30);
        b.Property(p => p.Status).HasColumnName("status")
            .HasConversion(v => v.ToString(), v => Enum.Parse<PaymentStatus>(v));
        b.Property(p => p.SplitIndex).HasColumnName("split_index");
        b.Property(p => p.SplitTotal).HasColumnName("split_total");
        b.Property(p => p.PayerLabel).HasColumnName("payer_label").HasMaxLength(120);
        b.Property(p => p.ProcessedBy).HasColumnName("processed_by");
        b.Property(p => p.CreatedAt).HasColumnName("created_at");
        b.Property(p => p.CompletedAt).HasColumnName("completed_at");

        b.Property(p => p.ShiftId).HasColumnName("shift_id");

        b.HasIndex(p => p.SessionId);
        b.HasIndex(p => new { p.TenantId, p.CreatedAt });
        b.HasIndex(p => p.ShiftId);
        b.HasIndex(p => new { p.SessionId, p.SplitIndex })
            .IsUnique()
            .HasFilter("split_index IS NOT NULL");
    }
}

public sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> b)
    {
        b.ToTable("shifts");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.Status).HasColumnName("status")
            .HasConversion(v => v.ToString(), v => Enum.Parse<ShiftStatus>(v));
        b.Property(s => s.OpeningFloatCents).HasColumnName("opening_float_cents").HasDefaultValue(0);
        b.Property(s => s.OpenedBy).HasColumnName("opened_by");
        b.Property(s => s.OpenedAt).HasColumnName("opened_at");
        b.Property(s => s.ClosedBy).HasColumnName("closed_by");
        b.Property(s => s.ClosedAt).HasColumnName("closed_at");
        b.Property(s => s.Note).HasColumnName("note").HasMaxLength(500);
        b.HasIndex(s => new { s.TenantId, s.Status });
    }
}

public sealed class TenantPaymentConfigConfiguration : IEntityTypeConfiguration<TenantPaymentConfig>
{
    private static readonly JsonSerializerOptions JsonOpts = new();

    public void Configure(EntityTypeBuilder<TenantPaymentConfig> b)
    {
        b.ToTable("tenant_payment_configs");
        b.HasKey(c => c.TenantId);
        b.Property(c => c.TenantId).HasColumnName("tenant_id");
        b.Property(c => c.DigitalPaymentsEnabled).HasColumnName("digital_payments_enabled").HasDefaultValue(false);
        b.Property(c => c.GatewayType).HasColumnName("gateway_type").HasMaxLength(20);
        b.Property(c => c.GatewayPublicKey).HasColumnName("gateway_public_key").HasMaxLength(300);
        b.Property(c => c.GatewayPrivateKeyRef).HasColumnName("gateway_private_key_ref").HasMaxLength(200);
        b.Property(c => c.TipEnabled).HasColumnName("tip_enabled").HasDefaultValue(true);
        b.Property(c => c.TipOptions)
            .HasColumnName("tip_options")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<List<int>>(v, JsonOpts) ?? new List<int>())
            .Metadata.SetValueComparer(new ValueComparer<List<int>>(
                (a, b2) => a != null && b2 != null && a.SequenceEqual(b2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v)),
                c => c.ToList()));
        b.Property(c => c.CommissionPct).HasColumnName("commission_pct")
            .HasColumnType("decimal(5,2)").HasDefaultValue(3.5m);
        b.Property(c => c.UpdatedAt).HasColumnName("updated_at");
    }
}

// ── Cross-module projections ──────────────────────────────────────────────────

public sealed class SessionProjectionConfiguration : IEntityTypeConfiguration<SessionProjection>
{
    public void Configure(EntityTypeBuilder<SessionProjection> b)
    {
        b.ToTable("sessions", t => t.ExcludeFromMigrations());
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.Status).HasColumnName("status").HasMaxLength(20);
        b.Property(s => s.TableCode).HasColumnName("table_code").HasMaxLength(50);
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
        b.Property(i => i.ItemPriceSnapshotCents).HasColumnName("item_price_snapshot_cents");
        b.Property(i => i.Quantity).HasColumnName("quantity");
    }
}
