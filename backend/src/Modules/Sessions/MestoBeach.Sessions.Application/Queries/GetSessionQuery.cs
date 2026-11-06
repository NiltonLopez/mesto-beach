using FluentValidation;
using MediatR;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Sessions.Application.Queries;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record ParticipantDto(
    Guid ParticipantId,
    string? DeviceId,   // null for non-staff callers viewing other participants
    bool IsHost,
    bool PinValidated,
    DateTime JoinedAt,
    string? DisplayName,
    bool HasAccount);

public record SessionDetailDto(
    Guid SessionId,
    Guid TenantId,
    string TableCode,
    Guid TableId,
    string Status,
    string QrMode,
    string ServiceMode,
    bool PinRequired,
    Guid? WaiterId,
    string? WaiterName,
    DateTime CreatedAt,
    DateTime? ClosedAt,
    List<ParticipantDto> Participants,
    OrdersSummaryDto OrdersSummary);

// ─── Query ────────────────────────────────────────────────────────────────────

/// <param name="CallerDeviceId">Non-null when the caller is a client (not staff).</param>
/// <param name="IsStaffCaller">True when JWT is a staff/platform token.</param>
public record GetSessionQuery(
    Guid SessionId,
    string? CallerDeviceId,
    bool IsStaffCaller,
    Guid? RequesterId) : IRequest<Result<SessionDetailDto>>;

// ─── Validator ────────────────────────────────────────────────────────────────

public sealed class GetSessionValidator : AbstractValidator<GetSessionQuery>
{
    public GetSessionValidator()
    {
        RuleFor(q => q.SessionId).NotEmpty();
        RuleFor(q => q.CallerDeviceId)
            .NotEmpty()
            .When(q => !q.IsStaffCaller)
            .WithMessage("deviceId es requerido para callers que no son staff.");
    }
}

// ─── Handler ─────────────────────────────────────────────────────────────────

public sealed class GetSessionHandler : IRequestHandler<GetSessionQuery, Result<SessionDetailDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionPinService _pinService;
    private readonly IOrderSummaryService _orders;

    public GetSessionHandler(
        ISessionRepository sessions,
        ISessionPinService pinService,
        IOrderSummaryService orders)
    {
        _sessions = sessions;
        _pinService = pinService;
        _orders = orders;
    }

    public async Task<Result<SessionDetailDto>> Handle(GetSessionQuery q, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(q.SessionId, ct);
        if (session is null)
            return Result.Failure<SessionDetailDto>(
                Error.NotFound("Sesión no encontrada."));

        var participants = await _sessions.GetParticipantsAsync(session.Id, ct);

        // Client caller auth: deviceId must belong to a participant
        if (!q.IsStaffCaller)
        {
            bool belongs = participants.Any(p => p.DeviceId == q.CallerDeviceId);
            if (!belongs)
                return Result.Failure<SessionDetailDto>(
                    Error.Forbidden("El deviceId no pertenece a esta sesión."));
        }

        // Resolve waiter name
        string? waiterName = session.WaiterId.HasValue
            ? await _sessions.GetStaffNameAsync(session.WaiterId.Value, ct)
            : null;

        // Build participant DTOs (hide deviceId from non-staff callers)
        var participantDtos = new List<ParticipantDto>(participants.Count);
        foreach (var p in participants)
        {
            // Postgres manda; Redis es caché y se re-siembra con 12 h si falta (ADR-040).
            bool pinValidated = await _pinService.ResolveAsync(session.TenantId, session.Id, p);

            participantDtos.Add(new ParticipantDto(
                p.Id,
                q.IsStaffCaller ? p.DeviceId : null,
                p.IsHost,
                pinValidated,
                p.JoinedAt,
                p.DisplayName,
                p.CustomerAccountId.HasValue));
        }

        var ordersSummary = await _orders.GetSummaryAsync(session.Id, ct);

        return Result.Success(new SessionDetailDto(
            session.Id,
            session.TenantId,
            session.TableCode,
            session.TableId,
            session.Status.ToString().ToUpper(),
            session.QrMode.ToString().ToUpper(),
            session.ServiceMode.ToString().ToUpper(),
            session.PinRequired,
            session.WaiterId,
            waiterName,
            session.CreatedAt,
            session.ClosedAt,
            participantDtos,
            ordersSummary));
    }
}
