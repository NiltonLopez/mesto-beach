using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Catalog.Application.Commands;
using MestoBeach.Catalog.Application.Queries;
using MestoBeach.Shared.Context;
using MestoBeach.Shared.Venue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Catalog;

// ─── Public catalog endpoint ──────────────────────────────────────────────────

/// <summary>Public menu endpoint — no authentication required.</summary>
[Route("api/v1/catalog")]
public sealed class CatalogPublicController : ApiController
{
    public CatalogPublicController(ISender mediator) : base(mediator) { }

    // GET /api/v1/catalog/{tenantId}
    [HttpGet("{tenantId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicCatalog(Guid tenantId, CancellationToken ct)
    {
        // La respuesta depende del modo activo, que cambia sin que cambie la URL (ADR-041).
        Response.Headers.CacheControl = "no-store";
        try
        {
            return ToResponse(await Mediator.Send(new GetPublicCatalogQuery(tenantId), ct));
        }
        catch (VenueModeUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = VenueModeUnavailableException.Code,
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable,
            });
        }
    }
}

// ─── Admin catalog endpoints ──────────────────────────────────────────────────

/// <summary>Admin catalog management: categories, items, images, cache.</summary>
[Route("api/v1/catalog/{tenantId:guid}")]
public sealed class CatalogAdminController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public CatalogAdminController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool CanAccessTenant(Guid tenantId)
    {
        if (_currentUser.UserType == "platform_admin") return true;
        if (_currentUser.UserType == "staff")
        {
            var staffTenantId = HttpContext.Items["tenant_id"] as Guid?;
            return staffTenantId == tenantId;
        }
        return false;
    }

    private IActionResult ForbiddenTenant() =>
        StatusCode(403, new ProblemDetails
        {
            Title = "Forbidden",
            Detail = "No tiene permisos para gestionar este tenant.",
            Status = 403
        });

    // ── Categories ─────────────────────────────────────────────────────────────

    // GET /api/v1/catalog/{tenantId}/categories
    [HttpGet("categories")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> GetCategories(Guid tenantId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new GetAdminCategoriesQuery(tenantId, _currentUser.UserId), ct));
    }

    // POST /api/v1/catalog/{tenantId}/categories
    [HttpPost("categories")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> CreateCategory(
        Guid tenantId, [FromBody] CreateCategoryRequest req, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new CreateCategoryCommand(tenantId, req.Name, req.DisplayOrder, _currentUser.UserId, req.VisibleIn), ct),
            successStatus: 201);
    }

    // PATCH /api/v1/catalog/{tenantId}/categories/{categoryId}
    [HttpPatch("categories/{categoryId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> UpdateCategory(
        Guid tenantId, Guid categoryId,
        [FromBody] UpdateCategoryRequest req, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new UpdateCategoryCommand(tenantId, categoryId, req.Name, req.DisplayOrder, _currentUser.UserId, req.VisibleIn), ct));
    }

    // DELETE /api/v1/catalog/{tenantId}/categories/{categoryId}
    [HttpDelete("categories/{categoryId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> DeleteCategory(
        Guid tenantId, Guid categoryId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new DeleteCategoryCommand(tenantId, categoryId, _currentUser.UserId), ct));
    }

    // ── Items ──────────────────────────────────────────────────────────────────

    // GET /api/v1/catalog/{tenantId}/items
    [HttpGet("items")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> GetItems(
        Guid tenantId,
        [FromQuery] Guid? categoryId,
        [FromQuery] bool? available,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        if (page < 1) return BadRequest("page debe ser >= 1.");
        if (pageSize is < 1 or > 100) return BadRequest("pageSize debe estar entre 1 y 100.");

        return ToResponse(await Mediator.Send(
            new GetAdminItemsQuery(tenantId, _currentUser.UserId,
                categoryId, available, includeDeleted, page, pageSize), ct));
    }

    // POST /api/v1/catalog/{tenantId}/items
    [HttpPost("items")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> CreateItem(
        Guid tenantId, [FromBody] CreateItemRequest req, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new CreateItemCommand(tenantId, req.CategoryId, req.Name, req.Description,
                req.PriceCents, req.Available ?? true, req.StockEnabled ?? false,
                req.StockQty ?? 0, req.LowStockThreshold ?? 0, req.DisplayOrder,
                _currentUser.UserId, req.ModifierGroupIds, req.AllowNotes ?? false,
                req.StationId, req.VisibleIn), ct),
            successStatus: 201);
    }

    // PATCH /api/v1/catalog/{tenantId}/items/{itemId}
    [HttpPatch("items/{itemId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> UpdateItem(
        Guid tenantId, Guid itemId,
        [FromBody] UpdateItemRequest req, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new UpdateItemCommand(tenantId, itemId, req.Name, req.Description,
                req.PriceCents, req.CategoryId, req.Available, req.StockEnabled,
                req.StockQty, req.LowStockThreshold, req.DisplayOrder,
                _currentUser.UserId, req.ModifierGroupIds, req.AllowNotes,
                req.StationId, req.ClearStation, req.VisibleIn), ct));
    }

    // DELETE /api/v1/catalog/{tenantId}/items/{itemId}
    [HttpDelete("items/{itemId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> DeleteItem(
        Guid tenantId, Guid itemId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new DeleteItemCommand(tenantId, itemId, _currentUser.UserId), ct));
    }

    // ── Modifier groups (add-on library) ───────────────────────────────────────

    // GET /api/v1/catalog/{tenantId}/modifier-groups
    [HttpGet("modifier-groups")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> GetModifierGroups(Guid tenantId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new GetModifierGroupsQuery(tenantId, _currentUser.UserId), ct));
    }

    // POST /api/v1/catalog/{tenantId}/modifier-groups
    [HttpPost("modifier-groups")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> CreateModifierGroup(
        Guid tenantId, [FromBody] CreateModifierGroupRequest req, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new CreateModifierGroupCommand(tenantId, req.Name, req.Type, req.Required,
                req.MinSelect, req.MaxSelect, req.Options ?? [], _currentUser.UserId), ct),
            successStatus: 201);
    }

    // PATCH /api/v1/catalog/{tenantId}/modifier-groups/{groupId}
    [HttpPatch("modifier-groups/{groupId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> UpdateModifierGroup(
        Guid tenantId, Guid groupId,
        [FromBody] CreateModifierGroupRequest req, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new UpdateModifierGroupCommand(tenantId, groupId, req.Name, req.Type, req.Required,
                req.MinSelect, req.MaxSelect, req.Options ?? [], _currentUser.UserId), ct));
    }

    // DELETE /api/v1/catalog/{tenantId}/modifier-groups/{groupId}
    [HttpDelete("modifier-groups/{groupId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> DeleteModifierGroup(
        Guid tenantId, Guid groupId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new DeleteModifierGroupCommand(tenantId, groupId, _currentUser.UserId), ct));
    }

    // ── Menu branding (look & feel) ─────────────────────────────────────────────

    // GET /api/v1/catalog/{tenantId}/branding
    [HttpGet("branding")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> GetBranding(Guid tenantId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(new GetMenuBrandingQuery(tenantId, _currentUser.UserId), ct));
    }

    // PUT /api/v1/catalog/{tenantId}/branding
    [HttpPut("branding")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> UpdateBranding(
        Guid tenantId, [FromBody] UpdateBrandingRequest req, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new UpdateMenuBrandingCommand(tenantId, req.DisplayName ?? "", req.Slogan ?? "",
                req.PrimaryColor, req.Theme, req.BackgroundColor, req.FontKey, _currentUser.UserId), ct));
    }

    // POST /api/v1/catalog/{tenantId}/branding/{kind}/image   (kind = logo | hero)
    [HttpPost("branding/{kind}/image")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadBrandingImage(
        Guid tenantId, string kind, IFormFile file, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        if (file is null || file.Length == 0) return BadRequest("El campo 'file' es requerido.");
        await using var stream = file.OpenReadStream();
        return ToResponse(await Mediator.Send(
            new UploadBrandingImageCommand(tenantId, kind, stream, file.ContentType, file.Length, _currentUser.UserId), ct),
            successStatus: 201);
    }

    // DELETE /api/v1/catalog/{tenantId}/branding/{kind}/image
    [HttpDelete("branding/{kind}/image")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> DeleteBrandingImage(Guid tenantId, string kind, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(new DeleteBrandingImageCommand(tenantId, kind, _currentUser.UserId), ct));
    }

    // ── Branding Night (ADR-039 / ADR-041) ─────────────────────────────────────

    // GET /api/v1/catalog/{tenantId}/branding/night
    [HttpGet("branding/night")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> GetNightBranding(Guid tenantId, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(new GetNightBrandingQuery(tenantId, _currentUser.UserId), ct));
    }

    // PUT /api/v1/catalog/{tenantId}/branding/night
    [HttpPut("branding/night")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> UpdateNightBranding(
        Guid tenantId, [FromBody] UpdateNightBrandingRequest req, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new UpdateNightBrandingCommand(tenantId, req.Enabled, req.DisplayName, req.Slogan,
                req.PrimaryColor, req.Theme, req.BackgroundColor, req.FontKey, _currentUser.UserId), ct));
    }

    // POST /api/v1/catalog/{tenantId}/branding/night/{kind}/image   (kind = logo | hero)
    [HttpPost("branding/night/{kind}/image")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadNightBrandingImage(
        Guid tenantId, string kind, IFormFile file, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        if (file is null || file.Length == 0) return BadRequest("El campo 'file' es requerido.");
        await using var stream = file.OpenReadStream();
        return ToResponse(await Mediator.Send(
            new UploadNightBrandingImageCommand(tenantId, kind, stream, file.ContentType, file.Length, _currentUser.UserId), ct),
            successStatus: 201);
    }

    // DELETE /api/v1/catalog/{tenantId}/branding/night/{kind}/image
    [HttpDelete("branding/night/{kind}/image")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> DeleteNightBrandingImage(Guid tenantId, string kind, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(new DeleteNightBrandingImageCommand(tenantId, kind, _currentUser.UserId), ct));
    }

    // ── Images ─────────────────────────────────────────────────────────────────

    // POST /api/v1/catalog/{tenantId}/items/{itemId}/images
    [HttpPost("items/{itemId:guid}/images")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    [RequestSizeLimit(3 * 1024 * 1024)] // 3MB to account for multipart overhead
    public async Task<IActionResult> UploadImage(
        Guid tenantId, Guid itemId,
        IFormFile file, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        if (file is null || file.Length == 0)
            return BadRequest("El campo 'file' es requerido.");

        await using var stream = file.OpenReadStream();
        return ToResponse(await Mediator.Send(
            new UploadImageCommand(tenantId, itemId, stream,
                file.ContentType, file.Length, _currentUser.UserId), ct),
            successStatus: 201);
    }

    // DELETE /api/v1/catalog/{tenantId}/items/{itemId}/images/{imageIndex}
    [HttpDelete("items/{itemId:guid}/images/{imageIndex:int}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> DeleteImage(
        Guid tenantId, Guid itemId, int imageIndex, CancellationToken ct)
    {
        if (!CanAccessTenant(tenantId)) return ForbiddenTenant();
        return ToResponse(await Mediator.Send(
            new DeleteImageCommand(tenantId, itemId, imageIndex, _currentUser.UserId), ct));
    }

    // ── Cache ──────────────────────────────────────────────────────────────────

    // POST /api/v1/catalog/{tenantId}/cache-invalidate
    [HttpPost("cache-invalidate")]
    [Authorize(Policy = AuthorizationPolicies.PlatformAdminJwt)]
    public async Task<IActionResult> InvalidateCache(Guid tenantId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new InvalidateCatalogCacheCommand(tenantId, _currentUser.UserId), ct));
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public sealed record CreateCategoryRequest(string Name, int DisplayOrder, List<string>? VisibleIn = null);
public sealed record UpdateCategoryRequest(string? Name, int? DisplayOrder, List<string>? VisibleIn = null);

public sealed record CreateItemRequest(
    Guid CategoryId,
    string Name,
    string? Description,
    int PriceCents,
    bool? Available,
    bool? StockEnabled,
    int? StockQty,
    int? LowStockThreshold,
    int DisplayOrder,
    List<Guid>? ModifierGroupIds = null,
    bool? AllowNotes = null,
    Guid? StationId = null,
    List<string>? VisibleIn = null
);

public sealed record UpdateItemRequest(
    string? Name,
    string? Description,
    int? PriceCents,
    Guid? CategoryId,
    bool? Available,
    bool? StockEnabled,
    int? StockQty,
    int? LowStockThreshold,
    int? DisplayOrder,
    List<Guid>? ModifierGroupIds = null,
    bool? AllowNotes = null,
    // Estación de KDS del plato. null = no tocarla; para dejarla vacía (que herede la de su
    // categoría) hay que mandar clearStation: true, porque en un PATCH null significa "no cambiar".
    Guid? StationId = null,
    bool? ClearStation = null,
    // ADR-041: modos en los que se ve el plato; null = no tocar.
    List<string>? VisibleIn = null
);

public sealed record UpdateBrandingRequest(
    string? DisplayName,
    string? Slogan,
    string PrimaryColor,
    string Theme,
    string? BackgroundColor,
    string FontKey
);

/// <summary>
/// Overrides de Night (sin imágenes). null = hereda del día, salvo backgroundColor (null = usar el tema).
/// Enabled es bool? para que su ausencia dé 400 en el validador y no un false silencioso.
/// </summary>
public sealed record UpdateNightBrandingRequest(
    bool? Enabled,
    string? DisplayName,
    string? Slogan,
    string? PrimaryColor,
    string? Theme,
    string? BackgroundColor,
    string? FontKey
);

public sealed record CreateModifierGroupRequest(
    string Name,
    string Type,
    bool Required,
    int MinSelect,
    int? MaxSelect,
    List<MestoBeach.Catalog.Application.Commands.ModifierOptionInput>? Options
);
