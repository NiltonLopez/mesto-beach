using MediatR;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Enums;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Application.Auth.Staff.Commands;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record StaffDto(
    Guid Id,
    string Name,
    string Email,
    IReadOnlyList<string> Roles,
    Guid TenantId,
    string TenantName,
    bool MustChangePassword,
    string TenantStatus,   // "Active" | "Trial" | "Pending" | "Suspended" — the admin is alerted when not active
    // Asignaciones de Mesto Beach (SDD-001 v2.0): sector del PDA y estación del Runner. Viajan
    // también en el JWT; aquí las ve el frontend sin tener que abrir el token.
    Guid? AssignedFloorId = null,
    Guid? AssignedStationId = null);

public record LoginStaffResult(
    string Token,
    int ExpiresIn,
    StaffDto Staff,
    string? RefreshToken); // null for Cajero

// ─── Command ──────────────────────────────────────────────────────────────────

public record LoginStaffCommand(
    string Email,
    string Password,
    Guid TenantId) : IRequest<Result<LoginStaffResult>>;

// ─── Handler ──────────────────────────────────────────────────────────────────

public sealed class LoginStaffHandler : IRequestHandler<LoginStaffCommand, Result<LoginStaffResult>>
{
    private readonly IUserRepository _users;
    private readonly IUserMembershipRepository _memberships;
    private readonly ITenantRepository _tenantRepo;
    private readonly IPasswordService _passwords;
    private readonly IJwtService _jwt;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly ILoginAttemptService _loginAttempts;

    public LoginStaffHandler(
        IUserRepository users,
        IUserMembershipRepository memberships,
        ITenantRepository tenantRepo,
        IPasswordService passwords,
        IJwtService jwt,
        IRefreshTokenService refreshTokens,
        ILoginAttemptService loginAttempts)
    {
        _users = users;
        _memberships = memberships;
        _tenantRepo = tenantRepo;
        _passwords = passwords;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _loginAttempts = loginAttempts;
    }

    public async Task<Result<LoginStaffResult>> Handle(
        LoginStaffCommand request, CancellationToken ct)
    {
        // 1. Verify tenant exists
        var tenant = await _tenantRepo.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result.Failure<LoginStaffResult>(Error.NotFound("Restaurante no encontrado."));

        var email = request.Email.ToLowerInvariant();

        // 2. Lockout before touching credentials
        var attempts = await _loginAttempts.GetCountAsync(email, request.TenantId, ct);
        if (attempts >= 5)
            return Result.Failure<LoginStaffResult>(
                Error.Locked("Cuenta bloqueada temporalmente. Intente de nuevo en 15 minutos."));

        // 3. Global identity + credentials
        var user = await _users.GetByEmailAsync(email, ct);
        if (user is null || !user.HasCredentials)
            return Result.Failure<LoginStaffResult>(Error.Unauthorized("Credenciales inválidas."));

        if (user.Status == StaffStatus.Pending)
            return Result.Failure<LoginStaffResult>(
                Error.Forbidden("Cuenta no activada. Revise su correo electrónico."));
        if (user.Status == StaffStatus.Inactive)
            return Result.Failure<LoginStaffResult>(
                Error.Forbidden("Cuenta desactivada. Contacte al administrador."));

        if (!_passwords.Verify(request.Password, user.PasswordHash!))
        {
            await _loginAttempts.IncrementAsync(email, request.TenantId, ct);
            return Result.Failure<LoginStaffResult>(Error.Unauthorized("Credenciales inválidas."));
        }

        // 4. Membership in this tenant
        var membership = await _memberships.GetByUserAndTenantAsync(user.Id, request.TenantId, ct);
        if (membership is null || !membership.IsActive)
            return Result.Failure<LoginStaffResult>(
                Error.Forbidden("No tiene acceso a este restaurante."));

        // Pending tenants are only accessible to their Admin during onboarding.
        if (!tenant.IsActive &&
            !(tenant.Status == TenantStatus.Pending && membership.IsAdmin))
            return Result.Failure<LoginStaffResult>(Error.NotFound("Restaurante no encontrado."));

        // 5. KDS-only memberships use the Device Token flow, not password login.
        if (membership.Roles.All(r => r == StaffRole.KDS))
            return Result.Failure<LoginStaffResult>(
                Error.Forbidden("Los dispositivos KDS deben autenticarse mediante el flujo de Device Token."));

        // 6. Success
        await _loginAttempts.ClearAsync(email, request.TenantId, ct);
        user.RecordLogin();
        await _users.UpdateAsync(user, ct);

        var roles = membership.Roles.Select(r => r.ToString()).ToList();
        var token = _jwt.GenerateStaffToken(
            user.Id, request.TenantId, roles,
            membership.AssignedFloorId, membership.AssignedStationId, out _);

        // 7. Refresh token for Mesero/Admin only — never while a password change is pending.
        //    PDA entra en la lista por ser el nombre nuevo del mismo puesto que Mesero
        //    (SDD-001 v2.0): trabaja el turno entero con un comandero en la mano y perder la
        //    sesión a las 8h sería la misma avería que se evitó para el mesero.
        string? refreshToken = null;
        bool refreshEligible = membership.Roles.Any(
            r => r is StaffRole.Mesero or StaffRole.PDA or StaffRole.Admin);
        if (!user.MustChangePassword && refreshEligible)
        {
            refreshToken = _refreshTokens.Generate();
            await _refreshTokens.StoreAsync(refreshToken, user.Id, request.TenantId, TimeSpan.FromDays(7), ct);
            await _refreshTokens.AddToUserSessionsAsync(user.Id, refreshToken, TimeSpan.FromDays(7), ct);
        }

        var staffDto = new StaffDto(
            user.Id, user.Name, user.Email, roles, request.TenantId, tenant.Name, user.MustChangePassword,
            tenant.Status.ToString(), membership.AssignedFloorId, membership.AssignedStationId);
        return Result.Success(new LoginStaffResult(token, 28800, staffDto, refreshToken));
    }
}
