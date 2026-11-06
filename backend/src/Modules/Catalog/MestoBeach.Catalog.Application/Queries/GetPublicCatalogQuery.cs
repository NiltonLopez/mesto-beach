using MediatR;
using MestoBeach.Catalog.Application.Common;
using MestoBeach.Catalog.Domain;
using MestoBeach.Catalog.Domain.Entities;
using MestoBeach.Shared.Venue;
using MestoBeach.Catalog.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Catalog.Application.Queries;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record PublicModifierOptionDto(
    Guid Id, string Name, int PriceDeltaCents, bool Available, bool IsDefault);

public sealed record PublicModifierGroupDto(
    Guid Id, string Name, string Type, bool Required, int MinSelect, int? MaxSelect,
    IReadOnlyList<PublicModifierOptionDto> Options);

public sealed record PublicCatalogItemDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    string? Description,
    int PriceCents,
    string? ImageUrl,       // images[0] only (compat)
    IReadOnlyList<string> Images,   // all images for the product detail gallery
    bool Available,
    bool StockEnabled,
    int StockQty,
    int DisplayOrder,
    IReadOnlyList<PublicModifierGroupDto> ModifierGroups,
    bool AllowNotes
);

public sealed record PublicCatalogCategoryDto(Guid Id, string Name, int DisplayOrder);

public sealed record PublicCatalogResponse(
    Guid TenantId,
    IReadOnlyList<PublicCatalogCategoryDto> Categories,
    IReadOnlyList<PublicCatalogItemDto> Items,
    Commands.MenuBrandingDto Branding,
    /// <summary>"RESTAURANT" | "NIGHT" (ADR-041): la carta y el branding vienen ya resueltos para este modo.</summary>
    string VenueMode
);

// ─── Query ────────────────────────────────────────────────────────────────────

public sealed record GetPublicCatalogQuery(Guid TenantId)
    : IRequest<Result<PublicCatalogResponse>>;

public sealed class GetPublicCatalogQueryHandler
    : IRequestHandler<GetPublicCatalogQuery, Result<PublicCatalogResponse>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly IVenueModeProvider _venueMode;
    private readonly ILogger<GetPublicCatalogQueryHandler> _log;

    public GetPublicCatalogQueryHandler(
        ICatalogRepository repo,
        ICatalogCacheService cache,
        IVenueModeProvider venueMode,
        ILogger<GetPublicCatalogQueryHandler> log)
    {
        _repo = repo;
        _cache = cache;
        _venueMode = venueMode;
        _log = log;
    }

    public async Task<Result<PublicCatalogResponse>> Handle(
        GetPublicCatalogQuery query,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Try cache first
        var cached = await _cache.GetAsync(query.TenantId, ct);
        bool cacheHit = cached is not null;

        TenantCatalog catalog;

        if (cacheHit)
        {
            catalog = System.Text.Json.JsonSerializer.Deserialize<TenantCatalog>(cached!)!;
        }
        else
        {
            catalog = await _repo.GetByTenantIdAsync(query.TenantId, ct)
                      ?? new TenantCatalog { TenantId = query.TenantId, UpdatedAt = DateTime.UtcNow };

            // Populate cache (best-effort)
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(catalog);
                await _cache.SetAsync(query.TenantId, json, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "catalog.cache.set_failed tenantId={TenantId}", query.TenantId);
            }
        }

        // Filtro por modo (ADR-041): la caché es la carta sin filtrar; el modo se lee DESPUÉS de ella,
        // en cada petición, y nunca viene del cliente. Lanza VenueModeUnavailableException (→ 503).
        var mode = (await _venueMode.GetAsync(query.TenantId, ct)).ToString();

        var categoryVisible = catalog.Categories.ToDictionary(
            c => c.Id, c => VisibleInModes.Includes(c.VisibleIn, mode));
        // Categoría no encontrada → vale ambos modos (misma regla que Orders).
        bool IsVisible(CatalogItem i) =>
            !i.IsDeleted
            && VisibleInModes.Includes(i.VisibleIn, mode)
            && (!categoryVisible.TryGetValue(i.CategoryId, out var catOk) || catOk);

        var liveItems = catalog.Items.Where(i => !i.IsDeleted).ToList();
        var visibleItems = liveItems.Where(IsVisible).ToList();
        var hiddenByMode = liveItems.Count - visibleItems.Count;
        var categoriesWithItems = visibleItems.Select(i => i.CategoryId).ToHashSet();

        // Una categoría visible sin ítems visibles en el modo no viaja (en los dos modos).
        var categories = catalog.Categories
            .Where(c => categoryVisible[c.Id] && categoriesWithItems.Contains(c.Id))
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new PublicCatalogCategoryDto(c.Id, c.Name, c.DisplayOrder))
            .ToList();

        var groupsById = catalog.ModifierGroups.ToDictionary(g => g.Id);

        var items = visibleItems
            .OrderBy(i => i.DisplayOrder)
            .Select(i => new PublicCatalogItemDto(
                Id: i.Id,
                CategoryId: i.CategoryId,
                Name: i.Name,
                Description: i.Description,
                PriceCents: i.PriceCents,
                ImageUrl: i.Images.Count > 0 ? i.Images[0] : null,
                Images: i.Images.ToList(),
                Available: i.EffectivelyAvailable,
                StockEnabled: i.StockEnabled,
                StockQty: i.StockQty,
                DisplayOrder: i.DisplayOrder,
                ModifierGroups: i.ModifierGroupIds
                    .Where(groupsById.ContainsKey)
                    .Select(gid => groupsById[gid])
                    .Select(g => new PublicModifierGroupDto(
                        g.Id, g.Name, g.Type.ToString().ToUpperInvariant(),
                        g.Required, g.MinSelect, g.MaxSelect,
                        g.Options.Select(o => new PublicModifierOptionDto(
                            o.Id, o.Name, o.PriceDeltaCents, o.Available, o.IsDefault)).ToList()))
                    .ToList(),
                AllowNotes: i.AllowNotes
            ))
            .ToList();

        sw.Stop();
        _log.LogInformation(
            "catalog.public.get tenantId={TenantId} cacheHit={CacheHit} itemCount={Count} durationMs={Ms} venueMode={VenueMode} hiddenByMode={HiddenByMode}",
            query.TenantId, cacheHit, items.Count, sw.ElapsedMilliseconds, mode, hiddenByMode);

        // Branding resuelto en el servidor: el menú no sabe de overrides ni de `night`.
        var b = BrandingResolver.Resolve(catalog.Branding, mode);
        var branding = new Commands.MenuBrandingDto(
            b.DisplayName, b.Slogan, b.LogoUrl, b.HeroUrl, b.PrimaryColor, b.Theme, b.BackgroundColor, b.FontKey);

        return Result.Success(new PublicCatalogResponse(query.TenantId, categories, items, branding, mode));
    }
}
