using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MestoBeach.Sessions.Domain.Entities;
using MestoBeach.Sessions.Domain.Enums;
using MestoBeach.Sessions.Infrastructure.Persistence;
using System.Text.Json;

namespace MestoBeach.Sessions.Infrastructure.Persistence.Configurations;

public sealed class DiningTableConfiguration : IEntityTypeConfiguration<DiningTable>
{
    public void Configure(EntityTypeBuilder<DiningTable> b)
    {
        b.ToTable("tables");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.TenantId).HasColumnName("tenant_id");
        b.Property(t => t.TableCode).HasColumnName("table_code").HasMaxLength(50).IsRequired();
        b.Property(t => t.Zone).HasColumnName("zone").HasMaxLength(100);
        b.Property(t => t.Active).HasColumnName("active");

        // Map layout (nullable)
        b.Property(t => t.FloorId).HasColumnName("floor_id");
        b.Property(t => t.PosX).HasColumnName("pos_x");
        b.Property(t => t.PosY).HasColumnName("pos_y");
        b.Property(t => t.Seats).HasColumnName("seats");
        b.Property(t => t.Shape).HasColumnName("shape").HasMaxLength(16);
        // PRD-019: clase de puesto (TABLE | HIGH_TABLE | RESERVADO).
        b.Property(t => t.Kind).HasColumnName("kind").HasMaxLength(16).IsRequired().HasDefaultValue(DiningTable.KindTable);

        b.Property(t => t.QrMode).HasColumnName("qr_mode").HasMaxLength(16).HasDefaultValue(QrMode.Fixed)
            .HasConversion(v => v.ToString().ToUpperInvariant(), v => Enum.Parse<QrMode>(v, true));
        b.Property(t => t.RequiresPin).HasColumnName("requires_pin").HasDefaultValue(true);

        b.Property(t => t.CreatedAt).HasColumnName("created_at");
        b.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        b.HasIndex(t => new { t.TenantId, t.TableCode }).IsUnique();
        b.HasIndex(t => t.FloorId);
    }
}

public sealed class FloorConfiguration : IEntityTypeConfiguration<Floor>
{
    private static readonly JsonSerializerOptions JsonOpts = new();

    public void Configure(EntityTypeBuilder<Floor> b)
    {
        b.ToTable("floors");
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).HasColumnName("id");
        b.Property(f => f.TenantId).HasColumnName("tenant_id");
        b.Property(f => f.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        b.Property(f => f.SortOrder).HasColumnName("sort_order");
        b.Property(f => f.Width).HasColumnName("width");
        b.Property(f => f.Height).HasColumnName("height");
        b.Property(f => f.Walls)
            .HasColumnName("walls")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<List<WallSegment>>(v, JsonOpts) ?? new List<WallSegment>())
            .Metadata.SetValueComparer(new ValueComparer<List<WallSegment>>(
                (a, c) => a != null && c != null && a.SequenceEqual(c),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        // PRD-019: modos en los que el piso existe. El default de columna cubre las filas existentes.
        b.Property(f => f.VisibleIn)
            .HasColumnName("visible_in")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValueSql("'[\"RESTAURANT\",\"NIGHT\"]'::jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOpts) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, c) => a != null && c != null && a.SequenceEqual(c),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        b.Property(f => f.CreatedAt).HasColumnName("created_at");
        b.Property(f => f.UpdatedAt).HasColumnName("updated_at");

        b.HasIndex(f => f.TenantId);
    }
}

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> b)
    {
        b.ToTable("sessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.TableId).HasColumnName("table_id");
        b.Property(s => s.TableCode).HasColumnName("table_code").HasMaxLength(50).IsRequired();
        b.Property(s => s.Status).HasColumnName("status")
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<SessionStatus>(v, true));
        b.Property(s => s.QrMode).HasColumnName("qr_mode")
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<QrMode>(v, true));
        b.Property(s => s.ServiceMode).HasColumnName("service_mode")
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<ServiceMode>(v, true));
        b.Property(s => s.PinRequired).HasColumnName("pin_required");
        b.Property(s => s.WaiterId).HasColumnName("waiter_id");
        b.Property(s => s.CreatedAt).HasColumnName("created_at");
        b.Property(s => s.OpenedBy).HasColumnName("opened_by");
        b.Property(s => s.ClosedAt).HasColumnName("closed_at");
        b.Property(s => s.ClosedBy).HasColumnName("closed_by");
        b.Property(s => s.ShiftId).HasColumnName("shift_id");
        b.Property(s => s.GuestCount).HasColumnName("guest_count");
        // PRD-019 — ADR-040: snapshot del modo del local al abrir (AddSessionVenueMode).
        b.Property(s => s.VenueMode).HasColumnName("venue_mode")
            .HasMaxLength(16).IsRequired()
            .HasDefaultValue(MestoBeach.Shared.Venue.VenueMode.RESTAURANT)
            .HasSentinel((MestoBeach.Shared.Venue.VenueMode)(-1))
            .HasConversion(v => v.ToString(), v => Enum.Parse<MestoBeach.Shared.Venue.VenueMode>(v, true));

        b.HasIndex(s => new { s.TenantId, s.TableId });
    }
}

