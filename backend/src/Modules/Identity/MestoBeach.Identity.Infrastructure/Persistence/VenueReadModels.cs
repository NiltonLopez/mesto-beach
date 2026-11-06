using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MestoBeach.Identity.Domain.Entities;
using MestoBeach.Shared.Venue;

namespace MestoBeach.Identity.Infrastructure.Persistence;

// ══════════════════════════════════════════════════════════════════════════════
// MODO DEL LOCAL (ADR-036, SDD-001 §7)
//
// Proyecciones de solo lectura para el pre-flight (mismo patrón que AssignmentReadModels.cs):
// sessions (Sessions), orders y order_items (Orders). ExcludeFromMigrations: las crea su dueño.
// Más la tabla propia venue_mode_changes, que sí migra Identity.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Sesión de mesa. Tabla <c>sessions</c>, propiedad de Sessions.</summary>
public sealed class SessionReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string TableCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>Pedido. Tabla <c>orders</c>, propiedad de Orders.</summary>
public sealed class OrderReadModel
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid TenantId { get; set; }
}

/// <summary>Plato de un pedido. Tabla <c>order_items</c>, propiedad de Orders.</summary>
public sealed class OrderItemReadModel
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid TenantId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class SessionReadModelConfiguration : IEntityTypeConfiguration<SessionReadModel>
{
    public void Configure(EntityTypeBuilder<SessionReadModel> b)
    {
        b.ToTable("sessions", t => t.ExcludeFromMigrations());
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.TableCode).HasColumnName("table_code").HasMaxLength(50);
        b.Property(s => s.Status).HasColumnName("status");
    }
}

public sealed class OrderReadModelConfiguration : IEntityTypeConfiguration<OrderReadModel>
{
    public void Configure(EntityTypeBuilder<OrderReadModel> b)
    {
        b.ToTable("orders", t => t.ExcludeFromMigrations());
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).HasColumnName("id");
        b.Property(o => o.SessionId).HasColumnName("session_id");
        b.Property(o => o.TenantId).HasColumnName("tenant_id");
    }
}

public sealed class OrderItemReadModelConfiguration : IEntityTypeConfiguration<OrderItemReadModel>
{
    public void Configure(EntityTypeBuilder<OrderItemReadModel> b)
    {
        b.ToTable("order_items", t => t.ExcludeFromMigrations());
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).HasColumnName("id");
        b.Property(i => i.OrderId).HasColumnName("order_id");
        b.Property(i => i.TenantId).HasColumnName("tenant_id");
        b.Property(i => i.Status).HasColumnName("status").HasMaxLength(20);
    }
}

public sealed class VenueModeChangeConfiguration : IEntityTypeConfiguration<VenueModeChange>
{
    public void Configure(EntityTypeBuilder<VenueModeChange> b)
    {
        b.ToTable("venue_mode_changes");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(c => c.FromMode).HasColumnName("from_mode").HasMaxLength(16).IsRequired()
            .HasConversion(v => v.ToString(), v => Enum.Parse<VenueMode>(v, true));
        b.Property(c => c.ToMode).HasColumnName("to_mode").HasMaxLength(16).IsRequired()
            .HasConversion(v => v.ToString(), v => Enum.Parse<VenueMode>(v, true));
        b.Property(c => c.ChangedBy).HasColumnName("changed_by").IsRequired();
        b.Property(c => c.ChangedAt).HasColumnName("changed_at").IsRequired();
        b.Property(c => c.Preflight).HasColumnName("preflight").HasColumnType("jsonb").IsRequired();

        // FK a tenants; changed_by sin FK a propósito (la auditoría sobrevive a la cuenta).
        b.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(c => new { c.TenantId, c.ChangedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_venue_mode_changes_tenant_changed_at");
    }
}
