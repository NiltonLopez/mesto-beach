using MestoBeach.Catalog.Domain.Entities;

namespace MestoBeach.Catalog.Domain.Repositories;

/// <summary>
/// Repository for the single-document-per-tenant catalog in MongoDB.
/// All methods operate on the `catalogs` collection.
/// </summary>
public interface ICatalogRepository
{
    // ── Read ─────────────────────────────────────────────────────────────────

    /// <summary>Returns the full catalog document for a tenant, or null if none exists.</summary>
    Task<TenantCatalog?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    // ── Categories ────────────────────────────────────────────────────────────

    /// <summary>
    /// Upserts the catalog document and appends the given category.
    /// Creates the document if it does not exist.
    /// </summary>
    Task AddCategoryAsync(Guid tenantId, CatalogCategory category, CancellationToken ct = default);

    Task UpdateCategoryAsync(Guid tenantId, Guid categoryId,
        string? name, int? displayOrder, List<string>? visibleIn = null, CancellationToken ct = default);

    Task DeleteCategoryAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default);

    // ── Items ─────────────────────────────────────────────────────────────────

    Task AddItemAsync(Guid tenantId, CatalogItem item, CancellationToken ct = default);

    /// <param name="stationId">Estación propia del plato; null = no tocarla.</param>
    /// <param name="clearStation">
    /// true borra la estación propia para que el plato vuelva a heredar la de su categoría.
    /// Hace falta porque en esta firma null significa "no cambiar", no "dejar vacío".
    /// </param>
    Task UpdateItemAsync(Guid tenantId, Guid itemId,
        string? name, string? description, int? priceCents, Guid? categoryId,
        bool? available, bool? stockEnabled, int? stockQty, int? lowStockThreshold,
        int? displayOrder, List<Guid>? modifierGroupIds, bool? allowNotes,
        Guid? stationId = null, bool clearStation = false, List<string>? visibleIn = null,
        CancellationToken ct = default);

    /// <summary>Sets deletedAt = now on the item (soft-delete).</summary>
    Task SoftDeleteItemAsync(Guid tenantId, Guid itemId, CancellationToken ct = default);

    // ── Modifier groups (tenant add-on library) ────────────────────────────────

    Task AddModifierGroupAsync(Guid tenantId, ModifierGroup group, CancellationToken ct = default);
    Task UpdateModifierGroupAsync(Guid tenantId, ModifierGroup group, CancellationToken ct = default);
    /// <summary>Removes the group from the library and detaches it from every item.</summary>
    Task DeleteModifierGroupAsync(Guid tenantId, Guid groupId, CancellationToken ct = default);

    // ── Branding (menu look & feel) ─────────────────────────────────────────────

    /// <summary>
    /// Escribe el branding de día campo a campo ($set branding.displayName, …). Nunca toca branding.night.
    /// </summary>
    Task UpdateBrandingAsync(Guid tenantId, MenuBranding branding, CancellationToken ct = default);

    /// <summary>Escribe solo branding.night (overrides del modo NIGHT); nunca toca el día.</summary>
    Task UpdateNightBrandingAsync(Guid tenantId, NightBranding night, CancellationToken ct = default);

    // ── Images ────────────────────────────────────────────────────────────────

    /// <summary>Appends the image URL to items[].images and updates updatedAt.</summary>
    Task AddImageAsync(Guid tenantId, Guid itemId, string imageUrl, CancellationToken ct = default);

    /// <summary>Removes the image URL at the given index from items[].images.</summary>
    Task RemoveImageAsync(Guid tenantId, Guid itemId, int imageIndex, CancellationToken ct = default);
}
