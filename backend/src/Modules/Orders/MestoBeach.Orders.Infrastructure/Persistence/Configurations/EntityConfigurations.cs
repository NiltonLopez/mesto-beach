using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Enums;
using MestoBeach.Orders.Infrastructure.Persistence;
using System.Text.Json;

namespace MestoBeach.Orders.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).HasColumnName("id");
        b.Property(o => o.SessionId).HasColumnName("session_id");
        b.Property(o => o.TenantId).HasColumnName("tenant_id");
        b.Property(o => o.ParticipantId).HasColumnName("participant_id");
        b.Property(o => o.CustomerAccountId).HasColumnName("customer_account_id");
        b.Property(o => o.CustomerName).HasColumnName("customer_name").HasMaxLength(120);
        b.Property(o => o.Observacion).HasColumnName("observacion").HasMaxLength(500);
        // PRD-019 / ADR-038: snapshots al crear (migración AddOrderVenueModeAndPriority).
        b.Property(o => o.Priority).HasColumnName("priority").IsRequired().HasDefaultValue(0);
        b.Property(o => o.VenueMode).HasColumnName("venue_mode").HasMaxLength(16).IsRequired()
            .HasDefaultValue("RESTAURANT");
        b.Property(o => o.Status).HasColumnName("status")
            .HasConversion(
                v => v == OrderStatus.InPreparation ? "IN_PREPARATION" : v.ToString().ToUpperInvariant(),
                v => v == "IN_PREPARATION" ? OrderStatus.InPreparation : Enum.Parse<OrderStatus>(v, true));
#pragma warning disable CS0618 // la columna sigue existiendo mientras el KDS razone por pedido
        b.Property(o => o.StationId).HasColumnName("station_id");
#pragma warning restore CS0618
        b.Property(o => o.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(300);
        b.Property(o => o.CancelledBy).HasColumnName("cancelled_by");
        b.Property(o => o.CancelledAt).HasColumnName("cancelled_at");
        b.Property(o => o.CreatedAt).HasColumnName("created_at");
        b.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        b.Property(o => o.ConfirmedAt).HasColumnName("confirmed_at");
        b.Property(o => o.ReadyAt).HasColumnName("ready_at");

        b.HasMany(o => o.Items).WithOne().HasForeignKey("OrderId");
        b.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Property);

        b.HasIndex(o => o.SessionId);
        b.HasIndex(o => new { o.TenantId, o.SessionId });
        b.HasIndex(o => new { o.TenantId, o.UpdatedAt }); // KDS delta scan (P2)
        b.HasIndex(o => new { o.TenantId, o.VenueMode, o.CreatedAt }); // cortes por modo (SDD-006)
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).HasColumnName("id");
        b.Property(i => i.OrderId).HasColumnName("order_id");
        // Catalog item ids are GUIDs (36 chars), not 24-char Mongo ObjectIds.
        b.Property(i => i.CatalogItemId).HasColumnName("catalog_item_id").HasMaxLength(36).IsRequired();
        b.Property(i => i.ItemNameSnapshot).HasColumnName("item_name_snapshot").HasMaxLength(200).IsRequired();
        b.Property(i => i.ItemPriceSnapshotCents).HasColumnName("item_price_snapshot_cents");
        b.Property(i => i.Quantity).HasColumnName("quantity");
        b.Property(i => i.Notes).HasColumnName("notes").HasMaxLength(300);
        b.Property(i => i.LoyaltyRedeemed).HasColumnName("loyalty_redeemed");
        b.Property(i => i.SelectedModifiers).HasColumnName("selected_modifiers").HasMaxLength(500);

        // ── Ciclo de vida por ítem (ADR-025) ──────────────────────────────────
        b.Property(i => i.TenantId).HasColumnName("tenant_id");
        b.Property(i => i.Status).HasColumnName("status").HasMaxLength(20)
            .HasConversion(
                v => v == OrderItemStatus.InPreparation ? "IN_PREPARATION"
                   : v == OrderItemStatus.InDelivery ? "IN_DELIVERY"
                   : v.ToString().ToUpperInvariant(),
                v => v == "IN_PREPARATION" ? OrderItemStatus.InPreparation
                   : v == "IN_DELIVERY" ? OrderItemStatus.InDelivery
                   : Enum.Parse<OrderItemStatus>(v, true));
        b.Property(i => i.Timing).HasColumnName("timing").HasMaxLength(10)
            .HasConversion(
                v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<ItemTiming>(v, true));
        b.Property(i => i.StationId).HasColumnName("station_id");
        b.Property(i => i.MarchedAt).HasColumnName("marched_at");
        b.Property(i => i.MarchedBy).HasColumnName("marched_by");
        // Sellos por estado: el KDS muestra cuánto llevó el plato en cada paso.
        b.Property(i => i.QueuedAt).HasColumnName("queued_at");
        b.Property(i => i.PreparationStartedAt).HasColumnName("preparation_started_at");
        b.Property(i => i.PreparationOrderedBy).HasColumnName("preparation_ordered_by");
        b.Property(i => i.ReadyAt).HasColumnName("ready_at");
        b.Property(i => i.DeliveryTripId).HasColumnName("delivery_trip_id");
        b.Property(i => i.DeliveredAt).HasColumnName("delivered_at");
        b.Property(i => i.UpdatedAt).HasColumnName("updated_at");

        b.HasIndex(i => i.OrderId);
        // Cola de la estación y tablero por tenant: se consultan por estado sin unir con orders.
        b.HasIndex(i => new { i.TenantId, i.Status });
        b.HasIndex(i => new { i.TenantId, i.StationId, i.Status });
        // Cursor del delta del KDS a nivel de ítem (ADR-022 llevado al plato).
        b.HasIndex(i => new { i.TenantId, i.UpdatedAt });
        // Manifiesto del viaje: los platos que carga un runner.
        b.HasIndex(i => i.DeliveryTripId);
    }
}

