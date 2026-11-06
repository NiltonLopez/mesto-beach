using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using MestoBeach.Shared.Venue;

namespace MestoBeach.Orders.Infrastructure.Persistence;

/// <summary>
/// Proyección de solo lectura de <c>table_layouts</c> (propiedad del módulo Sessions; PRD-019,
/// ADR-038): la posición de una mesa <b>por modo</b>. Sirve a las coordenadas del puesto en la cola
/// del runner y en el manifiesto del viaje (ADR-027) y a la barra de la mesa en el enrutado
/// (SDD-004 paso b). Clave <c>(table_id, mode)</c>. Ninguna proyección de Orders mapea ya
/// <c>tables.floor_id/pos_x/pos_y</c> (deprecadas). Excluida de las migraciones de Orders.
/// </summary>
public sealed class TableLayoutReadModel
{
    public Guid TableId { get; set; }
    public string Mode { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid? FloorId { get; set; }
    public double? PosX { get; set; }
    public double? PosY { get; set; }
    public Guid? StationId { get; set; }
    public bool Active { get; set; }
}

public sealed class TableLayoutReadModelConfiguration : IEntityTypeConfiguration<TableLayoutReadModel>
{
    public void Configure(EntityTypeBuilder<TableLayoutReadModel> b)
    {
        b.ToTable("table_layouts", t => t.ExcludeFromMigrations());
        b.HasKey(t => new { t.TableId, t.Mode });
        b.Property(t => t.TableId).HasColumnName("table_id");
        b.Property(t => t.Mode).HasColumnName("mode").HasMaxLength(16);
        b.Property(t => t.TenantId).HasColumnName("tenant_id");
        b.Property(t => t.FloorId).HasColumnName("floor_id");
        b.Property(t => t.PosX).HasColumnName("pos_x");
        b.Property(t => t.PosY).HasColumnName("pos_y");
        b.Property(t => t.StationId).HasColumnName("station_id");
        b.Property(t => t.Active).HasColumnName("active");
    }
}

/// <summary>Lectura de las filas del <b>modo activo</b> para un conjunto de mesas (cola y manifiesto).</summary>
internal static class TableLayoutLookup
{
    /// <summary>
    /// Filas activas del modo vigente, por <c>table_id</c>. Una mesa sin fila en ese modo, o con
    /// <c>active = false</c>, no aparece: sale sin coordenadas, igual que un puesto sin colocar.
    /// Si el modo no se puede leer (Redis y Postgres caídos) se devuelven vacías: la lista sigue
    /// sirviendo, solo que sin chinchetas.
    /// </summary>
    public static async Task<Dictionary<Guid, TableLayoutReadModel>> LoadActiveModeAsync(
        OrdersDbContext db, IVenueModeProvider venueMode, ILogger logger,
        Guid tenantId, IReadOnlyCollection<Guid> tableIds, CancellationToken ct)
    {
        if (tableIds.Count == 0) return new();

        string mode;
        try
        {
            mode = (await venueMode.GetAsync(tenantId, ct)).ToString();
        }
        catch (VenueModeUnavailableException ex)
        {
            logger.LogWarning(ex, "orders.table_layout.mode_unavailable {TenantId}", tenantId);
            return new();
        }

        return await db.Set<TableLayoutReadModel>()
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.Mode == mode && l.Active && tableIds.Contains(l.TableId))
            .ToDictionaryAsync(l => l.TableId, ct);
    }
}
