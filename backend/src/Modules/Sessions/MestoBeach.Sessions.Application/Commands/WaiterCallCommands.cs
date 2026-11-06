using MediatR;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Sessions.Application.Commands;

public record WaiterCallDto(
    Guid TenantId,
    Guid SessionId,
    string TableCode,
    DateTime CreatedAt,
    // Motivos del aviso:
    //   "WAITER" = llamada genérica al camarero.
    //   "PIN"    = el comensal pide el PIN de la mesa para poder pedir.
    //   "BILL"   = el comensal pide la cuenta.
    //   "ETA"    = el comensal pregunta "¿cuándo sale?" por su pedido.
    string Reason = "WAITER",
    // PRD-019 (SDD-003): clase del puesto (TABLE | HIGH_TABLE | RESERVADO), resuelta al listar en
    // GET /sessions/calls. Null fuera de ese listado.
    string? TableKind = null);

public record CreateWaiterCallCommand(Guid SessionId) : IRequest<Result<WaiterCallDto>>;

public sealed class CreateWaiterCallHandler : IRequestHandler<CreateWaiterCallCommand, Result<WaiterCallDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly IWaiterCallService _calls;
    private readonly ISignalRNotifier _signalR;

    public CreateWaiterCallHandler(
        ISessionRepository sessions,
        IWaiterCallService calls,
        ISignalRNotifier signalR)
    {
        _sessions = sessions;
        _calls = calls;
        _signalR = signalR;
    }

    public async Task<Result<WaiterCallDto>> Handle(CreateWaiterCallCommand cmd, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(cmd.SessionId, ct);
        if (session is null || !session.IsOpen)
            return Result.Failure<WaiterCallDto>(Error.NotFound("Sesión no encontrada o cerrada."));

        var call = await _calls.CreateOrGetAsync(session.TenantId, session.Id, session.TableCode, "WAITER", ct);

        await _signalR.NotifyWaitersAsync(session.TenantId, new
        {
            type = "WAITER_CALLED",
            sessionId = session.Id,
            tableCode = session.TableCode,
            reason = call.Reason,
            createdAt = call.CreatedAt
        }, ct);

        return Result.Success(new WaiterCallDto(call.TenantId, call.SessionId, call.TableCode, call.CreatedAt, call.Reason));
    }
}

// ─── PIN Request (cliente "pedir el PIN al mesero" → mesero/cajero) ────────────
// A customer joined a Fixed-QR table that requires a PIN to order. They ask the waiter for
// the shared table PIN; the waiter is notified in real time (PIN_REQUESTED) and reads the PIN
// off their screen to dictate it. Durable in Redis so a reconnecting waiter still sees it.

public record CreatePinRequestCommand(Guid SessionId) : IRequest<Result<WaiterCallDto>>;

public sealed class CreatePinRequestHandler : IRequestHandler<CreatePinRequestCommand, Result<WaiterCallDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly IWaiterCallService _calls;
    private readonly ISignalRNotifier _signalR;

    public CreatePinRequestHandler(
        ISessionRepository sessions,
        IWaiterCallService calls,
        ISignalRNotifier signalR)
    {
        _sessions = sessions;
        _calls = calls;
        _signalR = signalR;
    }

    public async Task<Result<WaiterCallDto>> Handle(CreatePinRequestCommand cmd, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(cmd.SessionId, ct);
        if (session is null || !session.IsOpen)
            return Result.Failure<WaiterCallDto>(Error.NotFound("Sesión no encontrada o cerrada."));

        var call = await _calls.CreateOrGetAsync(session.TenantId, session.Id, session.TableCode, "PIN", ct);

        await _signalR.NotifyWaitersAsync(session.TenantId, new
        {
            type = "PIN_REQUESTED",
            sessionId = session.Id,
            tableCode = session.TableCode,
            reason = call.Reason,
            createdAt = call.CreatedAt
        }, ct);

        return Result.Success(new WaiterCallDto(call.TenantId, call.SessionId, call.TableCode, call.CreatedAt, call.Reason));
    }
}

// ─── Bill Request (cliente "pedir la cuenta" → PDA / cajero) ──────────────────
// Se persiste en Redis igual que WAITER, PIN y ETA. Antes solo se emitía el evento en vivo:
// si en ese instante no había ninguna pantalla conectada —o se recargaba— la petición se
// perdía y no aparecía en ningún sitio, porque `ListWaiterCallsQuery` lee de Redis.

public record CreateBillRequestCommand(Guid SessionId) : IRequest<Result<WaiterCallDto>>;

