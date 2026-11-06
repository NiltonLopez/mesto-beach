using FluentValidation;
using MediatR;
using MestoBeach.Catalog.Application.Common;
using MestoBeach.Catalog.Domain.Entities;
using MestoBeach.Catalog.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Catalog.Application.Commands;

// ─── Shared DTOs ──────────────────────────────────────────────────────────────

public sealed record ModifierOptionDto(
    Guid Id, string Name, int PriceDeltaCents, bool Available, bool IsDefault);

public sealed record ModifierGroupDto(
    Guid Id,
    string Name,
    string Type,               // SINGLE | MULTI | REMOVE
    bool Required,
    int MinSelect,
    int? MaxSelect,
    IReadOnlyList<ModifierOptionDto> Options,
    int UsedByItemCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record ModifierOptionInput(
    Guid? Id, string Name, int PriceDeltaCents, bool Available = true, bool IsDefault = false);

// ─── Mapping helpers ──────────────────────────────────────────────────────────

internal static class ModifierGroupMapping
{
    public static ModifierType ParseType(string type) => type?.Trim().ToUpperInvariant() switch
    {
        "SINGLE" => ModifierType.Single,
        "MULTI" => ModifierType.Multi,
        "REMOVE" => ModifierType.Remove,
        _ => ModifierType.Multi
    };

    public static List<ModifierOption> ToOptions(IEnumerable<ModifierOptionInput> options) =>
        options.Select(o => new ModifierOption(
            o.Id ?? Guid.NewGuid(),
            o.Name.Trim(),
            o.PriceDeltaCents,
            o.Available,
            o.IsDefault)).ToList();

    public static ModifierGroupDto ToDto(ModifierGroup g, int usedBy) =>
        new(g.Id, g.Name, g.Type.ToString().ToUpperInvariant(), g.Required,
            g.MinSelect, g.MaxSelect,
            g.Options.Select(o => new ModifierOptionDto(
                o.Id, o.Name, o.PriceDeltaCents, o.Available, o.IsDefault)).ToList(),
            usedBy, g.CreatedAt, g.UpdatedAt);
}

// ─── Create ───────────────────────────────────────────────────────────────────

public sealed record CreateModifierGroupCommand(
    Guid TenantId,
    string Name,
    string Type,
    bool Required,
    int MinSelect,
    int? MaxSelect,
    List<ModifierOptionInput> Options,
    Guid RequesterId
) : IRequest<Result<ModifierGroupDto>>;

public sealed class CreateModifierGroupValidator : AbstractValidator<CreateModifierGroupCommand>
{
    public CreateModifierGroupValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).Must(t =>
            t is "SINGLE" or "MULTI" or "REMOVE")
            .WithMessage("type debe ser SINGLE, MULTI o REMOVE.");
        RuleFor(x => x.MinSelect).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxSelect!.Value).GreaterThanOrEqualTo(x => x.MinSelect)
            .When(x => x.MaxSelect.HasValue)
            .WithMessage("maxSelect debe ser >= minSelect.");
        RuleFor(x => x.Options).NotEmpty().WithMessage("Un grupo debe tener al menos una opción.");
        RuleForEach(x => x.Options).ChildRules(o =>
        {
            o.RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
            o.RuleFor(x => x.PriceDeltaCents).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class CreateModifierGroupHandler
    : IRequestHandler<CreateModifierGroupCommand, Result<ModifierGroupDto>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<CreateModifierGroupHandler> _log;

    public CreateModifierGroupHandler(ICatalogRepository repo, ICatalogCacheService cache,
        ILogger<CreateModifierGroupHandler> log)
    { _repo = repo; _cache = cache; _log = log; }

    public async Task<Result<ModifierGroupDto>> Handle(
        CreateModifierGroupCommand cmd, CancellationToken ct)
    {
        var type = ModifierGroupMapping.ParseType(cmd.Type);
        var options = ModifierGroupMapping.ToOptions(cmd.Options);
        // REMOVE groups never cost money.
        if (type == ModifierType.Remove)
            options = options.Select(o => new ModifierOption(o.Id, o.Name, 0, o.Available, false)).ToList();

        var group = new ModifierGroup(Guid.NewGuid(), cmd.Name.Trim(), type,
            cmd.Required, cmd.MinSelect, cmd.MaxSelect, options);

        await _repo.AddModifierGroupAsync(cmd.TenantId, group, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        _log.LogInformation(
            "catalog.admin.modgroup.created tenantId={TenantId} groupId={Id} name={Name} type={Type} requesterId={RequesterId}",
            cmd.TenantId, group.Id, group.Name, type, cmd.RequesterId);

        return Result.Success(ModifierGroupMapping.ToDto(group, 0));
    }
}

// ─── Update ───────────────────────────────────────────────────────────────────

public sealed record UpdateModifierGroupCommand(
    Guid TenantId,
    Guid GroupId,
    string Name,
    string Type,
    bool Required,
    int MinSelect,
    int? MaxSelect,
    List<ModifierOptionInput> Options,
    Guid RequesterId
) : IRequest<Result<ModifierGroupDto>>;

public sealed class UpdateModifierGroupValidator : AbstractValidator<UpdateModifierGroupCommand>
{
    public UpdateModifierGroupValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).Must(t =>
            t is "SINGLE" or "MULTI" or "REMOVE")
            .WithMessage("type debe ser SINGLE, MULTI o REMOVE.");
        RuleFor(x => x.MinSelect).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxSelect!.Value).GreaterThanOrEqualTo(x => x.MinSelect)
            .When(x => x.MaxSelect.HasValue)
            .WithMessage("maxSelect debe ser >= minSelect.");
        RuleFor(x => x.Options).NotEmpty().WithMessage("Un grupo debe tener al menos una opción.");
        RuleForEach(x => x.Options).ChildRules(o =>
        {
            o.RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
            o.RuleFor(x => x.PriceDeltaCents).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class UpdateModifierGroupHandler
    : IRequestHandler<UpdateModifierGroupCommand, Result<ModifierGroupDto>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<UpdateModifierGroupHandler> _log;

    public UpdateModifierGroupHandler(ICatalogRepository repo, ICatalogCacheService cache,
        ILogger<UpdateModifierGroupHandler> log)
    { _repo = repo; _cache = cache; _log = log; }

    public async Task<Result<ModifierGroupDto>> Handle(
        UpdateModifierGroupCommand cmd, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var existing = catalog?.ModifierGroups.FirstOrDefault(g => g.Id == cmd.GroupId);
        if (existing is null)
            return Result.Failure<ModifierGroupDto>(
                Error.NotFound("Grupo de modificadores no encontrado."));

        var type = ModifierGroupMapping.ParseType(cmd.Type);
        var options = ModifierGroupMapping.ToOptions(cmd.Options);
        if (type == ModifierType.Remove)
            options = options.Select(o => new ModifierOption(o.Id, o.Name, 0, o.Available, false)).ToList();

        existing.Update(cmd.Name.Trim(), type, cmd.Required, cmd.MinSelect, cmd.MaxSelect, options);

        await _repo.UpdateModifierGroupAsync(cmd.TenantId, existing, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        var usedBy = catalog!.Items.Count(i => !i.IsDeleted && i.ModifierGroupIds.Contains(cmd.GroupId));

        _log.LogInformation(
            "catalog.admin.modgroup.updated tenantId={TenantId} groupId={Id} requesterId={RequesterId}",
            cmd.TenantId, cmd.GroupId, cmd.RequesterId);

        return Result.Success(ModifierGroupMapping.ToDto(existing, usedBy));
    }
}

// ─── Delete ───────────────────────────────────────────────────────────────────

public sealed record DeleteModifierGroupCommand(
    Guid TenantId, Guid GroupId, Guid RequesterId
) : IRequest<Result<Unit>>;

public sealed class DeleteModifierGroupHandler
    : IRequestHandler<DeleteModifierGroupCommand, Result<Unit>>
{
    private readonly ICatalogRepository _repo;
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<DeleteModifierGroupHandler> _log;

    public DeleteModifierGroupHandler(ICatalogRepository repo, ICatalogCacheService cache,
        ILogger<DeleteModifierGroupHandler> log)
    { _repo = repo; _cache = cache; _log = log; }

    public async Task<Result<Unit>> Handle(DeleteModifierGroupCommand cmd, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(cmd.TenantId, ct);
        var exists = catalog?.ModifierGroups.Any(g => g.Id == cmd.GroupId) ?? false;
        if (!exists)
            return Result.Failure<Unit>(Error.NotFound("Grupo de modificadores no encontrado."));

        // Detaches the group from every item that references it (handled in the repo).
        await _repo.DeleteModifierGroupAsync(cmd.TenantId, cmd.GroupId, ct);
        await _cache.InvalidateAsync(cmd.TenantId, ct);

        _log.LogInformation(
            "catalog.admin.modgroup.deleted tenantId={TenantId} groupId={Id} requesterId={RequesterId}",
            cmd.TenantId, cmd.GroupId, cmd.RequesterId);

        return Result.Success(Unit.Value);
    }
}
