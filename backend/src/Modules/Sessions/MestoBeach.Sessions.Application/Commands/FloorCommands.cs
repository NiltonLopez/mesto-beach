using FluentValidation;
using MediatR;
using MestoBeach.Sessions.Domain.Entities;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Plans;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;
using MestoBeach.Sessions.Application.Common;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Sessions.Application.Commands;

// ─── Shared DTO ───────────────────────────────────────────────────────────────

public record FloorDto(
    Guid Id,
    Guid TenantId,
    string Name,
    int SortOrder,
    int Width,
    int Height,
    List<WallSegment> Walls,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<string> VisibleIn)
{
    public static FloorDto From(Floor f) =>
        new(f.Id, f.TenantId, f.Name, f.SortOrder, f.Width, f.Height,
            f.Walls, f.CreatedAt, f.UpdatedAt, f.VisibleIn);
}

internal static class FloorDefaults
{
    public const int Width = 1000;
    public const int Height = 700;
}

// ══════════════════════════════════════════════════════════════════════════════
// CREATE FLOOR
// ══════════════════════════════════════════════════════════════════════════════

public record CreateFloorCommand(Guid TenantId, string Name, Guid RequesterId)
    : IRequest<Result<FloorDto>>;

public sealed class CreateFloorValidator : AbstractValidator<CreateFloorCommand>
{
    public CreateFloorValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("El nombre del piso es requerido.")
            .MaximumLength(100);
    }
}

public sealed class CreateFloorHandler : IRequestHandler<CreateFloorCommand, Result<FloorDto>>
{
    private readonly IFloorRepository _floors;
    private readonly IPlanEntitlementsReader _entitlements;
    private readonly ILogger<CreateFloorHandler> _logger;

    public CreateFloorHandler(
        IFloorRepository floors, IPlanEntitlementsReader entitlements, ILogger<CreateFloorHandler> logger)
    {
        _floors = floors;
        _entitlements = entitlements;
        _logger = logger;
    }

