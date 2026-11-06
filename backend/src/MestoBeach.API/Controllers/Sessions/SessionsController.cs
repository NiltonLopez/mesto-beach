using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Sessions.Application.Commands;
using MestoBeach.Sessions.Application.Queries;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Sessions;

[Route("api/v1/sessions")]
public sealed class SessionsController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public SessionsController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    private Guid StaffTenantId =>
        (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

    private bool IsStaffOrPlatform =>
        _currentUser.UserType is "staff" or "platform_admin";

    // ── GET /api/v1/sessions/by-table?tenantId=&tableCode= ───────────────────
    [HttpGet("by-table")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByTable(
        [FromQuery] Guid tenantId,
        [FromQuery] string? tableCode,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty || string.IsNullOrEmpty(tableCode))
            return BadRequest("tenantId y tableCode son requeridos.");

        return ToResponse(await Mediator.Send(
            new GetSessionByTableQuery(tenantId, tableCode), ct));
    }

    // ── POST /api/v1/sessions ─────────────────────────────────────────────────
    [HttpPost]
    [AllowAnonymous] // Can use waiterJwt in body or an authenticated staff bearer token
    public async Task<IActionResult> OpenSession(
        [FromBody] OpenSessionRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new OpenSessionCommand(
                req.TenantId, req.TableCode, req.ServiceMode, req.QrMode,
                req.WaiterJwt, req.DeviceId, req.ShiftId,
                _currentUser.IsAuthenticated && _currentUser.UserType == "staff" ? _currentUser.UserId : null,
                _currentUser.IsAuthenticated && _currentUser.UserType == "staff"
                    ? (HttpContext.Items["tenant_id"] as Guid?)
                    : null,
                _currentUser.IsAuthenticated && _currentUser.UserType == "staff" ? _currentUser.Role : null,
                req.GuestCount), ct),
            successStatus: 201);

    // ── GET /api/v1/sessions/{sessionId}?deviceId= ───────────────────────────
    [HttpGet("{sessionId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSession(
        Guid sessionId,
        [FromQuery] string? deviceId,
        CancellationToken ct)
    {
        bool isStaff = IsStaffOrPlatform;
        if (!isStaff && string.IsNullOrEmpty(deviceId))
            return BadRequest("Se requiere deviceId o JWT de staff.");

        return ToResponse(await Mediator.Send(
            new GetSessionQuery(sessionId, deviceId, isStaff, _currentUser.UserId), ct));
    }

    // ── POST /api/v1/sessions/join ────────────────────────────────────────────
    [HttpPost("join")]
    [AllowAnonymous]
    public async Task<IActionResult> JoinSession(
        [FromBody] JoinSessionRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new JoinSessionCommand(
                req.TenantId, req.DeviceId, req.JoinToken,
                req.TokenType, req.CustomerAccountJwt), ct));

    // ── POST /api/v1/sessions/join-by-table ──────────────────────────────────
    // Fixed table QR: une al cliente a la sesión activa de la mesa (tenant + tableCode, sin token).
    [HttpPost("join-by-table")]
    [AllowAnonymous]
    public async Task<IActionResult> JoinByTable(
        [FromBody] JoinByTableRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new JoinByTableCommand(req.TenantId, req.TableCode, req.DeviceId), ct));

    // ── POST /api/v1/sessions/{sessionId}/participants ────────────────────────
    [HttpPost("{sessionId:guid}/participants")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdminOrCajero)]
    public async Task<IActionResult> AddParticipant(
        Guid sessionId,
        [FromBody] AddParticipantRequest req,
        CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new AddParticipantCommand(
                StaffTenantId, sessionId, req.DeviceId,
                req.CustomerAccountId, req.IsHost ?? false,
                _currentUser.UserId), ct),
            successStatus: 201);

    // ── PATCH /api/v1/sessions/{sessionId} ───────────────────────────────────
    [HttpPatch("{sessionId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> UpdateWaiter(
        Guid sessionId,
        [FromBody] UpdateSessionWaiterRequest req,
        CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new UpdateSessionWaiterCommand(
                StaffTenantId, sessionId, req.WaiterId, _currentUser.UserId), ct));

    // ── POST /api/v1/sessions/{sessionId}/close ───────────────────────────────
    [HttpPost("{sessionId:guid}/close")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdminOrCajero)]
    public async Task<IActionResult> CloseSession(
        Guid sessionId,
        [FromBody] CloseSessionRequest req,
        CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new CloseSessionCommand(
                StaffTenantId, sessionId, req.ClosureReason, _currentUser.UserId), ct));

    // ── POST /api/v1/sessions/{sessionId}/pin/validate ────────────────────────
    [HttpPost("{sessionId:guid}/pin/validate")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidatePin(
        Guid sessionId,
        [FromBody] ValidatePinRequest req,
        CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new ValidatePinCommand(
                sessionId, req.TenantId, req.ParticipantId, req.DeviceId, req.Pin), ct));

    // ── POST /api/v1/sessions/{sessionId}/pin/regenerate ─────────────────────
    [HttpPost("{sessionId:guid}/pin/regenerate")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdminOrCajero)]
    public async Task<IActionResult> RegeneratePin(
        Guid sessionId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new RegeneratePinCommand(StaffTenantId, sessionId, _currentUser.UserId), ct));

    // ── GET /api/v1/sessions/{sessionId}/pin ─────────────────────────────────
    [HttpGet("{sessionId:guid}/pin")]
    [Authorize(Policy = AuthorizationPolicies.StaffJwt)]
    public async Task<IActionResult> GetPin(Guid sessionId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(new GetSessionPinQuery(StaffTenantId, sessionId), ct));

    // ── GET /api/v1/sessions/{sessionId}/join-link?type= ─────────────────────
    [HttpGet("{sessionId:guid}/join-link")]
    [Authorize(Policy = AuthorizationPolicies.StaffJwt)]
    public async Task<IActionResult> GetJoinLink(
        Guid sessionId,
        [FromQuery] string type = "INVITE",
        CancellationToken ct = default) =>
        ToResponse(await Mediator.Send(new GenerateSessionJoinTokenQuery(StaffTenantId, sessionId, type), ct));

    // ── GET /api/v1/sessions/calls?tenantId= ─────────────────────────────────
    [HttpGet("calls")]
    [Authorize(Policy = AuthorizationPolicies.StaffJwt)]
    public async Task<IActionResult> GetWaiterCalls(
        [FromQuery] Guid? tenantId,
        CancellationToken ct)
    {
        var effectiveTenantId = tenantId ?? StaffTenantId;
        if (effectiveTenantId == Guid.Empty)
            return BadRequest("tenantId es requerido.");

        if (_currentUser.UserType == "staff" && StaffTenantId != effectiveTenantId)
            return StatusCode(403, new ProblemDetails { Title = "FORBIDDEN", Status = 403 });

        return ToResponse(await Mediator.Send(new ListWaiterCallsQuery(effectiveTenantId), ct));
    }

    // ── POST /api/v1/sessions/{sessionId}/call-waiter ────────────────────────
    [HttpPost("{sessionId:guid}/call-waiter")]
    [AllowAnonymous]
    public async Task<IActionResult> CallWaiter(Guid sessionId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(new CreateWaiterCallCommand(sessionId), ct), successStatus: 201);

    // ── POST /api/v1/sessions/{sessionId}/call-waiter/acknowledge ────────────
    [HttpPost("{sessionId:guid}/call-waiter/acknowledge")]
    [Authorize(Policy = AuthorizationPolicies.StaffJwt)]
    public async Task<IActionResult> AcknowledgeWaiterCall(Guid sessionId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(new AcknowledgeWaiterCallCommand(StaffTenantId, sessionId, _currentUser.UserId), ct));

    // ── POST /api/v1/sessions/{sessionId}/participants/{participantId}/identify ─
    // El cliente se identifica: nombre (invitado) y, si está logueado, vincula su cuenta Mesto Beach.
    [HttpPost("{sessionId:guid}/participants/{participantId:guid}/identify")]
    [AllowAnonymous]
    public async Task<IActionResult> Identify(
        Guid sessionId, Guid participantId, [FromBody] IdentifyRequest req, CancellationToken ct)
    {
        Guid? customerId = _currentUser.UserType == "customer" ? _currentUser.UserId : null;
        return ToResponse(await Mediator.Send(
            new IdentifyParticipantCommand(sessionId, participantId, req.DisplayName, customerId), ct));
    }

    // ── POST /api/v1/sessions/{sessionId}/invite-link ────────────────────────
    // Un participante genera un link de invitación (token firmado, INVITE). Quien lo abra entra
    // a la sesión SIN teclear el PIN (el token concede el acceso; el PIN no viaja en la URL).
    [HttpPost("{sessionId:guid}/invite-link")]
    [AllowAnonymous]
    public async Task<IActionResult> CreateInviteLink(
        Guid sessionId, [FromBody] InviteLinkRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new GenerateSessionJoinTokenQuery(req.TenantId, sessionId, "INVITE"), ct));

    // ── POST /api/v1/sessions/{sessionId}/request-pin ────────────────────────
    // El cliente pide el PIN de la mesa al mesero; el mesero/cajero recibe PIN_REQUESTED en vivo.
    [HttpPost("{sessionId:guid}/request-pin")]
    [AllowAnonymous]
    public async Task<IActionResult> RequestPin(Guid sessionId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(new CreatePinRequestCommand(sessionId), ct), successStatus: 201);

    // ── POST /api/v1/sessions/{sessionId}/request-bill ───────────────────────
    // El cliente "pide la cuenta"; el cashdesk/mesero recibe BILL_REQUESTED en tiempo real.
    [HttpPost("{sessionId:guid}/request-bill")]
    [AllowAnonymous]
    public async Task<IActionResult> RequestBill(Guid sessionId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(new CreateBillRequestCommand(sessionId), ct), successStatus: 201);

    // ── POST /api/v1/sessions/{sessionId}/ask-eta ────────────────────────────
    // El comensal pregunta "¿cuándo sale?" por su plato. El aviso llega al PDA/Salón
    // (ETA_REQUESTED) para que el camarero lo consulte y responda en persona; NUNCA se
    // manda directamente a cocina. Queda como llamado pendiente hasta que el Salón lo
    // dé por atendido.
    [HttpPost("{sessionId:guid}/ask-eta")]
    [AllowAnonymous]
    public async Task<IActionResult> AskEta(Guid sessionId, CancellationToken ct) =>
        ToResponse(await Mediator.Send(new CreateEtaRequestCommand(sessionId), ct), successStatus: 201);

    // ── GET /api/v1/sessions/closed ──────────────────────────────────────────
    // Historial de sesiones cerradas del tenant (cajero/admin).
    [HttpGet("closed")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdminOrCajero)]
    public async Task<IActionResult> ListClosed(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? tableCode,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        ToResponse(await Mediator.Send(
            new ListClosedSessionsQuery(StaffTenantId, from, to, tableCode, page, pageSize), ct));
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public sealed record OpenSessionRequest(
    Guid TenantId,
    string TableCode,
    string ServiceMode,
    string QrMode,
    string? WaiterJwt,
    string? DeviceId,
    Guid? ShiftId,
    int? GuestCount = null);

public sealed record JoinSessionRequest(
    Guid TenantId,
    string DeviceId,
    string JoinToken,
    string TokenType,
    string? CustomerAccountJwt);

public sealed record JoinByTableRequest(
    Guid TenantId,
    string TableCode,
    string DeviceId);

public sealed record AddParticipantRequest(
    string DeviceId,
    Guid? CustomerAccountId,
    bool? IsHost);

public sealed record UpdateSessionWaiterRequest(Guid? WaiterId);

public sealed record CloseSessionRequest(string ClosureReason);

public sealed record ValidatePinRequest(
    Guid TenantId,
    Guid ParticipantId,
    string DeviceId,
    string Pin);

public sealed record IdentifyRequest(string DisplayName);

public sealed record InviteLinkRequest(Guid TenantId);
