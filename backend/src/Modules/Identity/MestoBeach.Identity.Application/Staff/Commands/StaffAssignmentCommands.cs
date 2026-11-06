using MediatR;
using Microsoft.Extensions.Logging;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Entities;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Application.Staff.Commands;

// ══════════════════════════════════════════════════════════════════════════════
// ASIGNACIONES DE MESTOBEACH (SDD-001 v2.0 — PRD-018)
//
// Un PDA atiende UN sector y un Runner sale de UNA estación. La asignación es fija por persona
// y solo la cambia Administración (D-09 de PRD-018): no es un turno que se elija al entrar.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Respuesta del endpoint de asignación, tal cual la describe SDD-001 v2.0.</summary>
public record StaffAssignmentDto(
    Guid UserId,
    IReadOnlyList<string> Roles,
    Guid? AssignedFloorId,
    Guid? AssignedStationId);

/// <summary>
/// Errores del caso de uso, con el código que espera el frontend. Se agrupan aquí —igual que en
/// Orders— para que el texto y el código no se dupliquen por el handler.
/// </summary>
public static class StaffAssignmentErrors
{
    /// <summary>
    /// El rol exige asignación y viene vacía. El SDD pide 422; la taxonomía compartida
    /// (<c>ErrorType</c>) no lo tiene, así que el controlador lo eleva a 422 por este código.
    /// </summary>
    public const string AssignmentRequiredCode = "ASSIGNMENT_REQUIRED";

    public static Error AssignmentRequired(string detalle) =>
        new(AssignmentRequiredCode, detalle, ErrorType.Validation);

    public static readonly Error MembershipNotFound = new(
        "NOT_FOUND",
        "Usuario sin membresía activa en este restaurante.",
        ErrorType.NotFound);

    public static readonly Error FloorNotFound = new(
        "FLOOR_NOT_FOUND",
        "El sector indicado no existe en este restaurante.",
        ErrorType.NotFound);

    public static readonly Error StationNotFound = new(
        "STATION_NOT_FOUND",
        "La estación indicada no existe en este restaurante.",
        ErrorType.NotFound);

    public static readonly Error RunnerHasOpenTrip = new(
        "RUNNER_HAS_OPEN_TRIP",
        "El runner tiene una ruta sin cerrar. Debe cerrarla o liberarla antes de cambiar de estación.",
        ErrorType.Conflict);
}

/// <summary>
/// PATCH /api/v1/identity/staff/{userId}/assignment. Fija el sector del PDA o la estación del
/// Runner. Ambos campos llegan siempre: es un reemplazo del estado de asignación, no un parche
/// campo a campo, para que "quitarle el sector" sea expresable enviando <c>null</c>.
/// </summary>
public record UpdateStaffAssignmentCommand(
    Guid AdminId,
    Guid AdminTenantId,
    Guid TargetUserId,
    Guid? AssignedFloorId,
    Guid? AssignedStationId) : IRequest<Result<StaffAssignmentDto>>;

