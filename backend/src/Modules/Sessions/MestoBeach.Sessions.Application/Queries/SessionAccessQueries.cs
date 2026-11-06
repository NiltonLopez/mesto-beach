using MediatR;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Sessions.Application.Queries;

public record SessionPinDto(Guid SessionId, string TableCode, string Pin);

public record GetSessionPinQuery(Guid TenantId, Guid SessionId) : IRequest<Result<SessionPinDto>>;

public sealed class GetSessionPinHandler : IRequestHandler<GetSessionPinQuery, Result<SessionPinDto>>
{
    private readonly ISessionRepository _sessions;
    private readonly ISessionPinService _pins;

    public GetSessionPinHandler(ISessionRepository sessions, ISessionPinService pins)
    {
        _sessions = sessions;
        _pins = pins;
    }

    public async Task<Result<SessionPinDto>> Handle(GetSessionPinQuery q, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(q.SessionId, ct);
        if (session is null || !session.IsOpen || session.TenantId != q.TenantId)
            return Result.Failure<SessionPinDto>(Error.NotFound("Sesión no encontrada o cerrada."));

        if (!session.PinRequired)
            return Result.Failure<SessionPinDto>(Error.Validation("PIN_NOT_REQUIRED", "Esta sesión no requiere PIN."));

        var pin = await _pins.GetPinAsync(q.TenantId, q.SessionId);
        if (pin is null)
            return Result.Failure<SessionPinDto>(Error.NotFound("PIN no encontrado para esta sesión."));

        return Result.Success(new SessionPinDto(q.SessionId, session.TableCode, pin));
    }
}

public record SessionJoinTokenDto(
    Guid SessionId,
    Guid TenantId,
    string TableCode,
    string TokenType,
    string Token,
    string JoinUrl);

public record GenerateSessionJoinTokenQuery(Guid TenantId, Guid SessionId, string TokenType)
    : IRequest<Result<SessionJoinTokenDto>>;

public sealed class GenerateSessionJoinTokenHandler : IRequestHandler<GenerateSessionJoinTokenQuery, Result<SessionJoinTokenDto>>
{
    private static readonly TimeSpan TokenTtl = TimeSpan.FromHours(4);

    private readonly ISessionRepository _sessions;
    private readonly ITenantSettingsService _tenantSettings;
    private readonly ISessionTokenService _tokenService;
    private readonly ISessionCacheService _cache;
    private readonly IMenuUrlProvider _menuUrls;

    public GenerateSessionJoinTokenHandler(
        ISessionRepository sessions,
        ITenantSettingsService tenantSettings,
        ISessionTokenService tokenService,
        ISessionCacheService cache,
        IMenuUrlProvider menuUrls)
    {
        _sessions = sessions;
        _tenantSettings = tenantSettings;
        _tokenService = tokenService;
        _cache = cache;
        _menuUrls = menuUrls;
    }

    public async Task<Result<SessionJoinTokenDto>> Handle(GenerateSessionJoinTokenQuery q, CancellationToken ct)
    {
        var session = await _sessions.GetByIdAsync(q.SessionId, ct);
        if (session is null || !session.IsOpen || session.TenantId != q.TenantId)
            return Result.Failure<SessionJoinTokenDto>(Error.NotFound("Sesión no encontrada o cerrada."));

        if (q.TokenType == "DYNAMIC_QR" && session.QrMode.ToString().ToUpperInvariant() != "DYNAMIC")
            return Result.Failure<SessionJoinTokenDto>(Error.Validation("QR_MODE_INVALID", "La sesión no usa QR dinámico."));

        if (q.TokenType is not ("DYNAMIC_QR" or "INVITE"))
            return Result.Failure<SessionJoinTokenDto>(Error.Validation("TOKEN_TYPE_INVALID", "tokenType debe ser DYNAMIC_QR o INVITE."));

        var tokenKind = q.TokenType == "DYNAMIC_QR" ? "dynamic_qr" : "invite";
        var token = _tokenService.GenerateJoinToken(q.SessionId, q.TenantId, tokenKind, TokenTtl);

        if (q.TokenType == "DYNAMIC_QR")
            await _cache.StoreQrTokenAsync(q.TenantId, q.SessionId, token, TokenTtl);
        else
            await _cache.StoreInviteTokenAsync(q.TenantId, q.SessionId, token, TokenTtl);

        var settings = await _tenantSettings.GetAsync(q.TenantId, ct);
        var slug = settings?.Slug ?? q.TenantId.ToString();
        // La base sale de Frontend:BaseUrl, no de localhost: este enlace acaba en un QR
        // que se escanea con un móvil, y allí `localhost` sería el propio teléfono.
        var joinUrl = $"{_menuUrls.MenuBaseUrl}/join?tenantId={q.TenantId}&sessionId={q.SessionId}&token={Uri.EscapeDataString(token)}&type={q.TokenType}";

        if (q.TokenType == "DYNAMIC_QR")
            joinUrl += $"&slug={Uri.EscapeDataString(slug)}";

        return Result.Success(new SessionJoinTokenDto(
            q.SessionId,
            q.TenantId,
            session.TableCode,
            q.TokenType,
            token,
            joinUrl));
    }
}
