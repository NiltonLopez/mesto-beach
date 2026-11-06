using MestoBeach.Sessions.Domain.Enums;

namespace MestoBeach.Sessions.Domain.Entities;

/// <summary>Physical table configured for a tenant restaurant.</summary>
public sealed class DiningTable
{
    private DiningTable() { } // EF Core

    public DiningTable(Guid id, Guid tenantId, string tableCode, string? zone, bool active)
    {
        Id = id;
        TenantId = tenantId;
        TableCode = tableCode;
        Zone = zone;
        Active = active;
        QrMode = QrMode.Fixed;
        RequiresPin = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string TableCode { get; private set; } = string.Empty;
    public string? Zone { get; private set; }
    public bool Active { get; private set; }

    // ─── Access mode (chosen by the admin when the table is created/edited) ──────
    /// <summary>How customers reach this table's session: a Fixed printed QR or a rotating Dynamic QR.</summary>
    public QrMode QrMode { get; private set; } = QrMode.Fixed;
    /// <summary>Whether a Fixed-QR session requires a PIN. False = "sin seguridad" (open). N/A for Dynamic.</summary>
    public bool RequiresPin { get; private set; } = true;

    // ─── Map layout — DEPRECADO (PRD-019): la posición vive en table_layouts (una fila por modo).
    // Sessions ya no lee ni escribe estas columnas; las borrará DropLegacyTablePosition. ──
    public Guid? FloorId { get; private set; }
    public double? PosX { get; private set; }
    public double? PosY { get; private set; }
    public int? Seats { get; private set; }
    /// <summary>Visual shape on the map: "SQUARE" | "ROUND". Defaults to SQUARE.</summary>
    public string Shape { get; private set; } = "SQUARE";

    // ─── Clase de puesto (PRD-019): TABLE | HIGH_TABLE | RESERVADO ───────────────
    public const string KindTable = "TABLE";
    public const string KindHighTable = "HIGH_TABLE";
    public const string KindReservado = "RESERVADO";
    public static readonly IReadOnlyList<string> Kinds = [KindTable, KindHighTable, KindReservado];

    /// <summary>Clase de puesto. Orders la copia a orders.priority (RESERVADO primero, solo en NIGHT).</summary>
    public string Kind { get; private set; } = KindTable;

    public void SetKind(string kind)
    {
        var k = kind.Trim().ToUpperInvariant();
        if (!Kinds.Contains(k)) throw new ArgumentException($"kind inválido: {kind}", nameof(kind));
        Kind = k;
        UpdatedAt = DateTime.UtcNow;
    }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void UpdateTableCode(string tableCode)
    {
        TableCode = tableCode;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateZone(string? zone)
    {
        Zone = zone;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active)
    {
        Active = active;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Place (or move) the table on a floor map. Pass floorId=null to unplace it.</summary>
    public void SetPlacement(Guid? floorId, double? posX, double? posY)
    {
        FloorId = floorId;
        PosX = posX;
        PosY = posY;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetDetails(int? seats, string? shape)
    {
        if (seats.HasValue) Seats = seats.Value;
        if (!string.IsNullOrWhiteSpace(shape)) Shape = shape.Trim().ToUpperInvariant();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets how this table is accessed. Dynamic always implies a rotating QR with no static PIN,
    /// so <paramref name="requiresPin"/> is ignored for Dynamic.
    /// </summary>
    public void SetQrConfig(QrMode qrMode, bool requiresPin)
    {
        QrMode = qrMode;
        RequiresPin = qrMode == QrMode.Dynamic ? false : requiresPin;
        UpdatedAt = DateTime.UtcNow;
    }
}