public sealed class DeliveryTripConfiguration : IEntityTypeConfiguration<DeliveryTrip>
{
    public void Configure(EntityTypeBuilder<DeliveryTrip> b)
    {
        b.ToTable("delivery_trips");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.TenantId).HasColumnName("tenant_id");
        b.Property(t => t.RunnerId).HasColumnName("runner_id");
        b.Property(t => t.StationId).HasColumnName("station_id");
        b.Property(t => t.Status).HasColumnName("status").HasMaxLength(20)
            .HasConversion(
                v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<DeliveryTripStatus>(v, true));
        b.Property(t => t.CreatedAt).HasColumnName("created_at");
        b.Property(t => t.CompletedAt).HasColumnName("completed_at");

        // Un solo viaje abierto por runner (ADR-027). El filtro compara contra el literal que
        // realmente se persiste ('OPEN', igual que orders.status): si no coincidiera, el índice
        // no filtraría nada y la restricción sería decorativa.
        b.HasIndex(t => t.RunnerId)
            .IsUnique()
            .HasFilter("\"status\" = 'OPEN'")
            .HasDatabaseName("ux_delivery_trips_runner_open");
    }
}

public sealed class KdsStationConfiguration : IEntityTypeConfiguration<KdsStation>
{
    private static readonly JsonSerializerOptions JsonOpts = new();

    public void Configure(EntityTypeBuilder<KdsStation> b)
    {
        b.ToTable("kds_stations");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(s => s.CategoryIds)
            .HasColumnName("category_ids")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOpts) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        b.Property(s => s.CreatedAt).HasColumnName("created_at");
        b.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        // ADR-037: perfil de flujo y encendido por modo. Los DEFAULT de columna viven solo en la
        // migración (AddKdsStationProfileAndModes) para las filas existentes; el modelo no los
        // declara porque un HasDefaultValue(true) en un bool haría que EF omitiera un `false`
        // explícito al insertar (sentinel) y la estación naciera encendida.
        b.Property(s => s.IsActive).HasColumnName("is_active");
        b.Property(s => s.FlowProfile).HasColumnName("flow_profile").HasMaxLength(16).IsRequired();
        b.Property(s => s.ActiveIn)
            .HasColumnName("active_in")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOpts) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));

        b.HasIndex(s => s.TenantId);
    }
}

// ─── Cross-module read models ─────────────────────────────────────────────────

public sealed class SessionReadModelConfiguration : IEntityTypeConfiguration<SessionReadModel>
{
    public void Configure(EntityTypeBuilder<SessionReadModel> b)
    {
        b.ToTable("sessions", t => t.ExcludeFromMigrations());
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.TableCode).HasColumnName("table_code").HasMaxLength(50);
        b.Property(s => s.TableId).HasColumnName("table_id");
        b.Property(s => s.Status).HasColumnName("status").HasMaxLength(20);
        b.Property(s => s.PinRequired).HasColumnName("pin_required");
    }
}

public sealed class SessionParticipantReadModelConfiguration
    : IEntityTypeConfiguration<SessionParticipantReadModel>
{
    public void Configure(EntityTypeBuilder<SessionParticipantReadModel> b)
    {
        b.ToTable("session_participants", t => t.ExcludeFromMigrations());
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.SessionId).HasColumnName("session_id");
        b.Property(p => p.DeviceId).HasColumnName("device_id").HasMaxLength(100);
        b.Property(p => p.CustomerAccountId).HasColumnName("customer_account_id");
        b.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(120);
        b.Property(p => p.PinValidated).HasColumnName("pin_validated");
    }
}
