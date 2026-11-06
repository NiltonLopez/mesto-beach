using FluentValidation;
using MediatR;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace MestoBeach.Sessions.Application.Commands;

// ══════════════════════════════════════════════════════════════════════════════
// VALIDATE PIN
// ══════════════════════════════════════════════════════════════════════════════

public record ValidatePinDto(
    bool Validated,
    int? RemainingAttempts,
    Guid ParticipantId,
    Guid SessionId);

public record ValidatePinCommand(
    Guid SessionId,
    Guid TenantId,
    Guid ParticipantId,
    string DeviceId,
    string Pin) : IRequest<Result<ValidatePinDto>>;

public sealed class ValidatePinValidator : AbstractValidator<ValidatePinCommand>
{
    public ValidatePinValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.ParticipantId).NotEmpty();
        RuleFor(c => c.DeviceId).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Pin)
            .NotEmpty()
            .Length(4).WithMessage("El PIN debe ser exactamente 4 dígitos.")
            .Matches(@"^\d{4}$").WithMessage("El PIN solo puede contener dígitos numéricos.");
    }
}

public sealed class ValidatePinHandler : IRequestHandler<ValidatePinCommand, Result<ValidatePinDto>>
{
    private const int MaxAttempts = 5;

    private readonly ISessionRepository _sessions;
    private readonly ISessionPinService _pinService;
    private readonly ILogger<ValidatePinHandler> _logger;

    public ValidatePinHandler(
        ISessionRepository sessions,
        ISessionPinService pinService,
        ILogger<ValidatePinHandler> logger)
    {
        _sessions = sessions;
        _pinService = pinService;
        _logger = logger;
    }

    public async Task<Result<ValidatePinDto>> Handle(ValidatePinCommand cmd, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(cmd.SessionId, ct);
        if (session is null || !session.IsOpen)
            return Result.Failure<ValidatePinDto>(Error.NotFound("Sesión no encontrada o cerrada."));

        if (session.TenantId != cmd.TenantId)
            return Result.Failure<ValidatePinDto>(
                Error.Validation("TENANT_MISMATCH", "tenantId no coincide con la sesión."));

        var participant = await _sessions.GetParticipantByIdAsync(cmd.ParticipantId, ct);
        if (participant is null || participant.SessionId != cmd.SessionId)
            return Result.Failure<ValidatePinDto>(
                Error.NotFound("Participante no encontrado en esta sesión."));

        if (participant.DeviceId != cmd.DeviceId)
            return Result.Failure<ValidatePinDto>(
                Error.Validation("DEVICE_MISMATCH", "deviceId no coincide con el participante."));

        // Already validated?
        bool alreadyValidated;
        try
        {
            alreadyValidated = await _pinService.IsPinValidatedAsync(
                cmd.TenantId, cmd.SessionId, cmd.ParticipantId);
        }
        catch
        {
            alreadyValidated = false;
        }
        // Postgres manda (ADR-040): la columna validada vale aunque la clave Redis haya caducado.
        alreadyValidated = alreadyValidated || participant.PinValidated;

        if (alreadyValidated)
            return Result.Failure<ValidatePinDto>(
                Error.Gone("El PIN ya fue validado para este participante."));

        // Check lockout BEFORE reading correct PIN
        int attempts = await _pinService.IncrPinAttemptsAsync(
            cmd.TenantId, session.TableCode, cmd.DeviceId);

        if (attempts > MaxAttempts)
        {
            _logger.LogWarning(
                "sessions.client.pin_locked {TenantId} {SessionId} {TableCode} {DeviceId}",
                cmd.TenantId, cmd.SessionId, session.TableCode, cmd.DeviceId);
            return Result.Failure<ValidatePinDto>(
                Error.Locked("Límite de intentos alcanzado. Bloqueado por 15 minutos."));
        }

        // Get correct PIN
        var correctPin = await _pinService.GetPinAsync(cmd.TenantId, cmd.SessionId);
        if (correctPin is null)
            return Result.Failure<ValidatePinDto>(
                Error.NotFound("PIN no encontrado para esta sesión."));

        // Constant-time comparison to prevent timing attacks
        bool correct = CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(cmd.Pin),
            System.Text.Encoding.UTF8.GetBytes(correctPin));

