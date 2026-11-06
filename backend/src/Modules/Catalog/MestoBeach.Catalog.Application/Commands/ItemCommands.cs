using FluentValidation;
using MediatR;
using MestoBeach.Catalog.Application.Common;
using MestoBeach.Catalog.Application.Queries;
using MestoBeach.Catalog.Domain.Entities;
using MestoBeach.Catalog.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Catalog.Application.Commands;

// ─── Create Item ──────────────────────────────────────────────────────────────

public sealed record CreateItemCommand(
    Guid TenantId,
    Guid CategoryId,
    string Name,
    string? Description,
    int PriceCents,
    bool Available,
    bool StockEnabled,
    int StockQty,
    int LowStockThreshold,
    int DisplayOrder,
    Guid RequesterId,
    List<Guid>? ModifierGroupIds = null,
    bool AllowNotes = false,
    /// <summary>Estación de KDS propia; null deja que el plato herede la de su categoría.</summary>
    Guid? StationId = null,
    /// <summary>ADR-041. null = ambos modos al crear / no tocar al editar.</summary>
    List<string>? VisibleIn = null
) : IRequest<Result<AdminItemDto>>;

public sealed class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemCommandValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150)
            .Must(n => !string.IsNullOrWhiteSpace(n));
        RuleFor(x => x.Description).MaximumLength(300).When(x => x.Description is not null);
        RuleFor(x => x.PriceCents).GreaterThan(0);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
        RuleFor(x => x.StockQty).GreaterThanOrEqualTo(0)
            .When(x => x.StockEnabled)
            .WithMessage("stockQty es requerido cuando stockEnabled es true.");
        RuleFor(x => x.LowStockThreshold).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateItemCommandHandler
    : IRequestHandler<CreateItemCommand, Result<AdminItemDto>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<CreateItemCommandHandler> _log;

    public CreateItemCommandHandler(ICatalogRepository repo, ICatalogCacheService cache,
        ILogger<CreateItemCommandHandler> log)
    { _repo = repo; _cache = cache; _log = log; }

    public async Task<Result<AdminItemDto>> Handle(CreateItemCommand cmd, CancellationToken ct)
    {
        var (visibleIn, visibleInError) = VisibleInInput.Parse(cmd.VisibleIn);
        if (visibleInError is not null)
            return Result.Failure<AdminItemDto>(visibleInError);

        // Verify category exists
        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var categoryExists = catalog?.Categories.Any(c => c.Id == cmd.CategoryId) ?? false;
        if (!categoryExists)
            return Result.Failure<AdminItemDto>(
                Error.NotFound("Categoría no encontrada en el tenant."));

        var item = new CatalogItem(
            Guid.NewGuid(), cmd.CategoryId,
            cmd.Name.Trim(), cmd.Description?.Trim(),
            cmd.PriceCents, cmd.Available, cmd.StockEnabled,
            cmd.StockEnabled ? cmd.StockQty : 0,
            cmd.LowStockThreshold, cmd.DisplayOrder);
        item.SetModifiers(cmd.ModifierGroupIds, cmd.AllowNotes);
        if (cmd.StationId is { } estacion && estacion != Guid.Empty) item.SetStation(estacion);
        if (visibleIn is not null) item.SetVisibleIn(visibleIn);

        await _repo.AddItemAsync(cmd.TenantId, item, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        _log.LogInformation(
            "catalog.admin.item.created tenantId={TenantId} itemId={Id} categoryId={CatId} name={Name} priceCents={Price} requesterId={RequesterId}",
            cmd.TenantId, item.Id, item.CategoryId, item.Name, item.PriceCents, cmd.RequesterId);

        return Result.Success(ToDto(item, catalog!));
    }

    private static AdminItemDto ToDto(CatalogItem item, Domain.TenantCatalog catalog)
    {
        var catName = catalog.Categories.FirstOrDefault(c => c.Id == item.CategoryId)?.Name ?? "";
        return new AdminItemDto(item.Id, item.CategoryId, catName, item.Name, item.Description,
            item.PriceCents, item.Images, item.Available, item.StockEnabled,
            item.StockQty, item.LowStockThreshold, item.DisplayOrder,
            item.ModifierGroupIds, item.AllowNotes, item.StationId, item.VisibleIn,
            item.DeletedAt, item.CreatedAt, item.UpdatedAt);
    }
}

// ─── Update Item ──────────────────────────────────────────────────────────────

public sealed record UpdateItemCommand(
    Guid TenantId, Guid ItemId,
    string? Name, string? Description, int? PriceCents, Guid? CategoryId,
    bool? Available, bool? StockEnabled, int? StockQty, int? LowStockThreshold,
    int? DisplayOrder, Guid RequesterId,
    List<Guid>? ModifierGroupIds = null, bool? AllowNotes = null,
    /// <summary>Estación propia del plato; null = no tocarla.</summary>
    Guid? StationId = null,
    /// <summary>true la deja vacía para que el plato vuelva a heredar la de su categoría.</summary>
    bool? ClearStation = null,
    /// <summary>ADR-041. null = no tocar; si llega, se normaliza (VISIBLE_IN_EMPTY / INVALID_MODE).</summary>
    List<string>? VisibleIn = null
) : IRequest<Result<AdminItemDto>>;

