using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Payments.Application.Commands;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Payments;

/// <summary>
/// Cashier shift (cash drawer) management.
/// POST /api/v1/shifts            - open a shift
/// GET  /api/v1/shifts/open       - current open shift (or null)
/// POST /api/v1/shifts/{id}/close - close a shift + return cash report
/// GET  /api/v1/shifts/{id}/report- cash report for a shift
/// </summary>
[Route("api/v1/shifts")]
[Authorize(Policy = AuthorizationPolicies.StaffAdminOrCajero)]
public sealed class ShiftsController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public ShiftsController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    private Guid TenantId => (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

    [HttpPost]
    public async Task<IActionResult> Open([FromBody] OpenShiftRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new OpenShiftCommand(TenantId, _currentUser.UserId, req.OpeningFloatCents ?? 0), ct),
            successStatus: 201);

    [HttpGet("open")]
    public async Task<IActionResult> GetOpen(CancellationToken ct) =>
        ToResponse(await Mediator.Send(new GetOpenShiftQuery(TenantId), ct));

    [HttpPost("{shiftId:guid}/close")]
    public async Task<IActionResult> Close(Guid shiftId, [FromBody] CloseShiftRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new CloseShiftCommand(shiftId, TenantId, _currentUser.UserId, req.Note), ct));

    [HttpGet("{shiftId:guid}/report")]
    public async Task<IActionResult> Report(Guid shiftId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(new GetShiftReportQuery(shiftId, TenantId), ct));
}

public record OpenShiftRequest(int? OpeningFloatCents);
public record CloseShiftRequest(string? Note);
