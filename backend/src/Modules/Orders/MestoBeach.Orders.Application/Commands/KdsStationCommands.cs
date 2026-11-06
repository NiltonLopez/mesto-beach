using FluentValidation;
using MediatR;
using MestoBeach.Orders.Application.Queries;
using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Plans;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Orders.Application.Commands;

// ══════════════════════════════════════════════════════════════════════════════
// CREATE KDS STATION
// ══════════════════════════════════════════════════════════════════════════════

/// <param name="IsActive">ADR-037. Ausente = true.</param>
/// <param name="FlowProfile">ADR-037. <c>FULL_PASS</c> | <c>BAR</c>; ausente = <c>FULL_PASS</c>.</param>
/// <param name="ActiveIn">ADR-037. Ausente = <c>["RESTAURANT","NIGHT"]</c>.</param>
public record CreateKdsStationCommand(
    Guid TenantId,
    string Name,
    List<string> CategoryIds,
    Guid RequesterId,
    bool? IsActive = null,
    string? FlowProfile = null,
    List<string>? ActiveIn = null) : IRequest<Result<KdsStationDto>>;

public sealed class CreateKdsStationValidator : AbstractValidator<CreateKdsStationCommand>
{
    public CreateKdsStationValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.Name)
            .NotEmpty().MinimumLength(2).MaximumLength(100);
        RuleFor(c => c.CategoryIds)
            .NotNull()
            .Must(ids => ids.Count <= 50).WithMessage("Máximo 50 categoryIds por estación.")
            .ForEach(id => id
                .Matches(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")
                .WithMessage("Cada categoryId debe ser un GUID de categoría válido."));
    }
}

