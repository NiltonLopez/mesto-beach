using System.Text.Json.Serialization;

namespace MestoBeach.Catalog.Domain.Entities;

/// <summary>
/// Represents a category within a tenant's catalog.
/// Stored as a subdocument in the `catalogs` MongoDB collection.
/// </summary>
public sealed class CatalogCategory
{
    public CatalogCategory(Guid id, string name, int displayOrder)
    {
        Id = id;
        Name = name;
        DisplayOrder = displayOrder;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public int DisplayOrder { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Modos en los que se ve la categoría (ADR-041). [JsonInclude]: la caché catalog:{t} reconstruye el
    /// dominio con System.Text.Json y sin el atributo volvería al default en silencio. Una entrada de
    /// caché sin el campo se lee como ambos modos.
    /// </summary>
    [JsonInclude] public List<string> VisibleIn { get; private set; } = VisibleInModes.Both();

    /// <summary>Fija los modos ya normalizados (<see cref="VisibleInModes.Normalize"/>).</summary>
    public void SetVisibleIn(List<string> visibleIn) => VisibleIn = visibleIn;

    public void Update(string? name, int? displayOrder)
    {
        if (name is not null) Name = name;
        if (displayOrder is not null) DisplayOrder = displayOrder.Value;
    }
}