public sealed class CreateBillRequestHandler : IRequestHandler<CreateBillRequestCommand, Result<WaiterCallDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly IWaiterCallService _calls;
    private readonly ISignalRNotifier _signalR;

    public CreateBillRequestHandler(
        ISessionRepository sessions,
        IWaiterCallService calls,
        ISignalRNotifier signalR)
    {
        _sessions = sessions;
        _calls = calls;
        _signalR = signalR;
    }

    public async Task<Result<WaiterCallDto>> Handle(CreateBillRequestCommand cmd, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(cmd.SessionId, ct);
        if (session is null || !session.IsOpen)
            return Result.Failure<WaiterCallDto>(Error.NotFound("Sesión no encontrada o cerrada."));

        var call = await _calls.CreateOrGetAsync(session.TenantId, session.Id, session.TableCode, "BILL", ct);

        await _signalR.NotifyWaitersAsync(session.TenantId, new
        {
            type = "BILL_REQUESTED",
            sessionId = session.Id,
            tableCode = session.TableCode,
            reason = call.Reason,
            createdAt = call.CreatedAt
        }, ct);

        // Se devuelve el motivo realmente guardado: si ya había un PIN pendiente, ese manda y
        // decir "BILL" mentiría sobre el estado. Misma regla que en WAITER, PIN y ETA.
        return Result.Success(new WaiterCallDto(call.TenantId, call.SessionId, call.TableCode, call.CreatedAt, call.Reason));
    }
}

// ─── ETA Request (cliente "¿cuándo sale?" → PDA / Salón) ──────────────────────
// El comensal pregunta por su pedido. El aviso NUNCA va a cocina: llega al PDA/Salón para que
// el camarero consulte y responda en persona. A diferencia de BILL, este aviso SÍ se persiste
// en Redis para que aparezca en la lista de llamados del Salón (ListWaiterCallsQuery) y se pueda
// dar por atendido con AcknowledgeWaiterCallCommand.

public record CreateEtaRequestCommand(Guid SessionId) : IRequest<Result<WaiterCallDto>>;

public sealed class CreateEtaRequestHandler : IRequestHandler<CreateEtaRequestCommand, Result<WaiterCallDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly IWaiterCallService _calls;
    private readonly ISignalRNotifier _signalR;

    public CreateEtaRequestHandler(
        ISessionRepository sessions,
        IWaiterCallService calls,
        ISignalRNotifier signalR)
    {
        _sessions = sessions;
        _calls = calls;
        _signalR = signalR;
    }

    public async Task<Result<WaiterCallDto>> Handle(CreateEtaRequestCommand cmd, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(cmd.SessionId, ct);
        if (session is null || !session.IsOpen)
            return Result.Failure<WaiterCallDto>(Error.NotFound("Sesión no encontrada o cerrada."));

        // Se persiste el aviso. Ojo: si ya había un "PIN" o un "WAITER" pendiente, el servicio
        // conserva ese motivo (es más urgente) y devuelve el aviso previo sin pisarlo.
        var call = await _calls.CreateOrGetAsync(session.TenantId, session.Id, session.TableCode, "ETA", ct);

        await _signalR.NotifyWaitersAsync(session.TenantId, new
        {
            type = "ETA_REQUESTED",
            sessionId = session.Id,
            tableCode = session.TableCode,
            createdAt = call.CreatedAt
        }, ct);

        // Se devuelve el motivo REALMENTE persistido (call.Reason), no un "ETA" fijo: si había un
        // PIN o un WAITER pendiente, el aviso sigue siendo ese y la respuesta debe decirlo —igual
        // que hacen los handlers de WAITER y PIN—. Devolver "ETA" mentiría sobre el estado guardado.
        return Result.Success(new WaiterCallDto(call.TenantId, call.SessionId, call.TableCode, call.CreatedAt, call.Reason));
    }
}

public record AcknowledgeWaiterCallCommand(Guid TenantId, Guid SessionId, Guid RequesterId)
    : IRequest<Result<WaiterCallDto>>;

public sealed class AcknowledgeWaiterCallHandler : IRequestHandler<AcknowledgeWaiterCallCommand, Result<WaiterCallDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly IWaiterCallService _calls;
    private readonly ISignalRNotifier _signalR;

    public AcknowledgeWaiterCallHandler(
        ISessionRepository sessions,
        IWaiterCallService calls,
        ISignalRNotifier signalR)
    {
        _sessions = sessions;
        _calls = calls;
        _signalR = signalR;
    }

    public async Task<Result<WaiterCallDto>> Handle(AcknowledgeWaiterCallCommand cmd, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(cmd.SessionId, ct);
        if (session is null || session.TenantId != cmd.TenantId)
            return Result.Failure<WaiterCallDto>(Error.NotFound("Sesión no encontrada."));

        var removed = await _calls.AcknowledgeAsync(cmd.TenantId, cmd.SessionId, ct);
        if (!removed)
            return Result.Failure<WaiterCallDto>(Error.NotFound("No hay un llamado pendiente para esta sesión."));

        await _signalR.NotifyWaitersAsync(cmd.TenantId, new
        {
            type = "WAITER_CALL_ACKNOWLEDGED",
            sessionId = cmd.SessionId,
            tableCode = session.TableCode,
            acknowledgedBy = cmd.RequesterId
        }, ct);

        return Result.Success(new WaiterCallDto(cmd.TenantId, session.Id, session.TableCode, DateTime.UtcNow));
    }
}
