namespace MestoBeach.Sessions.Domain.Entities;

/// <summary>
/// Posición de una mesa en UN modo del local (ADR-038, SDD-003 §table_layouts). Una fila por
/// <c>(table_id, mode)</c>. <see cref="Active"/> es el interruptor por modo (el global sigue en
/// <c>tables.active</c>) y <see cref="StationId"/> la barra que la sirve; solo tiene efecto en NIGHT.
/// El modo se guarda como string en mayúsculas (<c>VenueMode.ToString()</c>).
/// </summary>
public sealed class TableLayout
{
    private TableLayout() { } // EF Core

    public TableLayout(Guid tenantId, Guid tableId, string mode, Guid? floorId, double? posX, double? posY,
        bool active, Guid? stationId = null)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        TableId = tableId;
        Mode = mode;
        FloorId = floorId;
        PosX = posX;
        PosY = posY;
        Active = active;
        StationId = stationId;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TableId { get; private set; }
    public string Mode { get; private set; } = string.Empty;
    public Guid? FloorId { get; private set; }
    public double? PosX { get; private set; }
    public double? PosY { get; private set; }
    public bool Active { get; private set; } = true;
    public Guid? StationId { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void SetPlacement(Guid? floorId, double? posX, double? posY)
    {
        FloorId = floorId;
        PosX = posX;
        PosY = posY;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active) { Active = active; UpdatedAt = DateTime.UtcNow; }
    public void SetStation(Guid? stationId) { StationId = stationId; UpdatedAt = DateTime.UtcNow; }
}