public sealed class SessionParticipantConfiguration : IEntityTypeConfiguration<SessionParticipant>
{
    public void Configure(EntityTypeBuilder<SessionParticipant> b)
    {
        b.ToTable("session_participants");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.SessionId).HasColumnName("session_id");
        b.Property(p => p.DeviceId).HasColumnName("device_id").HasMaxLength(100).IsRequired();
        b.Property(p => p.CustomerAccountId).HasColumnName("customer_account_id");
        b.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(120);
        b.Property(p => p.IsHost).HasColumnName("is_host");
        b.Property(p => p.JoinedAt).HasColumnName("joined_at");
        b.Property(p => p.PinValidated).HasColumnName("pin_validated");
        b.Property(p => p.InviteToken).HasColumnName("invite_token");

        // One participant per (session, device). Unique → atomic get-or-create is race-safe
        // (the same invite link opened in several tabs collapses to one participant).
        b.HasIndex(p => new { p.SessionId, p.DeviceId }).IsUnique();
    }
}

// ─── Read-only projections from Identity tables ───────────────────────────────

public sealed class TenantReadModelConfiguration : IEntityTypeConfiguration<TenantReadModel>
{
    public void Configure(EntityTypeBuilder<TenantReadModel> b)
    {
        b.ToTable("tenants", t => t.ExcludeFromMigrations());
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(100);
        b.Property(t => t.PinRequired).HasColumnName("pin_required");
    }
}

public sealed class StaffReadModelConfiguration : IEntityTypeConfiguration<StaffReadModel>
{
    public void Configure(EntityTypeBuilder<StaffReadModel> b)
    {
        // Reads the global identity table `users` (replaced `staff` in the unified-identity change).
        b.ToTable("users", t => t.ExcludeFromMigrations());
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.Name).HasColumnName("name").HasMaxLength(200);
    }
}

public sealed class ShiftReadModelConfiguration : IEntityTypeConfiguration<ShiftReadModel>
{
    public void Configure(EntityTypeBuilder<ShiftReadModel> b)
    {
        b.ToTable("shifts", t => t.ExcludeFromMigrations());
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.Status).HasColumnName("status").HasMaxLength(20);
    }
}

// ─── table_layouts (PRD-019, ADR-038): posición de una mesa por modo ─────────────

public sealed class TableLayoutConfiguration : IEntityTypeConfiguration<TableLayout>
{
    public void Configure(EntityTypeBuilder<TableLayout> b)
    {
        b.ToTable("table_layouts");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(l => l.TenantId).HasColumnName("tenant_id");
        b.Property(l => l.TableId).HasColumnName("table_id");
        b.Property(l => l.Mode).HasColumnName("mode").HasMaxLength(16).IsRequired();
        b.Property(l => l.FloorId).HasColumnName("floor_id");
        b.Property(l => l.PosX).HasColumnName("pos_x");
        b.Property(l => l.PosY).HasColumnName("pos_y");
        // Sin HasDefaultValue: un `false` explícito no debe convertirse en el default (sentinel).
        b.Property(l => l.Active).HasColumnName("active");
        // Sin FK: kds_stations la posee Orders.
        b.Property(l => l.StationId).HasColumnName("station_id");
        b.Property(l => l.UpdatedAt).HasColumnName("updated_at");

        b.HasOne<DiningTable>().WithMany().HasForeignKey(l => l.TableId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Floor>().WithMany().HasForeignKey(l => l.FloorId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(l => new { l.TableId, l.Mode }).IsUnique();
        b.HasIndex(l => new { l.TenantId, l.Mode });
    }
}

public sealed class KdsStationReadModelConfiguration : IEntityTypeConfiguration<KdsStationReadModel>
{
    private static readonly JsonSerializerOptions JsonOpts = new();

    public void Configure(EntityTypeBuilder<KdsStationReadModel> b)
    {
        b.ToTable("kds_stations", t => t.ExcludeFromMigrations());
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.Name).HasColumnName("name").HasMaxLength(100);
        b.Property(s => s.FlowProfile).HasColumnName("flow_profile").HasMaxLength(16);
        b.Property(s => s.IsActive).HasColumnName("is_active");
        b.Property(s => s.ActiveIn)
            .HasColumnName("active_in")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOpts) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, c) => a != null && c != null && a.SequenceEqual(c),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
    }
}
