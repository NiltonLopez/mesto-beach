using MestoBeach.Catalog.Domain.Entities;

namespace MestoBeach.Catalog.Domain;

/// <summary>
/// Represents the entire catalog for one tenant.
/// In MongoDB this is a single document in the `catalogs` collection.
/// This is a read-model returned from the repository.
/// </summary>
public sealed class TenantCatalog
{
    public Guid TenantId { get; init; }
    public List<CatalogCategory> Categories { get; init; } = [];
    public List<CatalogItem> Items { get; init; } = [];
    /// <summary>Tenant-level reusable add-on library. Items reference these by id.</summary>
    public List<ModifierGroup> ModifierGroups { get; init; } = [];
    /// <summary>Tenant-controlled look &amp; feel for the customer menu.</summary>
    public MenuBranding Branding { get; init; } = new();
    public DateTime UpdatedAt { get; init; }
}