public sealed class UpdateStaffAssignmentHandler
    : IRequestHandler<UpdateStaffAssignmentCommand, Result<StaffAssignmentDto>>
{
    private readonly IUserMembershipRepository _memberships;
    private readonly IFloorReadService _floors;
    private readonly IStationReadService _stations;
    private readonly IDeliveryTripReadService _trips;
    private readonly IGlobalInvalidationService _globalInvalidation;
    private readonly IStaffStationResolver _stationCache;
    private readonly IAuditLogService _audit;
    private readonly ILogger<UpdateStaffAssignmentHandler> _log;

    public UpdateStaffAssignmentHandler(
        IUserMembershipRepository memberships,
        IFloorReadService floors,
        IStationReadService stations,
        IDeliveryTripReadService trips,
        IGlobalInvalidationService globalInvalidation,
        IStaffStationResolver stationCache,
        IAuditLogService audit,
        ILogger<UpdateStaffAssignmentHandler> log)
    {
        _stationCache = stationCache;
        _memberships = memberships;
        _floors = floors;
        _stations = stations;
        _trips = trips;
        _globalInvalidation = globalInvalidation;
        _audit = audit;
        _log = log;
    }

    public async Task<Result<StaffAssignmentDto>> Handle(
        UpdateStaffAssignmentCommand cmd, CancellationToken ct)
    {
        // 1. La membresía debe existir y estar activa en el restaurante del token.
        var membership = await _memberships.GetByUserAndTenantAsync(
            cmd.TargetUserId, cmd.AdminTenantId, ct);
        if (membership is null || !membership.IsActive)
            return Result.Failure<StaffAssignmentDto>(StaffAssignmentErrors.MembershipNotFound);

        // 2. El rol manda: PDA sin sector y Runner sin estación no se pueden guardar. Quien es
        //    las dos cosas necesita las dos asignaciones.
        if (membership.RequiresFloor && cmd.AssignedFloorId is null)
            return Result.Failure<StaffAssignmentDto>(StaffAssignmentErrors.AssignmentRequired(
                "El rol PDA necesita un sector asignado."));

        if (membership.RequiresStation && cmd.AssignedStationId is null)
            return Result.Failure<StaffAssignmentDto>(StaffAssignmentErrors.AssignmentRequired(
                "El rol Runner necesita una estación asignada."));

        // 3. Las referencias deben existir dentro de este restaurante. No hay FK que lo garantice:
        //    floors es de Sessions y kds_stations de Orders (ver AssignmentReadModels.cs).
        if (cmd.AssignedFloorId is { } floorId &&
            !await _floors.ExistsAsync(cmd.AdminTenantId, floorId, ct))
            return Result.Failure<StaffAssignmentDto>(StaffAssignmentErrors.FloorNotFound);

        if (cmd.AssignedStationId is { } stationId &&
            !await _stations.ExistsAsync(cmd.AdminTenantId, stationId, ct))
            return Result.Failure<StaffAssignmentDto>(StaffAssignmentErrors.StationNotFound);

        var cambiaEstacion = membership.AssignedStationId != cmd.AssignedStationId;
        var cambiaSector = membership.AssignedFloorId != cmd.AssignedFloorId;

        // 4. Con la bandeja reclamada no se cambia de estación: esos platos salieron de la
        //    anterior y quedarían sin dueño (ADR-027). Primero cierra o libera la ruta.
        if (cambiaEstacion &&
            await _trips.HasOpenTripAsync(cmd.AdminTenantId, cmd.TargetUserId, ct))
            return Result.Failure<StaffAssignmentDto>(StaffAssignmentErrors.RunnerHasOpenTrip);

        // 5. Si no cambia nada, no se toca la base ni se invalida el token: reasignar a lo mismo
        //    no debería echar a nadie de su sesión.
        if (!cambiaEstacion && !cambiaSector)
            return Result.Success(Map(membership));

        membership.SetAssignment(cmd.AssignedFloorId, cmd.AssignedStationId);
        await _memberships.UpdateAsync(membership, ct);

        // 6. SDD-001 v2.1 §7 / ADR-038: la estación ya no se lee del token sino de la base (con
        //    caché staff_station de 60 s), así que basta con borrar la caché tras el commit. Solo
        //    si cambia el sector —que sigue viajando en el JWT— se invalida el token, como en v2.0.
        if (cambiaSector)
            await _globalInvalidation.InvalidateAllAsync(cmd.TargetUserId, ct);
        await _stationCache.InvalidateAsync(cmd.TargetUserId, cmd.AdminTenantId, ct);

        var roles = membership.Roles.Select(r => r.ToString()).ToList();
        await _audit.RecordAsync(
            "staff_assignment_changed", cmd.AdminId, cmd.TargetUserId.ToString(), cmd.AdminTenantId,
            new Dictionary<string, object?>
            {
                ["roles"] = roles,
                ["floorId"] = cmd.AssignedFloorId,
                ["stationId"] = cmd.AssignedStationId,
                ["changedBy"] = cmd.AdminId
            },
            ct);

        _log.LogInformation(
            "staff_assignment_changed userId={UserId} tenantId={TenantId} floorId={FloorId} stationId={StationId} changedBy={ChangedBy}",
            cmd.TargetUserId, cmd.AdminTenantId, cmd.AssignedFloorId, cmd.AssignedStationId, cmd.AdminId);

        return Result.Success(Map(membership));
    }

    private static StaffAssignmentDto Map(UserMembership m) => new(
        m.UserId,
        m.Roles.Select(r => r.ToString()).ToList(),
        m.AssignedFloorId,
        m.AssignedStationId);
}
