using MediatR;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Application.Auth.Staff.Commands;

// ─── Refresh ──────────────────────────────────────────────────────────────────

public record RefreshStaffTokenResult(string Token, int ExpiresIn = 28800);

public record RefreshStaffTokenCommand(string RefreshToken) : IRequest<Result<RefreshStaffTokenResult>>;

public sealed class RefreshStaffTokenHandler
    : IRequestHandler<RefreshStaffTokenCommand, Result<RefreshStaffTokenResult>>
{
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IUserRepository _users;
    private readonly IUserMembershipRepository _memberships;
    private readonly IJwtService _jwt;

    public RefreshStaffTokenHandler(
        IRefreshTokenService refreshTokens,
        IUserRepository users,
        IUserMembershipRepository memberships,
        IJwtService jwt)
    {
        _refreshTokens = refreshTokens;
        _users = users;
        _memberships = memberships;
        _jwt = jwt;
    }

    public async Task<Result<RefreshStaffTokenResult>> Handle(
        RefreshStaffTokenCommand request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Result.Failure<RefreshStaffTokenResult>(
                Error.Unauthorized("Sesión no encontrada."));

        var session = await _refreshTokens.GetAsync(request.RefreshToken, ct);
        if (session is null)
            return Result.Failure<RefreshStaffTokenResult>(
                Error.Unauthorized("Sesión expirada. Inicie sesión nuevamente."));

        var (userId, tenantId) = session.Value;

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null)
            return Result.Failure<RefreshStaffTokenResult>(
                Error.Unauthorized("Sesión inválida."));

        if (!user.IsActive)
            return Result.Failure<RefreshStaffTokenResult>(
                Error.Forbidden("Cuenta inactiva."));

        var membership = await _memberships.GetByUserAndTenantAsync(userId, tenantId, ct);
        if (membership is null || !membership.IsActive)
            return Result.Failure<RefreshStaffTokenResult>(
                Error.Forbidden("No tiene acceso a este restaurante."));

        var roles = membership.Roles.Select(r => r.ToString()).ToList();
        // La reemisión relee la membresía, así que un cambio de asignación llega al token nuevo.
        var token = _jwt.GenerateStaffToken(
            userId, tenantId, roles,
            membership.AssignedFloorId, membership.AssignedStationId, out _);
        return Result.Success(new RefreshStaffTokenResult(token));
    }
}

// ─── Logout ───────────────────────────────────────────────────────────────────

public record LogoutStaffCommand(
    string Jti,
    DateTime TokenExpiry,
    Guid UserId,
    string? RefreshToken) : IRequest<Result>;

public sealed class LogoutStaffHandler : IRequestHandler<LogoutStaffCommand, Result>
{
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IJwtBlacklistService _blacklist;

    public LogoutStaffHandler(IRefreshTokenService refreshTokens, IJwtBlacklistService blacklist)
    {
        _refreshTokens = refreshTokens;
        _blacklist = blacklist;
    }

    public async Task<Result> Handle(LogoutStaffCommand request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await _refreshTokens.DeleteAsync(request.RefreshToken, ct);
            await _refreshTokens.AddToUserSessionsAsync(
                request.UserId, request.RefreshToken, TimeSpan.Zero, ct); // effectively removes
        }

        var ttl = request.TokenExpiry - DateTime.UtcNow;
        if (ttl > TimeSpan.Zero)
            await _blacklist.AddAsync(request.Jti, ttl, ct);

        return Result.Success();
    }
}

// ─── Logout All ───────────────────────────────────────────────────────────────

public record LogoutAllStaffCommand(
    string Jti,
    DateTime TokenExpiry,
    Guid UserId) : IRequest<Result>;

public sealed class LogoutAllStaffHandler : IRequestHandler<LogoutAllStaffCommand, Result>
{
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IJwtBlacklistService _blacklist;
    private readonly IGlobalInvalidationService _globalInvalidation;

    public LogoutAllStaffHandler(
        IRefreshTokenService refreshTokens,
        IJwtBlacklistService blacklist,
        IGlobalInvalidationService globalInvalidation)
    {
        _refreshTokens = refreshTokens;
        _blacklist = blacklist;
        _globalInvalidation = globalInvalidation;
    }

    public async Task<Result> Handle(LogoutAllStaffCommand request, CancellationToken ct)
    {
        // Delete all refresh tokens for this user
        var sessions = (await _refreshTokens.GetUserSessionsAsync(request.UserId, ct)).ToList();
        foreach (var token in sessions)
            await _refreshTokens.DeleteAsync(token, ct);

        await _refreshTokens.DeleteAllUserSessionsAsync(request.UserId, ct);

        // Set global invalidation timestamp
        await _globalInvalidation.InvalidateAllAsync(request.UserId, ct);

        // Blacklist current JWT
        var ttl = request.TokenExpiry - DateTime.UtcNow;
        if (ttl > TimeSpan.Zero)
            await _blacklist.AddAsync(request.Jti, ttl, ct);

        return Result.Success();
    }
}
