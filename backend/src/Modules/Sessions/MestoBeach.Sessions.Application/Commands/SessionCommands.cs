using FluentValidation;
using MediatR;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Domain.Entities;
using MestoBeach.Sessions.Domain.Enums;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;
using Microsoft.Extensions.Logging;
using MestoBeach.Shared.Venue;

namespace MestoBeach.Sessions.Application.Commands;

// ══════════════════════════════════════════════════════════════════════════════
// OPEN SESSION
// ══════════════════════════════════════════════════════════════════════════════

public record OpenSessionDto(
    Guid SessionId,
    Guid TenantId,
    string TableCode,
    Guid TableId,
    string Status,
    string QrMode,
    string ServiceMode,
    bool PinRequired,
    string? Pin,
    Guid? WaiterId,
    Guid? OpenedBy,
    DateTime CreatedAt,
    Guid ParticipantId,
    int? GuestCount = null);

public record OpenSessionCommand(
    Guid TenantId,
    string TableCode,
    string ServiceMode,
    string QrMode,
    string? WaiterJwt,       // required when ServiceMode = STAFFED
    string? DeviceId,         // required when ServiceMode = SELF_SERVICE
    Guid? ShiftId,
    Guid? CallerStaffId,
    Guid? CallerStaffTenantId,
    string? CallerStaffRole,
    int? GuestCount = null)   // comensales que registra la Host al sentar (RF-1840)
    : IRequest<Result<OpenSessionDto>>;

public sealed class OpenSessionValidator : AbstractValidator<OpenSessionCommand>
{
    public OpenSessionValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.TableCode).NotEmpty().MaximumLength(50);
        RuleFor(c => c.ServiceMode)
            .Must(m => m is "STAFFED" or "SELF_SERVICE")
            .WithMessage("serviceMode debe ser STAFFED o SELF_SERVICE.");
        RuleFor(c => c.QrMode)
            .Must(m => m is "FIXED" or "DYNAMIC")
            .WithMessage("qrMode debe ser FIXED o DYNAMIC.");
        RuleFor(c => c)
            .Must(c => c.QrMode != "DYNAMIC" || c.ServiceMode != "SELF_SERVICE")
            .WithMessage("DYNAMIC + SELF_SERVICE es una combinación inválida.");
        RuleFor(c => c)
            .Must(c => c.ServiceMode != "STAFFED" || c.CallerStaffId.HasValue || !string.IsNullOrWhiteSpace(c.WaiterJwt))
            .WithMessage("waiterJwt o staff autenticado es requerido para modo STAFFED.");
        RuleFor(c => c.DeviceId)
            .NotEmpty().MaximumLength(100)
            .When(c => c.ServiceMode == "SELF_SERVICE")
            .WithMessage("deviceId es requerido para modo SELF_SERVICE.");
        RuleFor(c => c.GuestCount)
            .InclusiveBetween(1, 999)
            .When(c => c.GuestCount.HasValue)
            .WithMessage("El número de comensales debe estar entre 1 y 999.");
    }
}

