namespace MestoBeach.Sessions.Domain.Entities;

/// <summary>A wall/line segment drawn inside a floor's rectangle (layout only, coords in floor space).</summary>
public sealed record WallSegment(double X1, double Y1, double X2, double Y2);

/// <summary>A named level/floor of a tenant restaurant that holds a table-map layout.</summary>
public sealed class Floor
{
    private Floor() { } // EF Core

    public Floor(Guid id, Guid tenantId, string name, int sortOrder, int width, int height)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        SortOrder = sortOrder;
        Width = width;
        Height = height;
        Walls = new();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }

    /// <summary>Canvas dimensions of the floor's main rectangle, in layout px.</summary>
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Interior wall segments, stored as jsonb. Layout-only.</summary>
    public List<WallSegment> Walls { get; private set; } = new();

    /// <summary>
    /// Modos en los que el piso existe (PRD-019): ["RESTAURANT"] = la Playa cierra de noche.
    /// Siempre no vacío, en mayúsculas y en orden canónico RESTAURANT, NIGHT.
    /// </summary>
    public List<string> VisibleIn { get; private set; } = ["RESTAURANT", "NIGHT"];

    public bool IsVisibleIn(string mode) => VisibleIn.Contains(mode);

    public void SetVisibleIn(List<string> modes) { VisibleIn = modes; Touch(); }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void Rename(string name) { Name = name; Touch(); }
    public void SetOrder(int sortOrder) { SortOrder = sortOrder; Touch(); }
    public void SetCanvas(int width, int height) { Width = width; Height = height; Touch(); }
    public void SetWalls(List<WallSegment> walls) { Walls = walls ?? new(); Touch(); }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
