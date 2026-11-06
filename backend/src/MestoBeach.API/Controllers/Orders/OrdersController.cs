using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Orders.Application.Commands;
using MestoBeach.Orders.Application.Queries;
using MestoBeach.Orders.Domain.Enums;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Orders;

/// <summary>
/// POST   /api/v1/orders                     - Customer or Staff creates an order
/// GET    /api/v1/orders?sessionId=           - List orders for a session
/// GET    /api/v1/orders/{orderId}            - Get order detail
/// PATCH  /api/v1/orders/{orderId}/status     - Staff or KDS advances status
/// DELETE /api/v1/orders/{orderId}            - Staff/Customer cancels an order
/// </summary>
[Route("api/v1/orders")]
public sealed class OrdersController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public OrdersController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    private Guid TenantId => (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

    // ── POST /api/v1/orders ───────────────────────────────────────────────────

    [HttpPost]
    [AllowAnonymous] // Auth determined by caller type logic in handler
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest req, CancellationToken ct)
    {
        // Determine caller identity: customer session token provides participantId
        // Staff can also place orders on behalf of participants
        var callerParticipantId = req.ParticipantId;

        try
        {
            return ToResponse(await Mediator.Send(new CreateOrderCommand(
                req.SessionId,
                callerParticipantId,
                req.Items.Select(i => new OrderItemRequest(
                    i.CatalogItemId, i.Quantity, i.Notes, i.LoyaltyRedeemed, i.SelectedOptionIds,
                    i.Timing)).ToList(),
                req.Observacion,
                CallerTenantId: TenantId,
                CallerParticipantId: callerParticipantId), ct),
                successStatus: 201);
        }
        catch (MestoBeach.Shared.Venue.VenueModeUnavailableException ex)
        {
            // Sin modo no se puede aplicar la carta por modo (ADR-041): 503, sin crear la orden.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = MestoBeach.Shared.Venue.VenueModeUnavailableException.Code,
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable,
            });
        }
    }

    // ── GET /api/v1/orders?sessionId= ─────────────────────────────────────────

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ListOrders(
        [FromQuery] Guid sessionId,
        [FromQuery] string? status,
        [FromQuery] string? stationId,  // "null" to filter unassigned orders
        CancellationToken ct)
    {
        if (sessionId == Guid.Empty)
            return BadRequest("sessionId es requerido.");

        OrderStatus? parsedStatus = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!TryParseStatus(status, out var s))
                return BadRequest($"status inválido: {status}");
            parsedStatus = s;
        }

        Guid? parsedStationId = null;
        bool filterNullStation = false;

        if (!string.IsNullOrEmpty(stationId))
        {
            if (stationId.Equals("null", StringComparison.OrdinalIgnoreCase))
                filterNullStation = true;
            else if (!Guid.TryParse(stationId, out var sid))
                return BadRequest("stationId debe ser un UUID válido o la cadena 'null'.");
            else
                parsedStationId = sid;
        }

        var callerType = GetCallerType();

        // NOTE: a logged-in customer's JWT carries the CUSTOMER ACCOUNT id (not a participant id),
        // so it must NOT be passed as CallerParticipantId. The session is shared — every participant
        // sees the whole table's bill — so the listing is not gated by participant.
        var result = await Mediator.Send(new ListOrdersQuery(
            sessionId,
            parsedStatus,
            parsedStationId,
            filterNullStation,
            CallerTenantId: TenantId == Guid.Empty ? null : TenantId,
            CallerParticipantId: null,
            CallerType: callerType), ct);

        if (!result.IsSuccess) return ToResponse(result);

        if (result.Value!.Truncated)
            Response.Headers["X-Truncated"] = "true";

        return Ok(result.Value);
    }

    // ── GET /api/v1/orders/active ─────────────────────────────────────────────
    // KDS authoritative board: all active orders of the tenant (optionally one
    // station) in a single query. The KDS polls this as its source of truth.
    [HttpGet("active")]
    [Authorize(Policy = AuthorizationPolicies.StaffJwt)]
    public async Task<IActionResult> ListActiveOrders(
        [FromQuery] string? stationId,   // UUID, or "null" for unassigned, or omit for all
        [FromQuery] string? since,       // ISO-8601 UTC cursor for delta sync (omit = full snapshot)
        CancellationToken ct)
    {
        if (TenantId == Guid.Empty)
            return BadRequest("Tenant no resuelto.");

        Guid? parsedStationId = null;
        bool filterNullStation = false;
        if (!string.IsNullOrEmpty(stationId))
        {
            if (stationId.Equals("null", StringComparison.OrdinalIgnoreCase))
                filterNullStation = true;
            else if (!Guid.TryParse(stationId, out var sid))
                return BadRequest("stationId debe ser un UUID válido o la cadena 'null'.");
            else
                parsedStationId = sid;
        }

        DateTime? parsedSince = null;
        if (!string.IsNullOrEmpty(since))
        {
            if (!DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var s))
                return BadRequest("since debe ser una fecha ISO-8601 válida.");
            parsedSince = DateTime.SpecifyKind(s, DateTimeKind.Utc);
        }

        return ToResponse(await Mediator.Send(
            new ListActiveOrdersQuery(TenantId, parsedStationId, filterNullStation, parsedSince), ct));
    }

    // ── GET /api/v1/orders/{orderId} ──────────────────────────────────────────

    [HttpGet("{orderId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetOrder(Guid orderId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(new GetOrderQuery(
            orderId,
            CallerTenantId: TenantId == Guid.Empty ? null : TenantId,
            CallerParticipantId: _currentUser.IsAuthenticated && _currentUser.UserType == "customer"
                ? _currentUser.UserId
                : null,
            CallerType: GetCallerType()), ct));

    // ── PATCH /api/v1/orders/{orderId}/status ─────────────────────────────────

    [HttpPatch("{orderId:guid}/status")]
    [Authorize(Policy = AuthorizationPolicies.StaffJwt)]
    public async Task<IActionResult> UpdateStatus(
        Guid orderId,
        [FromBody] UpdateStatusRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.Status))
            return BadRequest("status es requerido.");

        return ToResponse(await Mediator.Send(new UpdateOrderStatusCommand(
            orderId,
            req.Status.ToUpperInvariant(),
            CallerTenantId: TenantId,
            CallerType: _currentUser.HasRole("KDS") ? "kds" : "staff",
            CallerRole: _currentUser.Role), ct));
    }

    // ── DELETE /api/v1/orders/{orderId} ───────────────────────────────────────

    [HttpDelete("{orderId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> CancelOrder(
        Guid orderId,
        [FromBody] CancelOrderRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.Reason))
            return BadRequest("reason es requerido.");

        var requesterId = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty;

        // Traducir la identidad del token al lenguaje del handler. Un no autenticado no es "customer"
        // (que sí puede anular lo suyo) sino "anon"; un token de servicio/KDS tampoco es "staff".
        var callerType = !_currentUser.IsAuthenticated
            ? "anon"
            : _currentUser.UserType switch
            {
                "customer" => "customer",
                "staff" => "staff",
                _ => "other"
            };
        Guid? callerParticipant =
            _currentUser.IsAuthenticated && _currentUser.UserType == "customer"
                ? _currentUser.UserId
                : null;

        return ToResponse(await Mediator.Send(new CancelOrderCommand(
            orderId,
            req.Reason,
            CallerTenantId: TenantId,
            RequesterId: requesterId,
            CallerType: callerType,
            CallerParticipantId: callerParticipant), ct));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetCallerType()
    {
        if (!_currentUser.IsAuthenticated) return "customer";
        return _currentUser.UserType switch
        {
            "staff" when _currentUser.HasRole("KDS") => "kds",
            "staff" => "staff",
            "customer" => "customer",
            _ => "staff"
        };
    }

    private static bool TryParseStatus(string s, out OrderStatus result)
    {
        result = s.ToUpperInvariant() switch
        {
            "PENDING"         => OrderStatus.Pending,
            "CONFIRMED"       => OrderStatus.Confirmed,
            "IN_PREPARATION"  => OrderStatus.InPreparation,
            "READY"           => OrderStatus.Ready,
            "DELIVERED"       => OrderStatus.Delivered,
            "CANCELLED"       => OrderStatus.Cancelled,
            _ => (OrderStatus)(-1)
        };
        return (int)result >= 0;
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record CreateOrderItemRequest(
    string CatalogItemId,
    int Quantity,
    string? Notes,
    bool LoyaltyRedeemed = false,
    List<Guid>? SelectedOptionIds = null,
    // Coursing (RF-1811): "NOW" | "LATER". Ausente = "NOW".
    string? Timing = null);

public record CreateOrderRequest(
    Guid SessionId,
    Guid ParticipantId,
    List<CreateOrderItemRequest> Items,
    string? Observacion = null);

public record UpdateStatusRequest(string Status);

public record CancelOrderRequest(string Reason);