public sealed class OpenSessionHandler : IRequestHandler<OpenSessionCommand, Result<OpenSessionDto>>
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(4);

    private readonly ITableRepository _tables;
    private readonly ISessionRepository _sessions;
    private readonly ISessionCacheService _cache;
    private readonly ISessionPinService _pinService;
    private readonly ITenantSettingsService _tenantSettings;
    private readonly IStaffJwtValidator _staffJwtValidator;
    private readonly IShiftReader _shiftReader;
    private readonly IVenueModeProvider _venueMode;
    private readonly ISignalRNotifier _signalR;
    private readonly IFloorRepository _floors;
    private readonly ILogger<OpenSessionHandler> _logger;

    public OpenSessionHandler(
        IFloorRepository floors,
        IVenueModeProvider venueMode,
        ITableRepository tables,
        ISessionRepository sessions,
        ISessionCacheService cache,
        ISessionPinService pinService,
        ITenantSettingsService tenantSettings,
        IStaffJwtValidator staffJwtValidator,
        IShiftReader shiftReader,
        ISignalRNotifier signalR,
        ILogger<OpenSessionHandler> logger)
    {
        _floors = floors;
        _venueMode = venueMode;
        _tables = tables;
        _sessions = sessions;
        _cache = cache;
        _pinService = pinService;
        _tenantSettings = tenantSettings;
        _staffJwtValidator = staffJwtValidator;
        _shiftReader = shiftReader;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<OpenSessionDto>> Handle(OpenSessionCommand cmd, CancellationToken ct)
    {
        // Rate limit check
        bool allowed = await _cache.CheckRateLimitAsync(cmd.TenantId, cmd.TableCode);
        if (!allowed)
            return Result.Failure<OpenSessionDto>(
                Error.TooManyRequests("Demasiadas solicitudes. Intente nuevamente en un minuto."));

        var isStaffed = cmd.ServiceMode == "STAFFED";

        // Staff-opened sessions (cajero/mesero) require an open cashier shift.
        if (isStaffed && !await _shiftReader.HasOpenShiftAsync(cmd.TenantId, ct))
            return Result.Failure<OpenSessionDto>(
                Error.Validation("NO_OPEN_SHIFT", "Debes abrir el turno de caja antes de abrir mesas."));

        // Validate table. For staff (walk-in / customer without phone) the table is
        // created on the fly if it does not exist yet; self-service requires a real table.
        string? modoDelGate = null; // integración PR4+PR5b: si el gate ya leyó el modo, se reutiliza abajo
        var table = await _tables.GetByCodeAsync(cmd.TenantId, cmd.TableCode, ct);
        if (table is null)
        {
            if (!isStaffed)
                return Result.Failure<OpenSessionDto>(
                    Error.Validation("INVALID_TABLE", $"Mesa '{cmd.TableCode}' no encontrada o inactiva."));

            table = new Domain.Entities.DiningTable(Guid.NewGuid(), cmd.TenantId, cmd.TableCode, null, true);
            // Ad-hoc tables created at the cashdesk adopt the requested QR mode (default Fixed + PIN).
            table.SetQrConfig(TableQrParsing.Parse(cmd.QrMode), true);
            await _tables.AddAsync(table, ct);
            // PRD-019: la mesa ad-hoc nace con sus dos filas (sin piso, activa en ambos modos), en la
            // misma transacción.
            var (adHocRow, adHocOther) = TableModeRules.NewTableRows(
                table, nameof(VenueMode.RESTAURANT), null, null, null, activeInOther: true);
            await _tables.AddLayoutAsync(adHocRow, ct);
            await _tables.AddLayoutAsync(adHocOther, ct);
            await _tables.SaveChangesAsync(ct);
            _logger.LogInformation("sessions.table.auto_created {TenantId} {TableCode}", cmd.TenantId, cmd.TableCode);
        }
        else if (!table.Active)
        {
            return Result.Failure<OpenSessionDto>(
                Error.Validation("INVALID_TABLE", $"Mesa '{cmd.TableCode}' no encontrada o inactiva."));
        }
        else
        {
            // PRD-019: piso visible y fila del modo activo con active = true; si no, 409.
            var (closed, mode) = await TableServiceGate.CheckAsync(_tables, _floors, _venueMode, table, ct);
            modoDelGate = mode;
            if (closed is not null)
            {
                if (closed.Code == TableModeRules.TableClosedInModeCode)
                    _logger.LogWarning("sessions.session.table_closed_in_mode {TenantId} {TableCode} {Mode}",
                        cmd.TenantId, cmd.TableCode, mode);
                return Result.Failure<OpenSessionDto>(closed);
            }
        }

        // Check no open session already
        Guid? existingSessionId = null;
        try
        {
            existingSessionId = await _cache.GetActiveSessionIdAsync(cmd.TenantId, cmd.TableCode);
        }
        catch { /* Redis unavailable */ }

        if (existingSessionId.HasValue)
            return Result.Failure<OpenSessionDto>(
                Error.Conflict("Ya existe una sesión abierta para esta mesa."));

        var existingDb = await _sessions.GetOpenByTableIdAsync(table.Id, ct);
        if (existingDb is not null)
            return Result.Failure<OpenSessionDto>(
                Error.Conflict("Ya existe una sesión abierta para esta mesa."));

        // Validate waiter JWT for STAFFED mode
        StaffClaims? staffClaims = null;
        if (cmd.ServiceMode == "STAFFED")
        {
            if (cmd.CallerStaffId.HasValue && cmd.CallerStaffTenantId.HasValue && !string.IsNullOrWhiteSpace(cmd.CallerStaffRole))
            {
                staffClaims = new StaffClaims(cmd.CallerStaffId.Value, cmd.CallerStaffTenantId.Value, cmd.CallerStaffRole);
            }
            else
            {
                staffClaims = _staffJwtValidator.Validate(cmd.WaiterJwt!);
                if (staffClaims is null)
                    return Result.Failure<OpenSessionDto>(
                        Error.Unauthorized("waiterJwt inválido o expirado."));
            }

            if (staffClaims.TenantId != cmd.TenantId)
                return Result.Failure<OpenSessionDto>(
                    Error.Forbidden("El staff autenticado pertenece a otro tenant."));

            if (staffClaims.Role is not ("Mesero" or "Cajero" or "Admin"))
                return Result.Failure<OpenSessionDto>(
                    Error.Forbidden("El JWT del mesero no tiene el rol requerido."));
        }

        // The API wire format is screaming-snake ("SELF_SERVICE"), but the enum members are
        // PascalCase ("SelfService"). A plain case-insensitive Enum.Parse throws on the underscore
        // form, so map explicitly (this previously 500'd FIXED + SELF_SERVICE opens). The validator
        // already constrains both fields to the two allowed values each.
        // QR mode and PIN now come from the TABLE (the admin sets them per table). The cashdesk/waiter
        // no longer choose at open-time. Dynamic always authenticates by the rotating QR (no static PIN);
        // a Fixed table keeps a PIN unless the admin marked it "sin seguridad" (RequiresPin = false).
        var qrMode = table.QrMode;
        var serviceMode = cmd.ServiceMode.Equals("SELF_SERVICE", StringComparison.OrdinalIgnoreCase)
            ? ServiceMode.SelfService : ServiceMode.Staffed;

        // PRD-019 — ADR-040: el modo activo se congela en sessions.venue_mode y, en NIGHT, el PIN es
        // obligatorio en todas las mesas (DYNAMIC y «sin seguridad» incluidas) sin tocar el preset.
        VenueMode venueMode;
        if (modoDelGate is not null && Enum.TryParse(modoDelGate, out VenueMode leido)) venueMode = leido;
        else try { venueMode = await _venueMode.GetAsync(cmd.TenantId, ct); }
        catch (VenueModeUnavailableException ex)
        {
            _logger.LogError(ex, "sessions.session.venue_mode_unavailable {TenantId}", cmd.TenantId);
            return Result.Failure<OpenSessionDto>(Error.ServiceUnavailable(
                VenueModeUnavailableException.Code, "No se pudo leer el modo del local. Intenta de nuevo."));
        }

        bool pinRequired = venueMode == VenueMode.NIGHT || (qrMode == QrMode.Fixed && table.RequiresPin);

        var sessionId = Guid.NewGuid();

        // PIN único entre sesiones abiertas (pins_in_use). Se reserva antes de persistir: si el
        // sorteo se agota no se crea la sesión. Si Redis falla, PIN sin reserva (best-effort, como antes).
        string? pin = null;
        if (pinRequired)
        {
            try
            {
                pin = await _pinService.ReserveUniquePinAsync(_sessions, cmd.TenantId, sessionId, ct);
            }
            catch (PinPoolExhaustedException ex)
            {
                _logger.LogError(ex, "sessions.pin.pool_exhausted {TenantId} {SessionId}", cmd.TenantId, sessionId);
                return Result.Failure<OpenSessionDto>(Error.Failure(
                    "PIN_POOL_EXHAUSTED", "No se pudo generar un PIN libre. Intenta de nuevo."));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "sessions.pin.reserve_failed {TenantId} {SessionId}", cmd.TenantId, sessionId);
                pin = _pinService.GeneratePin();
            }
        }
        var session = new Session(
            sessionId,
            cmd.TenantId,
            table.Id,
            table.TableCode,
            qrMode,
            serviceMode,
            pinRequired,
            staffClaims?.StaffId,
            staffClaims?.StaffId,
            cmd.ShiftId,
            cmd.GuestCount,
            venueMode);

        await _sessions.AddAsync(session, ct);

        // First participant
        var participantDeviceId = cmd.ServiceMode == "SELF_SERVICE"
            ? cmd.DeviceId!
            : staffClaims!.StaffId.ToString();

        var participant = new SessionParticipant(
            Guid.NewGuid(), sessionId, participantDeviceId,
            null, isHost: true, inviteToken: null);

        await _sessions.AddParticipantAsync(participant, ct);
        await _sessions.SaveChangesAsync(ct);

        // Store active session in Redis
        try
        {
            await _cache.SetActiveSessionAsync(cmd.TenantId, cmd.TableCode, sessionId, SessionTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "sessions.cache.set_active_failed {TenantId} {SessionId}",
                cmd.TenantId, sessionId);
        }

        // Store PIN (12 h, ADR-040) if required
        if (pin is not null)
        {
            try
            {
                await _pinService.StorePinAsync(cmd.TenantId, sessionId, pin, PinTimings.PinTtl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "sessions.pin.store_failed {TenantId} {SessionId}",
                    cmd.TenantId, sessionId);
            }
        }

        var qrModeStr = qrMode.ToString().ToUpperInvariant();

        _logger.LogInformation(
            "sessions.session.opened {TenantId} {SessionId} {TableCode} {ServiceMode} {QrMode} {PinRequired} {OpenedBy} {VenueMode}",
            cmd.TenantId, sessionId, cmd.TableCode,
            cmd.ServiceMode, qrModeStr, pinRequired, staffClaims?.StaffId, venueMode.ToString());

        try
        {
            await _signalR.NotifyWaitersAsync(cmd.TenantId, new
            {
                type = "SESSION_OPENED",
                sessionId,
                tableCode = cmd.TableCode,
                waiterId = staffClaims?.StaffId,
                serviceMode = cmd.ServiceMode,
                qrMode = qrModeStr,
                pinRequired
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "sessions.signalr.session_opened_notify_failed {SessionId}", sessionId);
        }

        return Result.Success(new OpenSessionDto(
            sessionId,
            cmd.TenantId,
            table.TableCode,
            table.Id,
            "OPEN",
            qrModeStr,
            cmd.ServiceMode,
            pinRequired,
            pin,
            staffClaims?.StaffId,
            staffClaims?.StaffId,
            session.CreatedAt,
            participant.Id,
            session.GuestCount));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// JOIN SESSION
// ══════════════════════════════════════════════════════════════════════════════

public record JoinSessionDto(
    Guid ParticipantId,
    Guid SessionId,
    Guid TenantId,
    string TableCode,
    bool IsHost,
    bool PinRequired,
    bool PinValidated,
    string ServiceMode,
    List<object> OrderedItems);

public record JoinSessionCommand(
    Guid TenantId,
    string DeviceId,
    string JoinToken,
    string TokenType,           // "DYNAMIC_QR" or "INVITE"
    string? CustomerAccountJwt) : IRequest<Result<JoinSessionDto>>;

public sealed class JoinSessionValidator : AbstractValidator<JoinSessionCommand>
{
    public JoinSessionValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.DeviceId).NotEmpty().MaximumLength(100);
        RuleFor(c => c.JoinToken).NotEmpty();
        // TokenType is only a HINT — the handler auto-detects the kind from the (self-describing)
        // token, so a shared link whose `type` param got mangled in transit still joins correctly.
    }
}

