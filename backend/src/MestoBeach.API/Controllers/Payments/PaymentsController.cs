using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Payments.Application.Commands;
using MestoBeach.Payments.Application.Queries;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Payments;

/// <summary>
/// POST  /api/v1/payments/cash                          - Cajero registers a cash payment
/// GET   /api/v1/payments/sessions/{sessionId}/status   - Get session payment status
/// </summary>
[Route("api/v1/payments")]
public sealed class PaymentsController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public PaymentsController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    private Guid TenantId => (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

    // ── POST /api/v1/payments/cash ────────────────────────────────────────────

    /// <summary>
    /// Register a cash payment for a session.
    /// Only Cajero or Admin staff can call this.
    /// </summary>
    [HttpPost("cash")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdminOrCajero)]
    public async Task<IActionResult> RegisterCash(
        [FromBody] RegisterCashPaymentRequest req,
        CancellationToken ct)
    {
        return ToResponse(await Mediator.Send(new RegisterCashPaymentCommand(
            req.SessionId,
            req.AmountCents,
            req.TipCents ?? 0,
            req.SplitIndex,
            req.SplitTotal,
            CallerTenantId: TenantId,
            ProcessedBy: _currentUser.UserId,
            PayerLabel: req.PayerLabel), ct),
            successStatus: 201);
    }

    // ── GET /api/v1/payments  (recent sales list) ─────────────────────────────

    /// <summary>
    /// Recent sales (payments) for the restaurant, newest first. Cajero or Admin only.
    /// Defaults to the last 30 days when no range is given.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.StaffAdminOrCajero)]
    public async Task<IActionResult> ListRecent(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = from ?? toDate.AddDays(-29);
        return ToResponse(await Mediator.Send(
            new GetRecentPaymentsQuery(TenantId, fromDate, toDate, Math.Clamp(limit, 1, 500)), ct));
    }

    // ── GET /api/v1/payments/sessions/{sessionId}/status ──────────────────────

    /// <summary>
    /// Get payment status for a session.
    /// Staff (any role) or authenticated customers can call this.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSessionStatus(
        Guid sessionId,
        CancellationToken ct)
    {
        string callerType;
        if (!_currentUser.IsAuthenticated)
            callerType = "customer";
        else
            callerType = _currentUser.UserType == "staff" ? "staff" : "customer";

        return ToResponse(await Mediator.Send(new GetSessionPaymentStatusQuery(
            sessionId,
            CallerTenantId: TenantId,
            CallerType: callerType), ct));
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record RegisterCashPaymentRequest(
    Guid SessionId,
    int AmountCents,
    int? TipCents,
    int? SplitIndex,
    int? SplitTotal,
    string? PayerLabel = null);