public sealed class CreateKdsStationHandler
    : IRequestHandler<CreateKdsStationCommand, Result<KdsStationDto>>
{
    private readonly IKdsStationRepository _stations;
    private readonly IPlanEntitlementsReader _entitlements;
    private readonly IVenueModeProvider _venueMode;
    private readonly ILogger<CreateKdsStationHandler> _logger;

    public CreateKdsStationHandler(
        IKdsStationRepository stations, IPlanEntitlementsReader entitlements,
        IVenueModeProvider venueMode,
        ILogger<CreateKdsStationHandler> logger)
    {
        _stations = stations;
        _entitlements = entitlements;
        _venueMode = venueMode;
        _logger = logger;
    }

    public async Task<Result<KdsStationDto>> Handle(CreateKdsStationCommand cmd, CancellationToken ct)
    {
        // Perfil y modos (ADR-037): ausentes valen los default de la migración.
        var (flowProfile, activeIn, profileError) = KdsStationRules.Normalize(cmd.FlowProfile, cmd.ActiveIn);
        if (profileError is not null)
            return Result.Failure<KdsStationDto>(profileError);

        // Name uniqueness (case-insensitive)
        var existing = await _stations.GetByNameAsync(cmd.TenantId, cmd.Name, ct);
        if (existing is not null)
            return Result.Failure<KdsStationDto>(
                Error.Conflict($"Ya existe una estación con el nombre '{cmd.Name}'."));

        // Plan limit: máximo de estaciones KDS por plan
        var ent = await _entitlements.GetAsync(cmd.TenantId, ct);
        if (ent is not null && ent.MaxKdsStations is int maxStations)
        {
            var stationCount = (await _stations.GetByTenantAsync(cmd.TenantId, ct)).Count;
            if (ent.ExceedsKdsStations(stationCount))
                return Result.Failure<KdsStationDto>(
                    PlanLimits.Exceeded("estaciones KDS", maxStations, stationCount, ent.PlanCode));
        }

        // Category uniqueness across tenant stations
        if (cmd.CategoryIds.Count > 0)
        {
            var allStations = await _stations.GetByTenantAsync(cmd.TenantId, ct);
            var conflicts = cmd.CategoryIds
                .Where(id => allStations.Any(s => s.CategoryIds.Contains(id)))
                .ToList();
            if (conflicts.Count > 0)
                return Result.Failure<KdsStationDto>(
                    Error.Validation("CATEGORY_CONFLICT",
                        $"CategoryIds ya asignados a otra estación: {string.Join(", ", conflicts)}"));
        }

        var station = new KdsStation(
            Guid.NewGuid(), cmd.TenantId, cmd.Name, cmd.CategoryIds,
            cmd.IsActive ?? true, flowProfile ?? KdsStation.FullPass, activeIn ?? KdsStation.AllModes());
        await _stations.AddAsync(station, ct);
        await _stations.SaveChangesAsync(ct);

        _logger.LogInformation(
            "kds_station.created {StationId} {TenantId} {Name} {CategoryCount} {CreatedBy} {FlowProfile} {ActiveIn} {IsActive}",
            station.Id, cmd.TenantId, cmd.Name, cmd.CategoryIds.Count, cmd.RequesterId,
            station.FlowProfile, string.Join(",", station.ActiveIn), station.IsActive);

        // isOn con el modo vigente: una estación activeIn [NIGHT] creada de día nace apagada.
        var mode = await _venueMode.GetAsync(cmd.TenantId, ct);
        return Result.Success(KdsStationDto.From(station, mode));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// UPDATE KDS STATION
// ══════════════════════════════════════════════════════════════════════════════

public record UpdateKdsStationDto(
    Guid Id, Guid TenantId, string Name, List<string> CategoryIds, DateTime UpdatedAt,
    bool IsActive, string FlowProfile, List<string> ActiveIn, bool IsOn);

public record UpdateKdsStationCommand(
    Guid StationId,
    Guid TenantId,
    string? Name,
    List<string>? CategoryIds,
    Guid RequesterId,
    bool? IsActive = null,
    string? FlowProfile = null,
    List<string>? ActiveIn = null) : IRequest<Result<UpdateKdsStationDto>>;

public sealed class UpdateKdsStationValidator : AbstractValidator<UpdateKdsStationCommand>
{
    public UpdateKdsStationValidator()
    {
        RuleFor(c => c.StationId).NotEmpty();
        RuleFor(c => c.Name)
            .MinimumLength(2).MaximumLength(100).When(c => c.Name is not null);
        RuleFor(c => c.CategoryIds)
            .Must(ids => ids!.Count <= 50).When(c => c.CategoryIds is not null)
            .WithMessage("Máximo 50 categoryIds.")
            .ForEach(id => id
                .Matches(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")
                .WithMessage("Cada categoryId debe ser un GUID de categoría válido."))
            .When(c => c.CategoryIds is not null);
        RuleFor(c => c)
            .Must(c => c.Name is not null || c.CategoryIds is not null
                    || c.IsActive is not null || c.FlowProfile is not null || c.ActiveIn is not null)
            .WithMessage("Body vacío: debe proveer name, categoryIds, isActive, flowProfile o activeIn.");
    }
}

public sealed class UpdateKdsStationHandler
    : IRequestHandler<UpdateKdsStationCommand, Result<UpdateKdsStationDto>>
{
    private readonly IKdsStationRepository _stations;
    private readonly IVenueModeProvider _venueMode;
    private readonly ILogger<UpdateKdsStationHandler> _logger;

    public UpdateKdsStationHandler(
        IKdsStationRepository stations, IVenueModeProvider venueMode,
        ILogger<UpdateKdsStationHandler> logger)
    {
        _stations = stations;
        _venueMode = venueMode;
        _logger = logger;
    }

    public async Task<Result<UpdateKdsStationDto>> Handle(
        UpdateKdsStationCommand cmd, CancellationToken ct)
    {
        var station = await _stations.GetByIdAsync(cmd.StationId, ct);
        if (station is null)
            return Result.Failure<UpdateKdsStationDto>(Error.NotFound("Estación no encontrada."));

        if (station.TenantId != cmd.TenantId)
            return Result.Failure<UpdateKdsStationDto>(
                Error.Forbidden("La estación pertenece a otro tenant."));

        var (flowProfile, activeIn, profileError) = KdsStationRules.Normalize(cmd.FlowProfile, cmd.ActiveIn);
        if (profileError is not null)
            return Result.Failure<UpdateKdsStationDto>(profileError);

        var allStations = await _stations.GetByTenantAsync(cmd.TenantId, ct);
        var otherStations = allStations.Where(s => s.Id != cmd.StationId).ToList();

        if (cmd.Name is not null)
        {
            bool nameTaken = otherStations.Any(
                s => string.Equals(s.Name.Trim(), cmd.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (nameTaken)
                return Result.Failure<UpdateKdsStationDto>(
                    Error.Conflict($"Ya existe una estación con el nombre '{cmd.Name}'."));
            station.UpdateName(cmd.Name);
        }

        if (cmd.CategoryIds is not null)
        {
            var conflicts = cmd.CategoryIds
                .Where(id => otherStations.Any(s => s.CategoryIds.Contains(id)))
                .ToList();
            if (conflicts.Count > 0)
                return Result.Failure<UpdateKdsStationDto>(
                    Error.Validation("CATEGORY_CONFLICT",
                        $"CategoryIds en conflicto: {string.Join(", ", conflicts)}"));
            station.UpdateCategoryIds(cmd.CategoryIds);
        }

        // Apagar se permite aunque tenga platos vivos (ADR-037): se terminan en su tablero; solo
        // las comandas nuevas la rechazan con STATION_CLOSED.
        if (cmd.IsActive is bool isActive) station.SetActive(isActive);
        if (flowProfile is not null) station.SetFlowProfile(flowProfile);
        if (activeIn is not null) station.SetActiveIn(activeIn);

        await _stations.SaveChangesAsync(ct);

        _logger.LogInformation(
            "kds_station.updated {StationId} {TenantId} {UpdatedBy} {FlowProfile} {ActiveIn} {IsActive}",
            cmd.StationId, cmd.TenantId, cmd.RequesterId,
            station.FlowProfile, string.Join(",", station.ActiveIn), station.IsActive);

        var mode = await _venueMode.GetAsync(cmd.TenantId, ct);
        return Result.Success(
            new UpdateKdsStationDto(
                station.Id, station.TenantId, station.Name, station.CategoryIds, station.UpdatedAt,
                station.IsActive, station.FlowProfile, station.ActiveIn, station.IsOnIn(mode)));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// PERFIL DE FLUJO Y MODOS (ADR-037)
// ══════════════════════════════════════════════════════════════════════════════

public static class KdsStationRules
{
    /// <summary>
    /// Normaliza <c>flowProfile</c> (case-insensitive, se guarda en mayúsculas) y <c>activeIn</c>
    /// (mayúsculas, sin repetidos). Devuelve null en lo que no venga. Errores:
    /// <c>INVALID_FLOW_PROFILE</c>, <c>ACTIVE_IN_EMPTY</c>, <c>INVALID_MODE</c>.
    /// </summary>
    public static (string? FlowProfile, List<string>? ActiveIn, Error? Error) Normalize(
        string? flowProfile, List<string>? activeIn)
    {
        string? profile = null;
        if (flowProfile is not null)
        {
            profile = KdsStation.FlowProfiles.FirstOrDefault(
                p => string.Equals(p, flowProfile.Trim(), StringComparison.OrdinalIgnoreCase));
            if (profile is null)
                return (null, null, Error.Validation("INVALID_FLOW_PROFILE",
                    "flowProfile debe ser FULL_PASS o BAR."));
        }

        List<string>? modes = null;
        if (activeIn is not null)
        {
            if (activeIn.Count == 0)
                return (null, null, Error.Validation("ACTIVE_IN_EMPTY",
                    "activeIn necesita al menos un modo (RESTAURANT o NIGHT)."));

            modes = [];
            foreach (var raw in activeIn)
            {
                // Enum.GetNames + OrdinalIgnoreCase, no Enum.TryParse (aceptaría "1").
                var name = Enum.GetNames<VenueMode>().FirstOrDefault(
                    n => string.Equals(n, raw?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (name is null)
                    return (null, null, Error.Validation("INVALID_MODE",
                        "Cada modo de activeIn debe ser RESTAURANT o NIGHT."));
                if (modes.Contains(name))
                    return (null, null, Error.Validation("INVALID_MODE",
                        $"activeIn repite el modo {name}."));
                modes.Add(name);
            }
        }

        return (profile, modes, null);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// DELETE KDS STATION
// ══════════════════════════════════════════════════════════════════════════════

public record DeleteKdsStationCommand(
    Guid StationId,
    Guid TenantId,
    Guid RequesterId) : IRequest<Result>;

public sealed class DeleteKdsStationHandler : IRequestHandler<DeleteKdsStationCommand, Result>
{
    private readonly IKdsStationRepository _stations;
    private readonly IOrderRepository _orders;
    private readonly ILogger<DeleteKdsStationHandler> _logger;

    public DeleteKdsStationHandler(
        IKdsStationRepository stations,
        IOrderRepository orders,
        ILogger<DeleteKdsStationHandler> logger)
    {
        _stations = stations;
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result> Handle(DeleteKdsStationCommand cmd, CancellationToken ct)
    {
        var station = await _stations.GetByIdAsync(cmd.StationId, ct);
        if (station is null)
            return Result.Failure(Error.NotFound("Estación no encontrada."));

        if (station.TenantId != cmd.TenantId)
            return Result.Failure(Error.Forbidden("La estación pertenece a otro tenant."));

        bool hasActive = await _orders.HasActiveOrdersForStationAsync(cmd.StationId, ct);
        if (hasActive)
            return Result.Failure(
                Error.Conflict("Existen órdenes activas asignadas a esta estación."));

        await _stations.DeleteAsync(station, ct);
        await _stations.SaveChangesAsync(ct);

        _logger.LogInformation(
            "kds_station.deleted {StationId} {TenantId} {DeletedBy}",
            cmd.StationId, cmd.TenantId, cmd.RequesterId);

        return Result.Success();
    }
}