public sealed class JoinSessionHandler : IRequestHandler<JoinSessionCommand, Result<JoinSessionDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionCacheService _cache;
    private readonly ISessionPinService _pinService;
    private readonly ISessionTokenService _tokenService;
    private readonly IVenueModeProvider _venueMode;
    private readonly ILogger<JoinSessionHandler> _logger;

    public JoinSessionHandler(
        ISessionRepository sessions,
        ISessionCacheService cache,
        ISessionPinService pinService,
        ISessionTokenService tokenService,
        IVenueModeProvider venueMode,
        ILogger<JoinSessionHandler> logger)
    {
        _sessions = sessions;
        _cache = cache;
        _pinService = pinService;
        _tokenService = tokenService;
        _venueMode = venueMode;
        _logger = logger;
    }

    public async Task<Result<JoinSessionDto>> Handle(JoinSessionCommand cmd, CancellationToken ct)
    {
        // Auto-detect the token kind from the token itself (it's self-describing). The client's
        // tokenType is only a hint to decide which kind to try first — a shared invite link whose
        // `type` param arrives mangled still joins. Each candidate is validated by signature AND
        // matched against its Redis copy (anti-replay).
        string hint = (cmd.TokenType ?? "").Trim().ToUpperInvariant() == "DYNAMIC_QR" ? "dynamic_qr" : "invite";
        string[] kindsToTry = hint == "dynamic_qr"
            ? new[] { "dynamic_qr", "invite" }
            : new[] { "invite", "dynamic_qr" };

        string? matchedKind = null;
        Guid sessionId = Guid.Empty, tenantId = Guid.Empty;
        foreach (var kind in kindsToTry)
        {
            if (!_tokenService.ValidateJoinToken(cmd.JoinToken, kind, out var sid, out var tid))
                continue;
            var stored = kind == "dynamic_qr"
                ? await _cache.GetQrTokenAsync(tid, sid)
                : await _cache.GetInviteTokenAsync(tid, sid);
            if (stored is not null && stored == cmd.JoinToken)
            {
                matchedKind = kind; sessionId = sid; tenantId = tid; break;
            }
        }

        if (matchedKind is null)
            return Result.Failure<JoinSessionDto>(
                Error.Unauthorized("Token inválido o expirado."));

        bool isInviteJoin = matchedKind == "invite";

        var session = await _sessions.GetByIdAsync(sessionId, ct);
        if (session is null || !session.IsOpen)
            return Result.Failure<JoinSessionDto>(
                Error.NotFound("Sesión no encontrada o cerrada."));

        // An INVITE link grants PIN-free access: the signed token IS the proof of access, so the
        // invitee never types the table PIN (the PIN is not exposed in the share URL).
        // PRD-019 — ADR-040: en NIGHT el enlace NO exime del PIN (nombre + PIN son la identidad de
        // la mesa). Modo activo; si no se puede leer, el congelado en la sesión (coinciden: el
        // cambio de modo exige el local vacío).
        VenueMode mode;
        try { mode = await _venueMode.GetAsync(tenantId, ct); }
        catch (VenueModeUnavailableException) { mode = session.VenueMode; }
        bool nightMode = mode == VenueMode.NIGHT;
        bool inviteGrantsPin = isInviteJoin && session.PinRequired && !nightMode;

        // Idempotent + concurrency-safe: one participant per (session, device), even if the same
        // device opens the invite link in multiple tabs at once.
        var candidate = new SessionParticipant(
            Guid.NewGuid(), sessionId, cmd.DeviceId,
            null, isHost: false, inviteToken: null);
        if (inviteGrantsPin) candidate.MarkPinValidated();

        var (participant, created) = await _sessions.AddOrGetParticipantAsync(candidate, ct);

        bool pinValidated;
        if (created)
        {
            if (inviteGrantsPin)
            {
                try { await _pinService.SetPinValidatedAsync(tenantId, sessionId, participant.Id, PinTimings.PinTtl); }
                catch { /* Redis unavailable — DB flag still set */ }
            }
            pinValidated = isInviteJoin && !nightMode;
        }
        else
        {
            // Re-join (sequential or race): keep the existing participant's PIN state (Postgres manda).
            pinValidated = await _pinService.ResolveAsync(tenantId, sessionId, participant);
        }

        _logger.LogInformation(
            "sessions.client.session_join {TenantId} {SessionId} {ParticipantId} {Kind} {Created}",
            tenantId, sessionId, participant.Id, matchedKind, created);

        return Result.Success(new JoinSessionDto(
            participant.Id, sessionId, tenantId, session.TableCode,
            participant.IsHost, session.PinRequired, pinValidated,
            session.ServiceMode.ToString().ToUpper(), []));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// JOIN BY TABLE (fixed QR — no token; resolves the active session of the table)
// ══════════════════════════════════════════════════════════════════════════════

public record JoinByTableCommand(
    Guid TenantId,
    string TableCode,
    string DeviceId) : IRequest<Result<JoinSessionDto>>;

public sealed class JoinByTableValidator : AbstractValidator<JoinByTableCommand>
{
    public JoinByTableValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.TableCode).NotEmpty().MaximumLength(50);
        RuleFor(c => c.DeviceId).NotEmpty().MaximumLength(100);
    }
}

