using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Sessions.Application.Commands;
using MestoBeach.Sessions.Application.Queries;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Sessions;

[Route("api/v1")]
public sealed class FloorsController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public FloorsController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    private Guid TenantId => (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

    // ── GET /api/v1/floors ──────────────────────────────────────────────────────
    [HttpGet("floors")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdminOrCajero)]
    public async Task<IActionResult> ListFloors(CancellationToken ct) =>
        ToResponse(await Mediator.Send(new ListFloorsQuery(TenantId), ct));

    // ── GET /api/v1/floorplan ───────────────────────────────────────────────────
    // Pisos + mesas + estado (Libre/Ocupada). Solo LECTURA del layout: lo consumen TODAS las
    // vistas operativas (editor admin, mapa de caja, PDA, Recepción, Salón y Runner), así que
    // lo puede leer cualquier staff autenticado. La edición del plano sigue restringida a StaffAdmin.
    [HttpGet("floorplan")]
    [Authorize(Policy = AuthorizationPolicies.StaffJwt)]
    // [PRD-019] ?mode=RESTAURANT|NIGHT (default: modo activo), ?includeHidden (solo Admin),
    // ?includeInactive (Host y Admin).
    public async Task<IActionResult> GetFloorPlan(
        [FromQuery] string? mode,
        [FromQuery] bool includeHidden,
        [FromQuery] bool includeInactive,
        CancellationToken ct)
    {
        // Rol efectivo para los flags: Admin manda sobre Host si el usuario tiene los dos.
        string? role = _currentUser.HasRole("Admin") ? "Admin"
            : _currentUser.HasRole("Host") ? "Host"
            : _currentUser.Role;
        return ToResponse(await Mediator.Send(
            new GetFloorPlanQuery(TenantId, mode, includeHidden, includeInactive, role), ct));
    }

    // ── POST /api/v1/floors ─────────────────────────────────────────────────────
    [HttpPost("floors")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> CreateFloor(
        [FromBody] CreateFloorRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new CreateFloorCommand(TenantId, req.Name, _currentUser.UserId), ct), successStatus: 201);

    // ── PATCH /api/v1/floors/reorder ────────────────────────────────────────────
    [HttpPatch("floors/reorder")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> ReorderFloors(
        [FromBody] ReorderFloorsRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new ReorderFloorsCommand(TenantId, req.OrderedFloorIds, _currentUser.UserId), ct));

    // ── PATCH /api/v1/floors/{floorId} ──── nombre y visibleIn (PRD-019) ──────────────────────────────
    [HttpPatch("floors/{floorId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> RenameFloor(
        Guid floorId, [FromBody] RenameFloorRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new RenameFloorCommand(TenantId, floorId, req.Name, _currentUser.UserId, req.VisibleIn), ct));

    // ── PATCH /api/v1/floors/{floorId}/layout ──── canvas + walls ───────────────
    [HttpPatch("floors/{floorId:guid}/layout")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> UpdateFloorLayout(
        Guid floorId, [FromBody] UpdateFloorLayoutRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new UpdateFloorLayoutCommand(TenantId, floorId, req.Width, req.Height,
                req.Walls?.Select(w => new MestoBeach.Sessions.Domain.Entities.WallSegment(w.X1, w.Y1, w.X2, w.Y2)).ToList(),
                _currentUser.UserId), ct));

    // ── DELETE /api/v1/floors/{floorId} ─────────────────────────────────────────
    [HttpDelete("floors/{floorId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> DeleteFloor(Guid floorId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(new DeleteFloorCommand(TenantId, floorId, _currentUser.UserId), ct));
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public sealed record CreateFloorRequest(string Name);
public sealed record RenameFloorRequest(string? Name, List<string>? VisibleIn = null);
public sealed record ReorderFloorsRequest(List<Guid> OrderedFloorIds);
public sealed record WallSegmentRequest(double X1, double Y1, double X2, double Y2);
public sealed record UpdateFloorLayoutRequest(int? Width, int? Height, List<WallSegmentRequest>? Walls);
