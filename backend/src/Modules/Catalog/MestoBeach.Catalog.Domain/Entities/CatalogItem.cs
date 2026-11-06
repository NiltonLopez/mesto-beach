using System.Text.Json.Serialization;

namespace MestoBeach.Catalog.Domain.Entities;

/// <summary>
/// Represents a menu item within a tenant's catalog.
/// Stored as a subdocument in the `catalogs` MongoDB collection.
/// Soft-deleted via <see cref="DeletedAt"/>.
/// </summary>
public sealed class CatalogItem
{
    public CatalogItem(
        Guid id,
        Guid categoryId,
        string name,
        string? description,
        int priceCents,
        bool available,
        bool stockEnabled,
        int stockQty,
        int lowStockThreshold,
        int displayOrder)
    {
        Id = id;
        CategoryId = categoryId;
        Name = name;
        Description = description;
        PriceCents = priceCents;
        Available = available;
        StockEnabled = stockEnabled;
        StockQty = stockQty;
        LowStockThreshold = lowStockThreshold;
        DisplayOrder = displayOrder;
        Images = [];
        ModifierGroupIds = [];
        AllowNotes = false;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public int PriceCents { get; private set; }
    // These fields are NOT constructor parameters and have private setters. When the public
    // catalog is cached (the domain object is JSON-serialized to Redis) and later read back,
    // System.Text.Json reconstructs the item via its constructor and would skip private-setter
    // properties — silently dropping images, modifiers, allowNotes and the soft-delete flag on a
    // cache HIT. [JsonInclude] lets the serializer round-trip them through the non-public accessor.
    [JsonInclude] public List<string> Images { get; private set; }
    public bool Available { get; private set; }
    public bool StockEnabled { get; private set; }
    public int StockQty { get; private set; }
    public int LowStockThreshold { get; private set; }
    public int DisplayOrder { get; private set; }
    /// <summary>Ids of the modifier groups (from the tenant library) attached to this item.</summary>
    [JsonInclude] public List<Guid> ModifierGroupIds { get; private set; } = [];
    /// <summary>Whether the customer may leave a free-text note for this item.</summary>
    [JsonInclude] public bool AllowNotes { get; private set; }
    /// <summary>
    /// Estación de KDS a la que va este plato. Si es <c>null</c>, hereda la de su categoría,
    /// que es el ruteo clásico y sigue siendo válido: solo hay que fijarla aquí en las
    /// excepciones —un postre flambeado que sale de parrilla y no de cocina, por ejemplo—.
    /// </summary>
    [JsonInclude] public Guid? StationId { get; private set; }
    /// <summary>
    /// Modos en los que se ve el plato (ADR-041); su efecto depende también del de su categoría.
    /// [JsonInclude] por la misma razón que el resto: sin él la caché lo devolvería a ambos modos.
    /// </summary>
    [JsonInclude] public List<string> VisibleIn { get; private set; } = VisibleInModes.Both();
    [JsonInclude] public DateTime? DeletedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void SetModifiers(List<Guid>? groupIds, bool allowNotes)
    {
        ModifierGroupIds = groupIds ?? new();
        AllowNotes = allowNotes;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Fija la estación del plato. <c>null</c> devuelve el plato a la de su categoría.</summary>
    public void SetStation(Guid? stationId)
    {
        StationId = stationId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Fija los modos ya normalizados (<see cref="VisibleInModes.Normalize"/>).</summary>
    public void SetVisibleIn(List<string> visibleIn)
    {
        VisibleIn = visibleIn;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>
    /// Effective availability: false if stock-managed and out of stock.
    /// </summary>
    public bool EffectivelyAvailable =>
        Available && !(StockEnabled && StockQty == 0);

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddImage(string url)
    {
        Images.Add(url);
        UpdatedAt = DateTime.UtcNow;
    }

    public void RemoveImageAt(int index)
    {
        Images.RemoveAt(index);
        UpdatedAt = DateTime.UtcNow;
    }
}