    public async Task<Result<FloorDto>> Handle(CreateFloorCommand cmd, CancellationToken ct)
    {
        var existing = await _floors.ListAsync(cmd.TenantId, ct);

        var ent = await _entitlements.GetAsync(cmd.TenantId, ct);
        if (ent is not null && ent.ExceedsFloors(existing.Count))
            return Result.Failure<FloorDto>(
                PlanLimits.Exceeded("pisos", ent.MaxFloors!.Value, existing.Count, ent.PlanCode));

        int nextOrder = existing.Count == 0 ? 0 : existing.Max(f => f.SortOrder) + 1;

        var floor = new Floor(Guid.NewGuid(), cmd.TenantId, cmd.Name.Trim(), nextOrder,
            FloorDefaults.Width, FloorDefaults.Height);
        await _floors.AddAsync(floor, ct);
        await _floors.SaveChangesAsync(ct);

        _logger.LogInformation("sessions.admin.floor.created {TenantId} {FloorId} {Name} {RequesterId}",
            cmd.TenantId, floor.Id, floor.Name, cmd.RequesterId);

        return Result.Success(FloorDto.From(floor));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// UPDATE FLOOR — nombre y modos en los que existe (PATCH /floors/{id}, PRD-019)
// ══════════════════════════════════════════════════════════════════════════════

/// <param name="Name">null = sin cambio.</param>
/// <param name="VisibleIn">null = sin cambio; si llega, no vacío y dentro de {RESTAURANT, NIGHT}.</param>
public record RenameFloorCommand(Guid TenantId, Guid FloorId, string? Name, Guid RequesterId,
    List<string>? VisibleIn = null)
    : IRequest<Result<FloorDto>>;

public sealed class RenameFloorValidator : AbstractValidator<RenameFloorCommand>
{
    public RenameFloorValidator()
    {
        RuleFor(c => c.FloorId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100).When(c => c.Name is not null);
        RuleFor(c => c)
            .Must(c => c.Name is not null || c.VisibleIn is not null)
            .WithMessage("Body vacío: debe proveer name o visibleIn.");
    }
}

public sealed class RenameFloorHandler : IRequestHandler<RenameFloorCommand, Result<FloorDto>>
{
    private readonly IFloorRepository _floors;
    private readonly ITableRepository _tables;
    private readonly ISessionRepository _sessions;
    private readonly IVenueModeProvider _venueMode;
    private readonly ISignalRNotifier _signalR;
    private readonly ILogger<RenameFloorHandler> _logger;

    public RenameFloorHandler(
        IFloorRepository floors, ITableRepository tables, ISessionRepository sessions,
        IVenueModeProvider venueMode, ISignalRNotifier signalR, ILogger<RenameFloorHandler> logger)
    {
        _floors = floors;
        _tables = tables;
        _sessions = sessions;
        _venueMode = venueMode;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<FloorDto>> Handle(RenameFloorCommand cmd, CancellationToken ct)
    {
        // visibleIn: no vacío, valores conocidos; se normaliza, deduplica y ordena canónicamente.
        List<string>? visibleIn = null;
        if (cmd.VisibleIn is not null)
        {
            if (cmd.VisibleIn.Count == 0)
                return Result.Failure<FloorDto>(
                    Error.Validation("VISIBLE_IN_EMPTY", "visibleIn no puede estar vacío."));
            var parsed = cmd.VisibleIn.Select(TableModeRules.ParseMode).ToList();
            if (parsed.Any(m => m is null))
                return Result.Failure<FloorDto>(
                    Error.Validation(TableModeRules.InvalidModeCode, "visibleIn solo admite RESTAURANT y NIGHT."));
            visibleIn = TableModeRules.Modes.Where(m => parsed.Contains(m)).ToList();
        }

        var floor = await _floors.GetByIdAsync(cmd.FloorId, ct);
        if (floor is null || floor.TenantId != cmd.TenantId)
            return Result.Failure<FloorDto>(
                new Error("FLOOR_NOT_FOUND", "Piso no encontrado.", ErrorType.NotFound));

        var changed = new List<string>();
        bool activeVisibilityChanged = false;

        if (visibleIn is not null && !visibleIn.SequenceEqual(floor.VisibleIn))
        {
            var modeResult = await _venueMode.GetModeAsync(cmd.TenantId, ct);
            if (modeResult.IsFailure) return Result.Failure<FloorDto>(modeResult.Error);
            var activeMode = modeResult.Value;

            bool wasVisible = floor.IsVisibleIn(activeMode);
            bool willBeVisible = visibleIn.Contains(activeMode);
            activeVisibilityChanged = wasVisible != willBeVisible;

            // Ocultar el modo activo con mesas del piso ocupadas → 409 con sus códigos.
            if (wasVisible && !willBeVisible)
            {
                var onFloor = (await _tables.ListLayoutsAsync(cmd.TenantId, activeMode, ct))
                    .Where(l => l.FloorId == floor.Id).Select(l => l.TableId).ToHashSet();
                var busy = (await _sessions.GetOpenByTenantAsync(cmd.TenantId, ct))
                    .Where(s => onFloor.Contains(s.TableId))
                    .Select(s => s.TableCode).Distinct().ToList();
                if (busy.Count > 0)
                    return Result.Failure<FloorDto>(new Error("FLOOR_HAS_ACTIVE_SESSIONS",
                        "No se puede cerrar el piso en el modo activo: tiene mesas ocupadas.", ErrorType.Conflict,
                        new Dictionary<string, object?> { ["tableCodes"] = busy }));
            }

            floor.SetVisibleIn(visibleIn);
            changed.Add("visibleIn");
            SessionsMetrics.FloorVisibilityUpdated.Add(1);
        }

        if (cmd.Name is not null)
        {
            floor.Rename(cmd.Name.Trim());
            changed.Add("name");
        }

        await _floors.SaveChangesAsync(ct);

        _logger.LogInformation(
            "sessions.admin.floor.updated {TenantId} {FloorId} {ChangedFields} {VisibleIn} {RequesterId}",
            cmd.TenantId, floor.Id, string.Join(",", changed), string.Join(",", floor.VisibleIn), cmd.RequesterId);

        if (activeVisibilityChanged)
        {
            try
            {
                await _signalR.NotifyWaitersAsync(cmd.TenantId,
                    new { type = "FLOOR_UPDATED", floorId = floor.Id, visibleIn = floor.VisibleIn }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "sessions.admin.floor.updated_notify_failed {TenantId}", cmd.TenantId);
            }
        }

        return Result.Success(FloorDto.From(floor));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// REORDER FLOORS
// ══════════════════════════════════════════════════════════════════════════════

public record ReorderFloorsCommand(Guid TenantId, List<Guid> OrderedFloorIds, Guid RequesterId)
    : IRequest<Result<List<FloorDto>>>;

public sealed class ReorderFloorsValidator : AbstractValidator<ReorderFloorsCommand>
{
    public ReorderFloorsValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.OrderedFloorIds).NotEmpty();
    }
}

public sealed class ReorderFloorsHandler : IRequestHandler<ReorderFloorsCommand, Result<List<FloorDto>>>
{
    private readonly IFloorRepository _floors;
    public ReorderFloorsHandler(IFloorRepository floors) => _floors = floors;

    public async Task<Result<List<FloorDto>>> Handle(ReorderFloorsCommand cmd, CancellationToken ct)
    {
        var floors = await _floors.ListAsync(cmd.TenantId, ct);
        var byId = floors.ToDictionary(f => f.Id);
        for (int i = 0; i < cmd.OrderedFloorIds.Count; i++)
        {
            if (byId.TryGetValue(cmd.OrderedFloorIds[i], out var f))
                f.SetOrder(i);
        }
        await _floors.SaveChangesAsync(ct);
        return Result.Success(floors.OrderBy(f => f.SortOrder).Select(FloorDto.From).ToList());
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// UPDATE FLOOR LAYOUT (canvas size + walls)
// ══════════════════════════════════════════════════════════════════════════════

public record UpdateFloorLayoutCommand(
    Guid TenantId, Guid FloorId, int? Width, int? Height, List<WallSegment>? Walls, Guid RequesterId)
    : IRequest<Result<FloorDto>>;

public sealed class UpdateFloorLayoutValidator : AbstractValidator<UpdateFloorLayoutCommand>
{
    public UpdateFloorLayoutValidator()
    {
        RuleFor(c => c.FloorId).NotEmpty();
        RuleFor(c => c.Width).InclusiveBetween(200, 5000).When(c => c.Width.HasValue);
        RuleFor(c => c.Height).InclusiveBetween(200, 5000).When(c => c.Height.HasValue);
    }
}

public sealed class UpdateFloorLayoutHandler : IRequestHandler<UpdateFloorLayoutCommand, Result<FloorDto>>
{
    private readonly IFloorRepository _floors;
    public UpdateFloorLayoutHandler(IFloorRepository floors) => _floors = floors;

    public async Task<Result<FloorDto>> Handle(UpdateFloorLayoutCommand cmd, CancellationToken ct)
    {
        var floor = await _floors.GetByIdAsync(cmd.FloorId, ct);
        if (floor is null || floor.TenantId != cmd.TenantId)
            return Result.Failure<FloorDto>(Error.NotFound("Piso no encontrado."));

        if (cmd.Width.HasValue || cmd.Height.HasValue)
            floor.SetCanvas(cmd.Width ?? floor.Width, cmd.Height ?? floor.Height);
        if (cmd.Walls is not null)
            floor.SetWalls(cmd.Walls);

        await _floors.SaveChangesAsync(ct);
        return Result.Success(FloorDto.From(floor));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// DELETE FLOOR  (tables on it are unplaced, not deleted)
// ══════════════════════════════════════════════════════════════════════════════

public record DeleteFloorCommand(Guid TenantId, Guid FloorId, Guid RequesterId)
    : IRequest<Result<bool>>;

public sealed class DeleteFloorHandler : IRequestHandler<DeleteFloorCommand, Result<bool>>
{
    private readonly IFloorRepository _floors;
    private readonly ITableRepository _tables;
    private readonly ILogger<DeleteFloorHandler> _logger;

    public DeleteFloorHandler(IFloorRepository floors, ITableRepository tables, ILogger<DeleteFloorHandler> logger)
    {
        _floors = floors;
        _tables = tables;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(DeleteFloorCommand cmd, CancellationToken ct)
    {
        var floor = await _floors.GetByIdAsync(cmd.FloorId, ct);
        if (floor is null || floor.TenantId != cmd.TenantId)
            return Result.Failure<bool>(Error.NotFound("Piso no encontrado."));

        // Unplace (do not delete) the table_layouts rows on this floor, in every mode — "Sin ubicar".
        // (La FK ON DELETE SET NULL haría lo mismo; se hace explícito para conservar el contador del log.)
        var tables = await _tables.ListLayoutsByFloorAsync(cmd.FloorId, ct);
        foreach (var t in tables)
            t.SetPlacement(null, null, null);
        if (tables.Count > 0)
            await _tables.SaveChangesAsync(ct);

        await _floors.RemoveAsync(floor, ct);
        await _floors.SaveChangesAsync(ct);

        _logger.LogInformation("sessions.admin.floor.deleted {TenantId} {FloorId} {UnplacedTables} {RequesterId}",
            cmd.TenantId, cmd.FloorId, tables.Count, cmd.RequesterId);

        return Result.Success(true);
    }
}