public sealed class JoinByTableHandler : IRequestHandler<JoinByTableCommand, Result<JoinSessionDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionPinService _pinService;
    private readonly ITableRepository _tables;
    private readonly IFloorRepository _floors;
    private readonly IVenueModeProvider _venueMode;
    private readonly ILogger<JoinByTableHandler> _logger;

    public JoinByTableHandler(
        ISessionRepository sessions,
        ISessionPinService pinService,
        ITableRepository tables,
        IFloorRepository floors,
        IVenueModeProvider venueMode,
        ILogger<JoinByTableHandler> logger)
    {
        _sessions = sessions;
        _pinService = pinService;
        _tables = tables;
        _floors = floors;
        _venueMode = venueMode;
        _logger = logger;
    }

    public async Task<Result<JoinSessionDto>> Handle(JoinByTableCommand cmd, CancellationToken ct)
    {
        // PRD-019: antes de buscar la sesión, la mesa debe estar en servicio en el modo activo
        // (piso visible + fila del modo activa). Una mesa cerrada responde 409, no 404.
        var table = await _tables.GetByCodeAsync(cmd.TenantId, cmd.TableCode, ct);
        if (table is not null)
        {
            var (closed, mode) = await TableServiceGate.CheckAsync(_tables, _floors, _venueMode, table, ct);
            if (closed is not null)
            {
                if (closed.Code == TableModeRules.TableClosedInModeCode)
                    _logger.LogWarning("sessions.session.table_closed_in_mode {TenantId} {TableCode} {Mode}",
                        cmd.TenantId, cmd.TableCode, mode);
                return Result.Failure<JoinSessionDto>(closed);
            }
        }

        // The fixed table QR carries only tenant + tableCode; resolve the active session.
        var session = await _sessions.GetOpenByTableCodeAsync(cmd.TenantId, cmd.TableCode, ct);
        if (session is null)
            return Result.Failure<JoinSessionDto>(
                Error.NotFound("No hay una mesa abierta con este código. Pide al mesero que abra la mesa."));

        // Idempotent + concurrency-safe: one participant per (session, device), even if the same
        // device opens the link in multiple tabs at once.
        var candidate = new SessionParticipant(
            Guid.NewGuid(), session.Id, cmd.DeviceId,
            null, isHost: false, inviteToken: null);
        var (participant, created) = await _sessions.AddOrGetParticipantAsync(candidate, ct);

        bool pinValidated = false;
        if (!created)
            pinValidated = await _pinService.ResolveAsync(cmd.TenantId, session.Id, participant);

        _logger.LogInformation(
            "sessions.client.join_by_table {TenantId} {SessionId} {ParticipantId} {TableCode} {Created}",
            cmd.TenantId, session.Id, participant.Id, cmd.TableCode, created);

        return Result.Success(new JoinSessionDto(
            participant.Id, session.Id, cmd.TenantId, session.TableCode,
            participant.IsHost, session.PinRequired, pinValidated,
            session.ServiceMode.ToString().ToUpper(), []));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// ADD PARTICIPANT (Staff)
// ══════════════════════════════════════════════════════════════════════════════

public record AddParticipantDto(
    Guid ParticipantId,
    Guid SessionId,
    string DeviceId,
    Guid? CustomerAccountId,
    bool IsHost,
    bool PinValidated,
    DateTime JoinedAt);

public record AddParticipantCommand(
    Guid TenantId,
    Guid SessionId,
    string DeviceId,
    Guid? CustomerAccountId,
    bool IsHost,
    Guid RequesterId) : IRequest<Result<AddParticipantDto>>;

public sealed class AddParticipantValidator : AbstractValidator<AddParticipantCommand>
{
    public AddParticipantValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.DeviceId).NotEmpty().MaximumLength(100);
    }
}

