using FluentValidation;
using MediatR;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Identity.Application.Auth.Platform.Commands;

// ─── Start Impersonation ──────────────────────────────────────────────────────

public sealed record StartImpersonationCommand(
    Guid OpsUserId,
    Guid TargetTenantId,
    string Reason
) : IRequest<Result<StartImpersonationResponse>>;

public sealed record StartImpersonationResponse(
    string ImpersonationToken,
    int ExpiresIn,
    Guid TenantId,
    string RedirectUrl
);

public sealed class StartImpersonationCommandValidator : AbstractValidator<StartImpersonationCommand>
{
    public StartImpersonationCommandValidator()
    {
        RuleFor(x => x.TargetTenantId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().Length(5, 500)
            .WithMessage("reason debe tener entre 5 y 500 caracteres.");
    }
}

public sealed class StartImpersonationCommandHandler
    : IRequestHandler<StartImpersonationCommand, Result<StartImpersonationResponse>>
{
    private readonly ITenantRepository _tenants;
    private readonly IJwtService _jwt;
    private readonly IImpersonationSessionService _sessions;
    private readonly IAuditLogService _audit;
    private readonly IFrontendUrlService _urls;
    private readonly ILogger<StartImpersonationCommandHandler> _log;

    public StartImpersonationCommandHandler(
        ITenantRepository tenants,
        IJwtService jwt,
        IImpersonationSessionService sessions,
        IAuditLogService audit,
        IFrontendUrlService urls,
        ILogger<StartImpersonationCommandHandler> log)
    {
        _tenants = tenants;
        _jwt = jwt;
        _sessions = sessions;
        _audit = audit;
        _urls = urls;
        _log = log;
    }

    public async Task<Result<StartImpersonationResponse>> Handle(
        StartImpersonationCommand cmd,
        CancellationToken ct)
    {
        // Verify tenant exists and is active
        var tenant = await _tenants.GetByIdAsync(cmd.TargetTenantId, ct);
        if (tenant is null || tenant.Status != Domain.Enums.TenantStatus.Active)
            return Result.Failure<StartImpersonationResponse>(
                Error.NotFound("Tenant no encontrado."));

        // Check for existing active session
        if (await _sessions.ExistsAsync(cmd.OpsUserId, cmd.TargetTenantId, ct))
            return Result.Failure<StartImpersonationResponse>(
                Error.Conflict("Ya existe una sesión de impersonación activa para este tenant. Use POST /impersonate/renew o finalícela primero."));

        // Issue token
        var jti = Guid.NewGuid().ToString();
        var token = _jwt.GenerateImpersonationToken(cmd.OpsUserId, cmd.TargetTenantId, jti);

        // Store in Redis
        await _sessions.SetAsync(cmd.OpsUserId, cmd.TargetTenantId, jti, cmd.Reason, ct);

        var redirectUrl = _urls.AdminImpersonateUrl(token);

        await _audit.RecordAsync(
            "impersonation_started", cmd.OpsUserId, cmd.TargetTenantId.ToString(), cmd.TargetTenantId,
            new Dictionary<string, object?> { ["reason"] = cmd.Reason, ["jti"] = jti },
            ct);

        _log.LogInformation(
            "impersonation.started opsUserId={OpsUserId} tenantId={TenantId} jti={Jti}",
            cmd.OpsUserId, cmd.TargetTenantId, jti);

        return Result.Success(new StartImpersonationResponse(
            ImpersonationToken: token,
            ExpiresIn: 900,
            TenantId: cmd.TargetTenantId,
            RedirectUrl: redirectUrl
        ));
    }
}

// ─── Renew Impersonation Token ────────────────────────────────────────────────

public sealed record RenewImpersonationCommand(
    Guid OpsUserId,
    Guid TargetTenantId,
    string OldJti
) : IRequest<Result<RenewImpersonationResponse>>;

public sealed record RenewImpersonationResponse(string ImpersonationToken, int ExpiresIn);

public sealed class RenewImpersonationCommandHandler
    : IRequestHandler<RenewImpersonationCommand, Result<RenewImpersonationResponse>>
{
    private readonly IJwtService _jwt;
    private readonly IImpersonationSessionService _sessions;
    private readonly IAuditLogService _audit;
    private readonly ILogger<RenewImpersonationCommandHandler> _log;

    public RenewImpersonationCommandHandler(
        IJwtService jwt,
        IImpersonationSessionService sessions,
        IAuditLogService audit,
        ILogger<RenewImpersonationCommandHandler> log)
    {
        _jwt = jwt;
        _sessions = sessions;
        _audit = audit;
        _log = log;
    }

    public async Task<Result<RenewImpersonationResponse>> Handle(
        RenewImpersonationCommand cmd,
        CancellationToken ct)
    {
        var session = await _sessions.GetAsync(cmd.OpsUserId, cmd.TargetTenantId, ct);
        if (session is null)
            return Result.Failure<RenewImpersonationResponse>(
                Error.Unauthorized("Sesión de impersonación no encontrada o expirada."));

        // Verify JTI matches stored value (prevents replay of already-renewed tokens)
        if (session.Value.Jti != cmd.OldJti)
            return Result.Failure<RenewImpersonationResponse>(
                Error.Unauthorized("Token de impersonación inválido."));

        // Issue new token with fresh JTI
        var newJti = Guid.NewGuid().ToString();
        var newToken = _jwt.GenerateImpersonationToken(cmd.OpsUserId, cmd.TargetTenantId, newJti);

        // Renew Redis TTL with new JTI
        await _sessions.SetAsync(cmd.OpsUserId, cmd.TargetTenantId, newJti, session.Value.Reason, ct);

        await _audit.RecordAsync(
            "impersonation_renewed", cmd.OpsUserId, cmd.TargetTenantId.ToString(), cmd.TargetTenantId,
            new Dictionary<string, object?> { ["oldJti"] = cmd.OldJti, ["newJti"] = newJti },
            ct);

        _log.LogInformation(
            "impersonation.renewed opsUserId={OpsUserId} tenantId={TenantId} oldJti={OldJti} newJti={NewJti}",
            cmd.OpsUserId, cmd.TargetTenantId, cmd.OldJti, newJti);

        return Result.Success(
            new RenewImpersonationResponse(newToken, 900));
    }
}

// ─── End Impersonation ────────────────────────────────────────────────────────

public sealed record EndImpersonationCommand(
    Guid OpsUserId,
    Guid TargetTenantId,
    string Jti,
    DateTime TokenExpiry
) : IRequest<Result<Unit>>;

public sealed class EndImpersonationCommandHandler
    : IRequestHandler<EndImpersonationCommand, Result<Unit>>
{
    private readonly IImpersonationSessionService _sessions;
    private readonly IJwtBlacklistService _blacklist;
    private readonly IAuditLogService _audit;
    private readonly ILogger<EndImpersonationCommandHandler> _log;

    public EndImpersonationCommandHandler(
        IImpersonationSessionService sessions,
        IJwtBlacklistService blacklist,
        IAuditLogService audit,
        ILogger<EndImpersonationCommandHandler> log)
    {
        _sessions = sessions;
        _blacklist = blacklist;
        _audit = audit;
        _log = log;
    }

    public async Task<Result<Unit>> Handle(EndImpersonationCommand cmd, CancellationToken ct)
    {
        // DEL is idempotent — no error if key already gone
        await _sessions.DeleteAsync(cmd.OpsUserId, cmd.TargetTenantId, ct);

        // Blacklist the token so it cannot be used again before its natural expiry
        var remaining = cmd.TokenExpiry - DateTime.UtcNow;
        var ttl = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
        await _blacklist.AddAsync(cmd.Jti, ttl, ct);

        await _audit.RecordAsync(
            "impersonation_ended", cmd.OpsUserId, cmd.TargetTenantId.ToString(), cmd.TargetTenantId,
            new Dictionary<string, object?> { ["jti"] = cmd.Jti },
            ct);

        _log.LogInformation(
            "impersonation.ended opsUserId={OpsUserId} tenantId={TenantId} jti={Jti}",
            cmd.OpsUserId, cmd.TargetTenantId, cmd.Jti);

        return Result.Success(Unit.Value);
    }
}
