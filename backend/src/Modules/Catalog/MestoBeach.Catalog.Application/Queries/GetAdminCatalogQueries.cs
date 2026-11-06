using MediatR;
using MestoBeach.Catalog.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Catalog.Application.Queries;

// ─── Admin Categories Query ───────────────────────────────────────────────────

public sealed record AdminCategoryDto(
    Guid Id,
    string Name,
    int DisplayOrder,
    int ItemCount,
    DateTime CreatedAt,
    /// <summary>ADR-041: el admin pinta los chips Día / Noche. Ausente en Mongo → ambos.</summary>
    IReadOnlyList<string> VisibleIn
);

public sealed record GetAdminCategoriesQuery(Guid TenantId, Guid RequesterId)
    : IRequest<Result<IReadOnlyList<AdminCategoryDto>>>;

public sealed class GetAdminCategoriesQueryHandler
    : IRequestHandler<GetAdminCategoriesQuery, Result<IReadOnlyList<AdminCategoryDto>>>
{
    private readonly ICatalogRepository _repo;
    private readonly ILogger<GetAdminCategoriesQueryHandler> _log;

    public GetAdminCategoriesQueryHandler(ICatalogRepository repo,
        ILogger<GetAdminCategoriesQueryHandler> log)
    {
        _repo = repo;
        _log = log;
    }

    public async Task<Result<IReadOnlyList<AdminCategoryDto>>> Handle(
        GetAdminCategoriesQuery query, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(query.TenantId, ct);

        var categories = catalog?.Categories ?? [];
        var items = catalog?.Items ?? [];

        var result = categories
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new AdminCategoryDto(
                Id: c.Id,
                Name: c.Name,
                DisplayOrder: c.DisplayOrder,
                ItemCount: items.Count(i => i.CategoryId == c.Id && !i.IsDeleted),
                CreatedAt: c.CreatedAt,
                VisibleIn: c.VisibleIn
            ))
            .ToList();

        _log.LogInformation(
            "catalog.admin.categories.list tenantId={TenantId} requesterId={RequesterId} categoryCount={Count}",
            query.TenantId, query.RequesterId, result.Count);

        return Result.Success<IReadOnlyList<AdminCategoryDto>>(result);
    }
}

// ─── Admin Items Query ────────────────────────────────────────────────────────

public sealed record AdminItemDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string? Description,
    int PriceCents,
    IReadOnlyList<string> Images,
    bool Available,
    bool StockEnabled,
    int StockQty,
    int LowStockThreshold,
    int DisplayOrder,
    IReadOnlyList<Guid> ModifierGroupIds,
    bool AllowNotes,
    /// <summary>Estación de KDS propia del plato; null = hereda la de su categoría.</summary>
    Guid? StationId,
    /// <summary>ADR-041. Ausente en Mongo → ["RESTAURANT","NIGHT"]; este endpoint no filtra por modo.</summary>
    IReadOnlyList<string> VisibleIn,
    DateTime? DeletedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record AdminItemsPagination(int Page, int PageSize, int Total);

public sealed record GetAdminItemsResponse(
    IReadOnlyList<AdminItemDto> Items,
    AdminItemsPagination Pagination
);

public sealed record GetAdminItemsQuery(
    Guid TenantId,
    Guid RequesterId,
    Guid? CategoryId,
    bool? Available,
    bool IncludeDeleted,
    int Page,
    int PageSize
) : IRequest<Result<GetAdminItemsResponse>>;

public sealed class GetAdminItemsQueryHandler
    : IRequestHandler<GetAdminItemsQuery, Result<GetAdminItemsResponse>>
{
    private readonly ICatalogRepository _repo;
    private readonly ILogger<GetAdminItemsQueryHandler> _log;

    public GetAdminItemsQueryHandler(ICatalogRepository repo,
        ILogger<GetAdminItemsQueryHandler> log)
    {
        _repo = repo;
        _log = log;
    }

    public async Task<Result<GetAdminItemsResponse>> Handle(
        GetAdminItemsQuery query, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(query.TenantId, ct);

        var categories = catalog?.Categories ?? [];
        var categoryMap = categories.ToDictionary(c => c.Id, c => c.Name);

        IEnumerable<Domain.Entities.CatalogItem> items = catalog?.Items ?? [];

        // Filters
        if (!query.IncludeDeleted) items = items.Where(i => !i.IsDeleted);
        if (query.CategoryId.HasValue) items = items.Where(i => i.CategoryId == query.CategoryId.Value);
        if (query.Available.HasValue)
            items = items.Where(i => i.EffectivelyAvailable == query.Available.Value);

        // Sort
        var sorted = items
            .OrderBy(i => i.DisplayOrder)
            .ThenByDescending(i => i.CreatedAt)
            .ToList();

        var total = sorted.Count;
        var paged = sorted
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(i => new AdminItemDto(
                Id: i.Id,
                CategoryId: i.CategoryId,
                CategoryName: categoryMap.GetValueOrDefault(i.CategoryId, ""),
                Name: i.Name,
                Description: i.Description,
                PriceCents: i.PriceCents,
                Images: i.Images,
                Available: i.Available,
                StockEnabled: i.StockEnabled,
                StockQty: i.StockQty,
                LowStockThreshold: i.LowStockThreshold,
                DisplayOrder: i.DisplayOrder,
                ModifierGroupIds: i.ModifierGroupIds,
                StationId: i.StationId,
                VisibleIn: i.VisibleIn,
                AllowNotes: i.AllowNotes,
                DeletedAt: i.DeletedAt,
                CreatedAt: i.CreatedAt,
                UpdatedAt: i.UpdatedAt
            ))
            .ToList();

        _log.LogInformation(
            "catalog.admin.items.list tenantId={TenantId} requesterId={RequesterId} page={Page} pageSize={PageSize} resultCount={Count}",
            query.TenantId, query.RequesterId, query.Page, query.PageSize, paged.Count);

        return Result.Success(new GetAdminItemsResponse(
            paged,
            new AdminItemsPagination(query.Page, query.PageSize, total)
        ));
    }
}
