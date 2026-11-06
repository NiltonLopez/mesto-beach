using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Orders.Application.Commands;
using MestoBeach.Orders.Application.Queries;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Orders;

/// <summary>
/// GET    /api/v1/kds-stations              - List KDS stations (Staff)
/// POST   /api/v1/kds-stations              - Create KDS station (Admin)
/// PUT    /api/v1/kds-stations/{id}         - Update KDS station (Admin)
/// DELETE /api/v1/kds-stations/{id}         - Delete KDS station (Admin)
/// </summary>
[Route("api/v1/kds-stations")]
public sealed class KdsStationsController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public KdsStationsController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    private Guid TenantId => (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

    // ── GET /api/v1/kds-stations ──────────────────────────────────────────────

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.StaffJwt)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        ToResponse(await Mediator.Send(new ListKdsStationsQuery(
            TenantId: TenantId,
            CallerTenantId: TenantId,
            RequesterId: _currentUser.UserId,
            CallerType: _currentUser.HasRole("KDS") ? "kds" : "staff"), ct));

    // ── POST /api/v1/kds-stations ─────────────────────────────────────────────

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> Create(
        [FromBody] CreateKdsStationRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(new CreateKdsStationCommand(
            TenantId,
            req.Name,
            req.CategoryIds ?? [],
            _currentUser.UserId,
            req.IsActive,
            req.FlowProfile,
            req.ActiveIn), ct),
            successStatus: 201);

    // ── PUT /api/v1/kds-stations/{stationId} ─────────────────────────────────

    [HttpPut("{stationId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> Update(
        Guid stationId,
        [FromBody] UpdateKdsStationRequest req,
        CancellationToken ct) =>
        ToResponse(await Mediator.Send(new UpdateKdsStationCommand(
            stationId,
            TenantId,
            req.Name,
            req.CategoryIds,
            _currentUser.UserId,
            req.IsActive,
            req.FlowProfile,
            req.ActiveIn), ct));

    // ── DELETE /api/v1/kds-stations/{stationId} ───────────────────────────────

    [HttpDelete("{stationId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> Delete(Guid stationId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(new DeleteKdsStationCommand(
            stationId,
            TenantId,
            _currentUser.UserId), ct));
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record CreateKdsStationRequest(
    string Name, List<string>? CategoryIds,
    bool? IsActive = null, string? FlowProfile = null, List<string>? ActiveIn = null);

public record UpdateKdsStationRequest(
    string? Name, List<string>? CategoryIds,
    bool? IsActive = null, string? FlowProfile = null, List<string>? ActiveIn = null);