        if (correct)
        {
            try
            {
                await _pinService.SetPinValidatedAsync(
                    cmd.TenantId, cmd.SessionId, cmd.ParticipantId, PinTimings.PinTtl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "sessions.pin.set_validated_redis_failed {ParticipantId}", cmd.ParticipantId);
            }

            participant.MarkPinValidated();
            await _sessions.SaveChangesAsync(ct);

            _logger.LogInformation(
                "sessions.client.pin_validated {TenantId} {SessionId} {ParticipantId}",
                cmd.TenantId, cmd.SessionId, cmd.ParticipantId);

            return Result.Success(new ValidatePinDto(true, null, cmd.ParticipantId, cmd.SessionId));
        }

        int remaining = Math.Max(0, MaxAttempts - attempts);

        _logger.LogInformation(
            "sessions.client.pin_failed {TenantId} {SessionId} {ParticipantId} {Attempt}",
            cmd.TenantId, cmd.SessionId, cmd.ParticipantId, attempts);

        return Result.Success(new ValidatePinDto(false, remaining, cmd.ParticipantId, cmd.SessionId));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// REGENERATE PIN
// ══════════════════════════════════════════════════════════════════════════════

public record RegeneratePinDto(Guid SessionId, string NewPin, DateTime RegeneratedAt);

public record RegeneratePinCommand(
    Guid TenantId,
    Guid SessionId,
    Guid RequesterId) : IRequest<Result<RegeneratePinDto>>;

public sealed class RegeneratePinHandler
    : IRequestHandler<RegeneratePinCommand, Result<RegeneratePinDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionPinService _pinService;
    private readonly ISignalRNotifier _signalR;
    private readonly ILogger<RegeneratePinHandler> _logger;

    public RegeneratePinHandler(
        ISessionRepository sessions,
        ISessionPinService pinService,
        ISignalRNotifier signalR,
        ILogger<RegeneratePinHandler> logger)
    {
        _sessions = sessions;
        _pinService = pinService;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<RegeneratePinDto>> Handle(
        RegeneratePinCommand cmd, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(cmd.SessionId, ct);
        if (session is null || !session.IsOpen || session.TenantId != cmd.TenantId)
            return Result.Failure<RegeneratePinDto>(
                Error.NotFound("Sesión no encontrada o cerrada."));

        // ADR-040: libera el PIN anterior por sessionId (sin leer la clave, que puede haber
        // caducado) y reserva uno nuevo, único entre sesiones abiertas.
        string newPin;
        try
        {
            await _pinService.ReleasePinsAsync(cmd.TenantId, cmd.SessionId);
            newPin = await _pinService.ReserveUniquePinAsync(_sessions, cmd.TenantId, cmd.SessionId, ct);
        }
        catch (PinPoolExhaustedException ex)
        {
            _logger.LogError(ex, "sessions.pin.pool_exhausted {TenantId} {SessionId}", cmd.TenantId, cmd.SessionId);
            return Result.Failure<RegeneratePinDto>(Error.Failure(
                "PIN_POOL_EXHAUSTED", "No se pudo generar un PIN libre. Intenta de nuevo."));
        }
        await _pinService.UpdatePinKeepTtlAsync(cmd.TenantId, cmd.SessionId, newPin);

        // SignalR — do NOT include the PIN
        try
        {
            await _signalR.NotifyWaitersAsync(cmd.TenantId, new
            {
                type = "PIN_REGENERATED",
                sessionId = cmd.SessionId,
                tableCode = session.TableCode
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "sessions.signalr.pin_regenerated_notify_failed {SessionId}", cmd.SessionId);
        }

        _logger.LogInformation(
            "sessions.cajero.pin_regenerated {TenantId} {SessionId} {TableCode} {RequesterId}",
            cmd.TenantId, cmd.SessionId, session.TableCode, cmd.RequesterId);

        return Result.Success(new RegeneratePinDto(
            cmd.SessionId, newPin, DateTime.UtcNow));
    }
}
