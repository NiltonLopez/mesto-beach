using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MestoBeach.Identity.Infrastructure.Persistence;

// ══════════════════════════════════════════════════════════════════════════════
// PROYECCIONES DE SOLO LECTURA HACIA OTROS MÓDULOS
//
// Para validar una asignación hay que responder a tres preguntas que Identity no sabe
// contestar sola: ¿existe ese sector?, ¿existe esa estación?, ¿tiene el runner un viaje sin
// cerrar? Las tres viven en Sessions y en Orders.
//
// Se sigue el patrón que ya usa el repositorio para el mismo problema —Orders lee `sessions` y
// `tables` con SessionReadModel/TableLayoutReadModel, Sessions lee `users` y `tenants`—: una
// proyección mínima, de solo lectura, detrás de una interfaz de la capa Application. No hay
// referencia a los proyectos de Sessions ni de Orders, no se toca su dominio y ninguna de estas
// tablas entra en las migraciones de Identity: las crea y las mantiene su módulo dueño.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Sector del local. Tabla <c>floors</c>, propiedad del módulo Sessions.</summary>
public sealed class FloorReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Estación de preparación. Tabla <c>kds_stations</c>, propiedad del módulo Orders.</summary>
public sealed class KdsStationReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    // ADR-037 (PR3): para el stations[] de GET /api/v1/venue.
    public bool IsActive { get; set; } = true;
    public string FlowProfile { get; set; } = "FULL_PASS";
    public List<string> ActiveIn { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Viaje de entrega de un runner (ADR-027). Tabla <c>delivery_trips</c>, propiedad de Orders.
/// Aquí solo interesa si queda alguno <c>OPEN</c>: un runner con la bandeja reclamada no puede
/// cambiar de estación sin cerrarla o liberarla antes.
/// </summary>
public sealed class DeliveryTripReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RunnerId { get; set; }
    public Guid StationId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class FloorReadModelConfiguration : IEntityTypeConfiguration<FloorReadModel>
{
    public void Configure(EntityTypeBuilder<FloorReadModel> b)
    {
        b.ToTable("floors", t => t.ExcludeFromMigrations());
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).HasColumnName("id");
        b.Property(f => f.TenantId).HasColumnName("tenant_id");
        b.Property(f => f.Name).HasColumnName("name").HasMaxLength(100);
    }
}

public sealed class KdsStationReadModelConfiguration : IEntityTypeConfiguration<KdsStationReadModel>
{
    public void Configure(EntityTypeBuilder<KdsStationReadModel> b)
    {
        b.ToTable("kds_stations", t => t.ExcludeFromMigrations());
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.TenantId).HasColumnName("tenant_id");
        b.Property(s => s.Name).HasColumnName("name").HasMaxLength(100);
        b.Property(s => s.IsActive).HasColumnName("is_active");
        b.Property(s => s.FlowProfile).HasColumnName("flow_profile").HasMaxLength(16);
        b.Property(s => s.ActiveIn).HasColumnName("active_in").HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                (a, b) => a != null && b != null && a.SequenceEqual(b),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        b.Property(s => s.CreatedAt).HasColumnName("created_at");
    }
}

public sealed class DeliveryTripReadModelConfiguration : IEntityTypeConfiguration<DeliveryTripReadModel>
{
    public void Configure(EntityTypeBuilder<DeliveryTripReadModel> b)
    {
        b.ToTable("delivery_trips", t => t.ExcludeFromMigrations());
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.TenantId).HasColumnName("tenant_id");
        b.Property(t => t.RunnerId).HasColumnName("runner_id");
        b.Property(t => t.StationId).HasColumnName("station_id");
        b.Property(t => t.Status).HasColumnName("status").HasMaxLength(20);
    }
}