public sealed class UpdateItemCommandValidator : AbstractValidator<UpdateItemCommand>
{
    public UpdateItemCommandValidator()
    {
        RuleFor(x => x).Must(x =>
            x.Name is not null || x.Description is not null || x.PriceCents is not null ||
            x.CategoryId is not null || x.Available is not null || x.StockEnabled is not null ||
            x.StockQty is not null || x.LowStockThreshold is not null || x.DisplayOrder is not null ||
            x.ModifierGroupIds is not null || x.AllowNotes is not null ||
            x.StationId is not null || x.ClearStation is true || x.VisibleIn is not null)
            .WithMessage("Al menos un campo debe estar presente en el body.");
        When(x => x.Name is not null, () =>
            RuleFor(x => x.Name!).NotEmpty().MaximumLength(150)
                .Must(n => !string.IsNullOrWhiteSpace(n)));
        When(x => x.Description is not null, () =>
            RuleFor(x => x.Description!).MaximumLength(300));
        When(x => x.PriceCents is not null, () =>
            RuleFor(x => x.PriceCents!.Value).GreaterThan(0));
        When(x => x.DisplayOrder is not null, () =>
            RuleFor(x => x.DisplayOrder!.Value).GreaterThanOrEqualTo(0));
        When(x => x.StockQty is not null, () =>
            RuleFor(x => x.StockQty!.Value).GreaterThanOrEqualTo(0));
        When(x => x.LowStockThreshold is not null, () =>
            RuleFor(x => x.LowStockThreshold!.Value).GreaterThanOrEqualTo(0));
    }
}

public sealed class UpdateItemCommandHandler
    : IRequestHandler<UpdateItemCommand, Result<AdminItemDto>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<UpdateItemCommandHandler> _log;

    public UpdateItemCommandHandler(ICatalogRepository repo, ICatalogCacheService cache,
        ILogger<UpdateItemCommandHandler> log)
    { _repo = repo; _cache = cache; _log = log; }

    public async Task<Result<AdminItemDto>> Handle(UpdateItemCommand cmd, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var item = catalog?.Items.FirstOrDefault(i => i.Id == cmd.ItemId && !i.IsDeleted);
        if (item is null)
            return Result.Failure<AdminItemDto>(
                Error.NotFound("Item no encontrado o eliminado."));

        // Se guarda normalizado y sin fusionarlo con el de la categoría (SDD-002 §PATCH items).
        var (visibleIn, visibleInError) = VisibleInInput.Parse(cmd.VisibleIn);
        if (visibleInError is not null)
            return Result.Failure<AdminItemDto>(visibleInError);

        if (cmd.CategoryId.HasValue)
        {
            var catExists = catalog!.Categories.Any(c => c.Id == cmd.CategoryId.Value);
            if (!catExists)
                return Result.Failure<AdminItemDto>(
                    Error.Validation("Item.CategoryNotFound", "Categoría no existe en el tenant."));
        }

        await _repo.UpdateItemAsync(cmd.TenantId, cmd.ItemId,
            cmd.Name?.Trim(), cmd.Description?.Trim(), cmd.PriceCents, cmd.CategoryId,
            cmd.Available, cmd.StockEnabled, cmd.StockQty, cmd.LowStockThreshold,
            cmd.DisplayOrder, cmd.ModifierGroupIds, cmd.AllowNotes,
            cmd.StationId, cmd.ClearStation is true, visibleIn, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        _log.LogInformation(
            "catalog.admin.item.updated tenantId={TenantId} itemId={Id} requesterId={RequesterId}",
            cmd.TenantId, cmd.ItemId, cmd.RequesterId);

        // Re-read updated state
        var updated = (await _repo.GetByTenantIdAsync(cmd.TenantId, ct))!
            .Items.First(i => i.Id == cmd.ItemId);
        var catName = catalog!.Categories.FirstOrDefault(c => c.Id == updated.CategoryId)?.Name ?? "";

        return Result.Success(new AdminItemDto(updated.Id, updated.CategoryId, catName,
            updated.Name, updated.Description, updated.PriceCents, updated.Images,
            updated.Available, updated.StockEnabled, updated.StockQty,
            updated.LowStockThreshold, updated.DisplayOrder,
            updated.ModifierGroupIds, updated.AllowNotes, updated.StationId, updated.VisibleIn,
            updated.DeletedAt, updated.CreatedAt, updated.UpdatedAt));
    }
}

// ─── Soft-Delete Item ─────────────────────────────────────────────────────────

public sealed record DeleteItemCommand(
    Guid TenantId, Guid ItemId, Guid RequesterId
) : IRequest<Result<Unit>>;

public sealed class DeleteItemCommandHandler
    : IRequestHandler<DeleteItemCommand, Result<Unit>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<DeleteItemCommandHandler> _log;

    public DeleteItemCommandHandler(ICatalogRepository repo, ICatalogCacheService cache,
        ILogger<DeleteItemCommandHandler> log)
    { _repo = repo; _cache = cache; _log = log; }

    public async Task<Result<Unit>> Handle(DeleteItemCommand cmd, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var item = catalog?.Items.FirstOrDefault(i => i.Id == cmd.ItemId);

        if (item is null || item.IsDeleted)
            return Result.Failure<Unit>(
                Error.NotFound("Item no encontrado o ya eliminado."));

        await _repo.SoftDeleteItemAsync(cmd.TenantId, cmd.ItemId, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        _log.LogInformation(
            "catalog.admin.item.deleted tenantId={TenantId} itemId={Id} requesterId={RequesterId}",
            cmd.TenantId, cmd.ItemId, cmd.RequesterId);

        return Result.Success(Unit.Value);
    }
}