public sealed class AddParticipantHandler
    : IRequestHandler<AddParticipantCommand, Result<AddParticipantDto>>
{
    // Staff-created participants are trusted (the staff opened the walk-in session),
    // so we pre-mark their PIN as validated for the rest of the session: column (truth, ADR-040)
    // + Redis cache with PinTimings.PinTtl (12 h).

    private readonly ISessionRepository _sessions;
    private readonly ISessionPinService _pinService;
    private readonly ILogger<AddParticipantHandler> _logger;

    public AddParticipantHandler(
        ISessionRepository sessions,
        ISessionPinService pinService,
        ILogger<AddParticipantHandler> logger)
    {
        _sessions = sessions;
        _pinService = pinService;
        _logger = logger;
    }

    public async Task<Result<AddParticipantDto>> Handle(AddParticipantCommand cmd, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(cmd.SessionId, ct);
        if (session is null || !session.IsOpen || session.TenantId != cmd.TenantId)
            return Result.Failure<AddParticipantDto>(
                Error.NotFound("Sesión no encontrada o cerrada."));

        // Idempotency
        var existing = await _sessions.GetParticipantAsync(cmd.SessionId, cmd.DeviceId, ct);
        if (existing is not null)
        {
            // Ensure the staff participant can order even if the PIN flag expired/was lost.
            if (!existing.PinValidated)
            {
                existing.MarkPinValidated();
                await _sessions.SaveChangesAsync(ct);
            }
            await MarkPinValidated(cmd.TenantId, cmd.SessionId, existing.Id);
            return Result.Success(new AddParticipantDto(
                existing.Id, cmd.SessionId, existing.DeviceId,
                existing.CustomerAccountId, existing.IsHost, true,
                existing.JoinedAt));
        }

        // Prevent duplicate host
        if (cmd.IsHost)
        {
            var participants = await _sessions.GetParticipantsAsync(cmd.SessionId, ct);
            if (participants.Any(p => p.IsHost))
                return Result.Failure<AddParticipantDto>(
                    Error.Validation("DUPLICATE_HOST", "Ya existe un participante host en esta sesión."));
        }

        var candidate = new SessionParticipant(
            Guid.NewGuid(), cmd.SessionId, cmd.DeviceId,
            cmd.CustomerAccountId, cmd.IsHost, null);
        candidate.MarkPinValidated(); // persisted in session_participants.pin_validated
        var (participant, _) = await _sessions.AddOrGetParticipantAsync(candidate, ct);
        if (!participant.PinValidated)
        {
            // Race branch: the winner was created elsewhere without the flag.
            participant.MarkPinValidated();
            await _sessions.SaveChangesAsync(ct);
        }

        // Trusted staff participant: skip the PIN gate when ordering on their behalf.
        await MarkPinValidated(cmd.TenantId, cmd.SessionId, participant.Id);

        _logger.LogInformation(
            "sessions.staff.participant_added {TenantId} {SessionId} {ParticipantId} {DeviceId} {RequesterId}",
            cmd.TenantId, cmd.SessionId, participant.Id, cmd.DeviceId, cmd.RequesterId);

        return Result.Success(new AddParticipantDto(
            participant.Id, cmd.SessionId, cmd.DeviceId,
            cmd.CustomerAccountId, cmd.IsHost, true, participant.JoinedAt));
    }

    private async Task MarkPinValidated(Guid tenantId, Guid sessionId, Guid participantId)
    {
        try
        {
            await _pinService.SetPinValidatedAsync(tenantId, sessionId, participantId, PinTimings.PinTtl);
        }
        catch (Exception ex)
        {
            // Non-fatal: ordering will still fail closed if the session actually requires a PIN.
            _logger.LogWarning(ex,
                "sessions.staff.pin_validate_failed {TenantId} {SessionId} {ParticipantId}",
                tenantId, sessionId, participantId);
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// CLOSE SESSION
// ══════════════════════════════════════════════════════════════════════════════

public record CloseSessionDto(
    Guid SessionId,
    string Status,
    DateTime ClosedAt,
    Guid ClosedBy,
    string TableCode,
    long Duration);

public record CloseSessionCommand(
    Guid TenantId,
    Guid SessionId,
    string ClosureReason,
    Guid RequesterId) : IRequest<Result<CloseSessionDto>>;

public sealed class CloseSessionValidator : AbstractValidator<CloseSessionCommand>
{
    public CloseSessionValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.ClosureReason)
            .Must(r => r is "PAYMENT_COMPLETED" or "MANUAL_CLOSE" or "SYSTEM_TIMEOUT")
            .WithMessage("closureReason inválido.");
    }
}

public sealed class CloseSessionHandler : IRequestHandler<CloseSessionCommand, Result<CloseSessionDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionCacheService _cache;
    private readonly ISessionPinService _pinService;
    private readonly ISignalRNotifier _signalR;
    private readonly ILogger<CloseSessionHandler> _logger;

    public CloseSessionHandler(
        ISessionRepository sessions,
        ISessionCacheService cache,
        ISessionPinService pinService,
        ISignalRNotifier signalR,
        ILogger<CloseSessionHandler> logger)
    {
        _sessions = sessions;
        _cache = cache;
        _pinService = pinService;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<CloseSessionDto>> Handle(CloseSessionCommand cmd, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(cmd.SessionId, ct);
        if (session is null || !session.IsOpen || session.TenantId != cmd.TenantId)
            return Result.Failure<CloseSessionDto>(
                Error.NotFound("Sesión no encontrada o ya cerrada."));

        // Guard: no active orders
        bool hasActive = await _sessions.HasActiveOrdersAsync(cmd.SessionId, ct);
        if (hasActive)
            return Result.Failure<CloseSessionDto>(
                Error.Conflict("Existen pedidos activos (PENDING o IN_PREPARATION). Cierre los pedidos antes de cerrar la sesión."));

        session.Close(cmd.RequesterId);
        await _sessions.SaveChangesAsync(ct);

        long duration = (long)(session.ClosedAt!.Value - session.CreatedAt).TotalSeconds;

        // Redis cleanup (best-effort)
        try
        {
            await _cache.DeleteSessionTokensAsync(cmd.TenantId, cmd.SessionId, session.TableCode);
            await _pinService.DeletePinAsync(cmd.TenantId, cmd.SessionId);
            await _pinService.ReleasePinsAsync(cmd.TenantId, cmd.SessionId); // pins_in_use, por sessionId
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "sessions.cajero.session_close_redis_cleanup_failed {TenantId} {SessionId}",
                cmd.TenantId, cmd.SessionId);
        }

        // SignalR notification
        try
        {
            await _signalR.NotifyWaitersAsync(cmd.TenantId,
                new { type = "SESSION_CLOSED", sessionId = cmd.SessionId, tableCode = session.TableCode }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "sessions.signalr.session_closed_notify_failed {SessionId}", cmd.SessionId);
        }

        _logger.LogInformation(
            "sessions.cajero.session_closed {TenantId} {SessionId} {TableCode} {ClosureReason} {Duration} {ClosedBy}",
            cmd.TenantId, cmd.SessionId, session.TableCode, cmd.ClosureReason, duration, cmd.RequesterId);

        return Result.Success(new CloseSessionDto(
            cmd.SessionId, "CLOSED", session.ClosedAt!.Value,
            cmd.RequesterId, session.TableCode, duration));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// UPDATE SESSION WAITER
// ══════════════════════════════════════════════════════════════════════════════

public record UpdateSessionWaiterDto(
    Guid SessionId,
    Guid? WaiterId,
    string? WaiterName,
    DateTime UpdatedAt);

public record UpdateSessionWaiterCommand(
    Guid TenantId,
    Guid SessionId,
    Guid? WaiterId,    // null = unassign
    Guid RequesterId) : IRequest<Result<UpdateSessionWaiterDto>>;

public sealed class UpdateSessionWaiterValidator : AbstractValidator<UpdateSessionWaiterCommand>
{
    public UpdateSessionWaiterValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
    }
}

public sealed class UpdateSessionWaiterHandler
    : IRequestHandler<UpdateSessionWaiterCommand, Result<UpdateSessionWaiterDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly ISignalRNotifier _signalR;
    private readonly ILogger<UpdateSessionWaiterHandler> _logger;

    public UpdateSessionWaiterHandler(
        ISessionRepository sessions,
        ISignalRNotifier signalR,
        ILogger<UpdateSessionWaiterHandler> logger)
    {
        _sessions = sessions;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<UpdateSessionWaiterDto>> Handle(
        UpdateSessionWaiterCommand cmd, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(cmd.SessionId, ct);
        if (session is null || !session.IsOpen || session.TenantId != cmd.TenantId)
            return Result.Failure<UpdateSessionWaiterDto>(
                Error.NotFound("Sesión no encontrada o cerrada."));

        Guid? previousWaiterId = session.WaiterId;
        string? waiterName = null;

        if (cmd.WaiterId.HasValue)
        {
            waiterName = await _sessions.GetStaffNameAsync(cmd.WaiterId.Value, ct);
            if (waiterName is null)
                return Result.Failure<UpdateSessionWaiterDto>(
                    Error.NotFound("Mesero no encontrado en este tenant."));
        }

        session.AssignWaiter(cmd.WaiterId);
        await _sessions.SaveChangesAsync(ct);

        try
        {
            await _signalR.NotifyWaitersAsync(cmd.TenantId, new
            {
                type = "WAITER_REASSIGNED",
                sessionId = cmd.SessionId,
                tableCode = session.TableCode,
                newWaiterId = cmd.WaiterId
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "sessions.signalr.waiter_reassigned_notify_failed {SessionId}", cmd.SessionId);
        }

        _logger.LogInformation(
            "sessions.admin.waiter_reassigned {TenantId} {SessionId} {PreviousWaiterId} {NewWaiterId} {RequesterId}",
            cmd.TenantId, cmd.SessionId, previousWaiterId, cmd.WaiterId, cmd.RequesterId);

        return Result.Success(new UpdateSessionWaiterDto(
            cmd.SessionId, cmd.WaiterId, waiterName, DateTime.UtcNow));
    }
}
