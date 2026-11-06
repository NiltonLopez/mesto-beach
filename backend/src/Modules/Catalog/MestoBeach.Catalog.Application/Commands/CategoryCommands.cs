using FluentValidation;
using MediatR;
using MestoBeach.Catalog.Application.Common;
using MestoBeach.Catalog.Domain.Entities;
using MestoBeach.Catalog.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Catalog.Application.Commands;

// ─── Create Category ──────────────────────────────────────────────────────────

public sealed record CreateCategoryCommand(
    Guid TenantId, string Name, int DisplayOrder, Guid RequesterId,
    /// <summary>ADR-041. null = ambos modos; si llega, se normaliza (VISIBLE_IN_EMPTY / INVALID_MODE).</summary>
    List<string>? VisibleIn = null
) : IRequest<Result<CreatedCategoryResponse>>;

public sealed record CreatedCategoryResponse(
    Guid Id, string Name, int DisplayOrder, DateTime CreatedAt, IReadOnlyList<string> VisibleIn
);

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .Must(n => !string.IsNullOrWhiteSpace(n))
            .WithMessage("name no puede ser solo espacios en blanco.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateCategoryCommandHandler
    : IRequestHandler<CreateCategoryCommand, Result<CreatedCategoryResponse>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<CreateCategoryCommandHandler> _log;

    public CreateCategoryCommandHandler(
        ICatalogRepository repo, ICatalogCacheService cache,
        ILogger<CreateCategoryCommandHandler> log)
    { _repo = repo; _cache = cache; _log = log; }

    public async Task<Result<CreatedCategoryResponse>> Handle(
        CreateCategoryCommand cmd, CancellationToken ct)
    {
        var (visibleIn, visibleInError) = VisibleInInput.Parse(cmd.VisibleIn);
        if (visibleInError is not null)
            return Result.Failure<CreatedCategoryResponse>(visibleInError);

        var category = new CatalogCategory(Guid.NewGuid(), cmd.Name.Trim(), cmd.DisplayOrder);
        if (visibleIn is not null) category.SetVisibleIn(visibleIn);
        await _repo.AddCategoryAsync(cmd.TenantId, category, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        _log.LogInformation(
            "catalog.admin.category.created tenantId={TenantId} categoryId={Id} name={Name} requesterId={RequesterId}",
            cmd.TenantId, category.Id, category.Name, cmd.RequesterId);

        return Result.Success(new CreatedCategoryResponse(
            category.Id, category.Name, category.DisplayOrder, category.CreatedAt, category.VisibleIn));
    }
}

// ─── Update Category ──────────────────────────────────────────────────────────

public sealed record UpdateCategoryCommand(
    Guid TenantId, Guid CategoryId, string? Name, int? DisplayOrder, Guid RequesterId,
    List<string>? VisibleIn = null
) : IRequest<Result<UpdatedCategoryResponse>>;

public sealed record UpdatedCategoryResponse(
    Guid Id, string Name, int DisplayOrder, DateTime CreatedAt, IReadOnlyList<string> VisibleIn
);

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x).Must(x => x.Name is not null || x.DisplayOrder is not null || x.VisibleIn is not null)
            .WithMessage("Al menos un campo debe estar presente en el body.");
        When(x => x.Name is not null, () =>
        {
            RuleFor(x => x.Name!).NotEmpty().MaximumLength(100)
                .Must(n => !string.IsNullOrWhiteSpace(n))
                .WithMessage("name no puede ser solo espacios en blanco.");
        });
        When(x => x.DisplayOrder is not null, () =>
            RuleFor(x => x.DisplayOrder!.Value).GreaterThanOrEqualTo(0));
    }
}

public sealed class UpdateCategoryCommandHandler
    : IRequestHandler<UpdateCategoryCommand, Result<UpdatedCategoryResponse>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<UpdateCategoryCommandHandler> _log;

    public UpdateCategoryCommandHandler(
        ICatalogRepository repo, ICatalogCacheService cache,
        ILogger<UpdateCategoryCommandHandler> log)
    { _repo = repo; _cache = cache; _log = log; }

    public async Task<Result<UpdatedCategoryResponse>> Handle(
        UpdateCategoryCommand cmd, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var existing = catalog?.Categories.FirstOrDefault(c => c.Id == cmd.CategoryId);
        if (existing is null)
            return Result.Failure<UpdatedCategoryResponse>(
                Error.NotFound("Categoría no encontrada."));

        var (visibleIn, visibleInError) = VisibleInInput.Parse(cmd.VisibleIn);
        if (visibleInError is not null)
            return Result.Failure<UpdatedCategoryResponse>(visibleInError);

        // Cambiar el visibleIn de la categoría no toca el de sus ítems (SDD-002 §PATCH categories).
        await _repo.UpdateCategoryAsync(cmd.TenantId, cmd.CategoryId,
            cmd.Name?.Trim(), cmd.DisplayOrder, visibleIn, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        // Return updated state
        var name = cmd.Name?.Trim() ?? existing.Name;
        var order = cmd.DisplayOrder ?? existing.DisplayOrder;

        _log.LogInformation(
            "catalog.admin.category.updated tenantId={TenantId} categoryId={Id} requesterId={RequesterId}",
            cmd.TenantId, cmd.CategoryId, cmd.RequesterId);

        return Result.Success(new UpdatedCategoryResponse(
            cmd.CategoryId, name, order, existing.CreatedAt, visibleIn ?? existing.VisibleIn));
    }
}

// ─── Delete Category ──────────────────────────────────────────────────────────

public sealed record DeleteCategoryCommand(
    Guid TenantId, Guid CategoryId, Guid RequesterId
) : IRequest<Result<Unit>>;

public sealed class DeleteCategoryCommandHandler
    : IRequestHandler<DeleteCategoryCommand, Result<Unit>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<DeleteCategoryCommandHandler> _log;

    public DeleteCategoryCommandHandler(
        ICatalogRepository repo, ICatalogCacheService cache,
        ILogger<DeleteCategoryCommandHandler> log)
    { _repo = repo; _cache = cache; _log = log; }

    public async Task<Result<Unit>> Handle(DeleteCategoryCommand cmd, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var category = catalog?.Categories.FirstOrDefault(c => c.Id == cmd.CategoryId);
        if (category is null)
            return Result.Failure<Unit>(Error.NotFound("Categoría no encontrada."));

        var activeItems = catalog!.Items.Count(
            i => i.CategoryId == cmd.CategoryId && !i.IsDeleted);

        if (activeItems > 0)
        {
            _log.LogInformation(
                "catalog.admin.category.delete_blocked tenantId={TenantId} categoryId={Id} activeItemCount={Count} requesterId={RequesterId}",
                cmd.TenantId, cmd.CategoryId, activeItems, cmd.RequesterId);
            return Result.Failure<Unit>(
                Error.Conflict("La categoría tiene uno o más items activos asociados."));
        }

        await _repo.DeleteCategoryAsync(cmd.TenantId, cmd.CategoryId, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        _log.LogInformation(
            "catalog.admin.category.deleted tenantId={TenantId} categoryId={Id} requesterId={RequesterId}",
            cmd.TenantId, cmd.CategoryId, cmd.RequesterId);

        return Result.Success(Unit.Value);
    }
}
