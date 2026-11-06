using FluentValidation;
using MediatR;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Sessions.Application.Queries;

// ─── DTO ─────────────────────────────────────────────────────────────────────

public record SessionByTableDto(
    Guid SessionId,
    Guid TenantId,
    string TableCode,
    string Status,
    string ServiceMode,
    bool PinRequired,
    int ParticipantCount,
    DateTime CreatedAt);

// ─── Query ────────────────────────────────────────────────────────────────────

public record GetSessionByTableQuery(
    Guid TenantId,
    string TableCode) : IRequest<Result<SessionByTableDto>>;

// ─── Validator ────────────────────────────────────────────────────────────────

public sealed class GetSessionByTableValidator : AbstractValidator<GetSessionByTableQuery>
{
    public GetSessionByTableValidator()
    {
        RuleFor(q => q.TenantId).NotEmpty().WithMessage("tenantId es requerido.");
        RuleFor(q => q.TableCode)
            .NotEmpty().WithMessage("tableCode es requerido.")
            .MaximumLength(50);
    }
}

// ─── Handler ─────────────────────────────────────────────────────────────────

public sealed class GetSessionByTableHandler
    : IRequestHandler<GetSessionByTableQuery, Result<SessionByTableDto>>
{
    private readonly ISessionCacheService _cache;
    private readonly ISessionRepository _sessions;
    private readonly ITableRepository _tables;
    private readonly ITenantSettingsService _tenantSettings;

    public GetSessionByTableHandler(
        ISessionCacheService cache,
        ISessionRepository sessions,
        ITableRepository tables,
        ITenantSettingsService tenantSettings)
    {
        _cache = cache;
        _sessions = sessions;
        _tables = tables;
        _tenantSettings = tenantSettings;
    }

    public async Task<Result<SessionByTableDto>> Handle(
        GetSessionByTableQuery q, CancellationToken ct)
    {
        // Verify table exists
        var table = await _tables.GetByCodeAsync(q.TenantId, q.TableCode, ct);
        if (table is null)
            return Result.Failure<SessionByTableDto>(
                Error.NotFound($"Mesa '{q.TableCode}' no existe para el tenant."));

        // Try Redis first
        Domain.Entities.Session? session = null;
        try
        {
            var sessionId = await _cache.GetActiveSessionIdAsync(q.TenantId, q.TableCode);
            if (sessionId.HasValue)
                session = await _sessions.GetByIdAsync(sessionId.Value, ct);
        }
        catch { /* Redis unavailable — fall through */ }

        // Fallback to DB
        session ??= await _sessions.GetOpenByTableCodeAsync(q.TenantId, q.TableCode, ct);

        if (session is null)
            return Result.Failure<SessionByTableDto>(
                Error.NotFound("No hay sesión activa para esta mesa."));

        var settings = await _tenantSettings.GetAsync(q.TenantId, ct);
        int participantCount = await _sessions.CountParticipantsAsync(session.Id, ct);

        return Result.Success(new SessionByTableDto(
            session.Id,
            session.TenantId,
            session.TableCode,
            session.Status.ToString().ToUpper(),
            session.ServiceMode.ToString().ToUpper(),
            settings?.PinRequired ?? session.PinRequired,
            participantCount,
            session.CreatedAt));
    }
}
