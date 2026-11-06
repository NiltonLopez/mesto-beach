using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Payments.Application.Commands;
using MestoBeach.Payments.Application.Queries;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Payments;

/// <summary>
/// GET   /api/v1/payments/config    - Get tenant payment config
/// PATCH /api/v1/payments/config    - Upsert tenant payment config (Admin only)
/// </summary>
[Route("api/v1/payments/config")]
public sealed class PaymentConfigController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public PaymentConfigController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    private Guid TenantId => (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

    // ── GET /api/v1/payments/config ───────────────────────────────────────────

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.StaffJwt)]
    public async Task<IActionResult> GetConfig(CancellationToken ct) =>
        ToResponse(await Mediator.Send(new GetPaymentConfigQuery(TenantId), ct));

    // ── PATCH /api/v1/payments/config ─────────────────────────────────────────

    [HttpPatch]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> UpsertConfig(
        [FromBody] UpsertPaymentConfigRequest req,
        CancellationToken ct) =>
        ToResponse(await Mediator.Send(new UpsertPaymentConfigCommand(
            TenantId,
            req.TipEnabled,
            req.TipOptions,
            RequesterId: _currentUser.UserId), ct));
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record UpsertPaymentConfigRequest(
    bool? TipEnabled,
    List<int>? TipOptions);
