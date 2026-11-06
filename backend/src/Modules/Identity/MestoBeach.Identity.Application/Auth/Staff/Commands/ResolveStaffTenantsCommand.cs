using MediatR;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Enums;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Application.Auth.Staff.Commands;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record StaffTenantOption(Guid TenantId, string TenantName, IReadOnlyList<string> Roles);

public record ResolveStaffTenantsResult(IReadOnlyList<StaffTenantOption> Tenants);

// ─── Command ──────────────────────────────────────────────────────────────────

/// <summary>
/// Discovery step for staff login: authenticate the global account once (email + password) and
/// return every active tenant the user belongs to, so the UI can auto-enter when there's a single
/// restaurant and show a picker when there are several. Issues NO token.
/// </summary>
public record ResolveStaffTenantsCommand(string Email, string Password)
    : IRequest<Result<ResolveStaffTenantsResult>>;

// ─── Handler ──────────────────────────────────────────────────────────────────

public sealed class ResolveStaffTenantsHandler
    : IRequestHandler<ResolveStaffTenantsCommand, Result<ResolveStaffTenantsResult>>
{
    private static readonly Guid GlobalBucket = Guid.Empty;
    private const int MaxAttempts = 10;

    private readonly IUserRepository _users;
    private readonly IUserMembershipRepository _memberships;
    private readonly ITenantRepository _tenantRepo;
    private readonly IPasswordService _passwords;
    private readonly ILoginAttemptService _loginAttempts;

    public ResolveStaffTenantsHandler(
        IUserRepository users,
        IUserMembershipRepository memberships,
        ITenantRepository tenantRepo,
        IPasswordService passwords,
        ILoginAttemptService loginAttempts)
    {
        _users = users;
        _memberships = memberships;
        _tenantRepo = tenantRepo;
        _passwords = passwords;
        _loginAttempts = loginAttempts;
    }

    public async Task<Result<ResolveStaffTenantsResult>> Handle(
        ResolveStaffTenantsCommand request, CancellationToken ct)
    {
        var email = request.Email.ToLowerInvariant();

        var attempts = await _loginAttempts.GetCountAsync(email, GlobalBucket, ct);
        if (attempts >= MaxAttempts)
            return Result.Failure<ResolveStaffTenantsResult>(
                Error.Locked("Demasiados intentos. Intente de nuevo en 15 minutos."));

        var user = await _users.GetByEmailAsync(email, ct);
        bool credentialsOk = user is not null
            && user.HasCredentials
            && user.Status == StaffStatus.Active
            && _passwords.Verify(request.Password, user.PasswordHash!);

        if (!credentialsOk)
        {
            await _loginAttempts.IncrementAsync(email, GlobalBucket, ct);
            return Result.Failure<ResolveStaffTenantsResult>(Error.Unauthorized("Credenciales inválidas."));
        }

        var memberships = await _memberships.GetByUserAsync(user!.Id, ct);
        var options = new List<StaffTenantOption>();
        bool blockedByPending = false;  // had an active membership, but the restaurant isn't live yet
        foreach (var m in memberships)
        {
            if (!m.IsActive) continue;
            if (m.Roles.All(r => r == StaffRole.KDS)) continue; // KDS-only → device flow

            var tenant = await _tenantRepo.GetByIdAsync(m.TenantId, ct);
            if (tenant is null) continue;
            var accessible = tenant.IsActive
                || (tenant.Status == TenantStatus.Pending && m.IsAdmin);
            if (!accessible)
            {
                // A non-admin whose restaurant is still in onboarding (Pending) → clearer hint below.
                if (tenant.Status == TenantStatus.Pending) blockedByPending = true;
                continue;
            }

            options.Add(new StaffTenantOption(
                tenant.Id, tenant.Name, m.Roles.Select(r => r.ToString()).ToList()));
        }

        if (options.Count == 0)
            return Result.Failure<ResolveStaffTenantsResult>(
                Error.Forbidden(blockedByPending
                    ? "El restaurante aún no está activo. Pídele al administrador que lo active para poder ingresar."
                    : "No tiene acceso a ningún restaurante activo."));

        await _loginAttempts.ClearAsync(email, GlobalBucket, ct);
        return Result.Success(new ResolveStaffTenantsResult(
            options.OrderBy(o => o.TenantName).ToList()));
    }
}
