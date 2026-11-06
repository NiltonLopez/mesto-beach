using MediatR;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Entities;
using MestoBeach.Identity.Domain.Enums;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Application.Staff.Commands;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record StaffMemberDto(
    Guid UserId,
    string Name,
    string Email,
    IReadOnlyList<string> Roles,
    string Status,
    Guid TenantId,
    DateTime CreatedAt,
    // Asignaciones de Mesto Beach (SDD-001 v2.0). La pantalla de personal las lista junto al rol:
    // un PDA sin sector o un Runner sin estación no puede trabajar y hay que verlo de un vistazo.
    Guid? AssignedFloorId = null,
    Guid? AssignedStationId = null);

/// <summary>
/// How a staff member gains access to a tenant:
/// - ACTIVATION_LINK: a brand-new account stays Pending; the member sets their own password via a 48h link.
/// - DIRECT_ACCESS: a brand-new account gets a temporary password and must change it on first login.
/// - EXISTING_USER: the email already had an account, so we just granted access reusing their password.
/// </summary>
public static class StaffAccessMode
{
    public const string ActivationLink = "ACTIVATION_LINK";
    public const string DirectAccess = "DIRECT_ACCESS";
    public const string ExistingUser = "EXISTING_USER";

    public static bool IsValid(string value) =>
        value is ActivationLink or DirectAccess;
}

public record StaffInviteResponseDto(
    Guid UserId,
    string Name,
    string Email,
    IReadOnlyList<string> Roles,
    string Status,
    Guid TenantId,
    DateTime CreatedAt,
    string AccessMode,
    string? ActivationUrl = null,
    string? TemporaryPassword = null);

public record StaffResendResponseDto(
    Guid UserId,
    string ActivationUrl,
    DateTime ExpiresAt);

internal static class StaffMapping
{
    public static List<StaffRole> ParseRoles(IEnumerable<string> roles, out string? error)
    {
        error = null;
        var parsed = new List<StaffRole>();
        foreach (var r in roles ?? [])
        {
            if (!Enum.TryParse<StaffRole>(r, true, out var role))
            {
                error = $"Rol no válido: {r}.";
                return parsed;
            }
            parsed.Add(role);
        }
        if (parsed.Count == 0) error = "Debe asignar al menos un rol.";
        return parsed;
    }

    /// <summary>Combined status shown to the tenant admin: pending until the user activates, else the membership status.</summary>
    public static string CombinedStatus(User user, UserMembership membership) =>
        user.Status == StaffStatus.Pending ? "PENDING" : membership.Status.ToString().ToUpperInvariant();
}

// ─── Invite (create-or-link) ───────────────────────────────────────────────────

public record InviteStaffCommand(
    Guid AdminId,
    Guid TenantId,
    string Name,
    string Email,
    IReadOnlyList<string> Roles,
    string AccessMode) : IRequest<Result<StaffInviteResponseDto>>;

public sealed class InviteStaffHandler : IRequestHandler<InviteStaffCommand, Result<StaffInviteResponseDto>>
{
    private readonly IUserRepository _users;
    private readonly IUserMembershipRepository _memberships;
    private readonly IActivationTokenService _activation;
    private readonly IEmailNotificationService _email;
    private readonly IFrontendUrlService _urls;
    private readonly IPasswordService _passwords;
    private readonly IPasswordGenerator _passwordGenerator;
    private readonly MestoBeach.Shared.Plans.IPlanEntitlementsReader _entitlements;

    public InviteStaffHandler(
        IUserRepository users,
        IUserMembershipRepository memberships,
        IActivationTokenService activation,
        IEmailNotificationService email,
        IFrontendUrlService urls,
        IPasswordService passwords,
        IPasswordGenerator passwordGenerator,
        MestoBeach.Shared.Plans.IPlanEntitlementsReader entitlements)
    {
        _users = users;
        _memberships = memberships;
        _activation = activation;
        _email = email;
        _urls = urls;
        _passwords = passwords;
        _passwordGenerator = passwordGenerator;
        _entitlements = entitlements;
    }

    /// <summary>Bloquea (403 plan_limit_exceeded) si añadir staff excede el plan del tenant.</summary>
    private async Task<Error?> StaffLimitError(Guid tenantId, CancellationToken ct)
    {
        var ent = await _entitlements.GetAsync(tenantId, ct);
        if (ent is null || ent.MaxStaff is not int maxStaff) return null;
        var count = (await _memberships.GetByTenantAsync(tenantId, ct)).Count;
        return ent.ExceedsStaff(count)
            ? MestoBeach.Shared.Plans.PlanLimits.Exceeded("staff", maxStaff, count, ent.PlanCode)
            : null;
    }

    public async Task<Result<StaffInviteResponseDto>> Handle(InviteStaffCommand request, CancellationToken ct)
    {
        var roles = StaffMapping.ParseRoles(request.Roles, out var roleError);
        if (roleError is not null)
            return Result.Failure<StaffInviteResponseDto>(Error.Validation("INVALID_ROLE", roleError));

        if (!StaffAccessMode.IsValid(request.AccessMode))
            return Result.Failure<StaffInviteResponseDto>(
                Error.Validation("INVALID_ACCESS_MODE", "Modo de acceso no válido."));

        // La invitación no lleva asignación, así que no puede crear un PDA/Runner operativo: lo
        // haría con sector/estación en null, el mismo estado incoherente que los endpoints de rol y
        // de asignación prohíben. Sin esta guarda se colaba por aquí un Runner sin estación cuyo
        // token no lleva `stationId`, y aguas abajo la cola del pase se abría a cualquier estación.
        // El flujo correcto es invitar con un rol base, asignar sector/estación y luego promover.
        if (roles.Contains(StaffRole.PDA))
            return Result.Failure<StaffInviteResponseDto>(Error.Validation(
                "ASSIGNMENT_REQUIRED",
                "Un PDA necesita un sector asignado. Invita con un rol base, asígnale el sector y luego dale el rol PDA."));
        if (roles.Contains(StaffRole.Runner))
            return Result.Failure<StaffInviteResponseDto>(Error.Validation(
                "ASSIGNMENT_REQUIRED",
                "Un Runner necesita una estación asignada. Invita con un rol base, asígnale la estación y luego dale el rol Runner."));

        var emailNorm = request.Email.ToLowerInvariant();
        var existingUser = await _users.GetByEmailAsync(emailNorm, ct);

        // ── Existing account → just grant access to this tenant, reusing their password. ──
        if (existingUser is not null)
        {
            var already = await _memberships.GetByUserAndTenantAsync(existingUser.Id, request.TenantId, ct);
            if (already is not null)
                return Result.Failure<StaffInviteResponseDto>(
                    Error.Conflict("Este usuario ya tiene acceso a este restaurante."));

            if (await StaffLimitError(request.TenantId, ct) is { } limit)
                return Result.Failure<StaffInviteResponseDto>(limit);

            var newMembership = new UserMembership(Guid.NewGuid(), existingUser.Id, request.TenantId, roles);
            await _memberships.AddAsync(newMembership, ct);

            return Result.Success(new StaffInviteResponseDto(
                existingUser.Id, existingUser.Name, existingUser.Email,
                roles.Select(r => r.ToString()).ToList(),
                StaffMapping.CombinedStatus(existingUser, newMembership),
                request.TenantId, newMembership.CreatedAt,
                StaffAccessMode.ExistingUser));
        }

        // ── New account → create the user + membership. ──
        if (await StaffLimitError(request.TenantId, ct) is { } staffLimit)
            return Result.Failure<StaffInviteResponseDto>(staffLimit);

        var user = new User(Guid.NewGuid(), request.Name, emailNorm);

        if (request.AccessMode == StaffAccessMode.DirectAccess)
        {
            var tempPassword = _passwordGenerator.GenerateTemporary();
            user.Activate(_passwords.Hash(tempPassword), mustChange: true);
            await _users.AddAsync(user, ct);
            var membership = new UserMembership(Guid.NewGuid(), user.Id, request.TenantId, roles);
            await _memberships.AddAsync(membership, ct);

            return Result.Success(new StaffInviteResponseDto(
                user.Id, user.Name, user.Email, roles.Select(r => r.ToString()).ToList(),
                StaffMapping.CombinedStatus(user, membership),
                request.TenantId, membership.CreatedAt,
                StaffAccessMode.DirectAccess, TemporaryPassword: tempPassword));
        }

        // Activation link: user stays Pending until they set their own password.
        await _users.AddAsync(user, ct);
        var pendingMembership = new UserMembership(Guid.NewGuid(), user.Id, request.TenantId, roles);
        await _memberships.AddAsync(pendingMembership, ct);

        var token = _activation.Generate();
        await _activation.StoreAsync(token, user.Id, request.TenantId, ct);
        await _activation.StoreUserIndexAsync(user.Id, token, ct);

        var activationUrl = _urls.StaffActivationUrl(token);
        _ = _email.SendStaffActivationEmailAsync(user.Email, user.Name, activationUrl, ct);

        return Result.Success(new StaffInviteResponseDto(
            user.Id, user.Name, user.Email, roles.Select(r => r.ToString()).ToList(),
            StaffMapping.CombinedStatus(user, pendingMembership),
            request.TenantId, pendingMembership.CreatedAt,
            StaffAccessMode.ActivationLink, ActivationUrl: activationUrl));
    }
}

// ─── Activate (global account) ─────────────────────────────────────────────────

public record ActivateStaffCommand(
    string Token,
    string Password,
    string ConfirmPassword) : IRequest<Result<StaffMemberDto>>;

public sealed class ActivateStaffHandler : IRequestHandler<ActivateStaffCommand, Result<StaffMemberDto>>
{
    private readonly IUserRepository _users;
    private readonly IActivationTokenService _activation;
    private readonly IPasswordService _passwords;

    public ActivateStaffHandler(
        IUserRepository users,
        IActivationTokenService activation,
        IPasswordService passwords)
    {
        _users = users;
        _activation = activation;
        _passwords = passwords;
    }

    public async Task<Result<StaffMemberDto>> Handle(ActivateStaffCommand request, CancellationToken ct)
    {
        if (request.Password != request.ConfirmPassword)
            return Result.Failure<StaffMemberDto>(
                Error.Validation("PASSWORD_MISMATCH", "Las contraseñas no coinciden."));

        var session = await _activation.GetAsync(request.Token, ct);
        if (session is null)
            return Result.Failure<StaffMemberDto>(
                Error.Validation("INVALID_TOKEN", "El enlace de activación es inválido o ha expirado."));

        var (userId, tenantId) = session.Value;
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || user.Status != StaffStatus.Pending)
            return Result.Failure<StaffMemberDto>(
                Error.Validation("ALREADY_ACTIVE",
                    "Esta cuenta ya ha sido activada o está en un estado no activable."));

        user.Activate(_passwords.Hash(request.Password));
        await _users.UpdateAsync(user, ct);

        await _activation.DeleteAsync(request.Token, ct);
        await _activation.DeleteUserIndexAsync(userId, ct);

        return Result.Success(new StaffMemberDto(
            user.Id, user.Name, user.Email, [], user.Status.ToString().ToUpperInvariant(),
            tenantId, user.CreatedAt));
    }
}

// ─── Resend Invitation ────────────────────────────────────────────────────────

public record ResendStaffInvitationCommand(
    Guid AdminId,
    Guid AdminTenantId,
    Guid TargetUserId) : IRequest<Result<StaffResendResponseDto>>;

public sealed class ResendStaffInvitationHandler
    : IRequestHandler<ResendStaffInvitationCommand, Result<StaffResendResponseDto>>
{
    private readonly IUserRepository _users;
    private readonly IUserMembershipRepository _memberships;
    private readonly IActivationTokenService _activation;
    private readonly IEmailNotificationService _email;
    private readonly IFrontendUrlService _urls;

    public ResendStaffInvitationHandler(
        IUserRepository users,
        IUserMembershipRepository memberships,
        IActivationTokenService activation,
        IEmailNotificationService email,
        IFrontendUrlService urls)
    {
        _users = users;
        _memberships = memberships;
        _activation = activation;
        _email = email;
        _urls = urls;
    }

    public async Task<Result<StaffResendResponseDto>> Handle(
        ResendStaffInvitationCommand request, CancellationToken ct)
    {
        var membership = await _memberships.GetByUserAndTenantAsync(request.TargetUserId, request.AdminTenantId, ct);
        if (membership is null)
            return Result.Failure<StaffResendResponseDto>(
                Error.Forbidden("No tiene permisos para gestionar este usuario."));

        var user = await _users.GetByIdAsync(request.TargetUserId, ct);
        if (user is null)
            return Result.Failure<StaffResendResponseDto>(Error.NotFound("Usuario no encontrado."));

        if (user.Status != StaffStatus.Pending)
            return Result.Failure<StaffResendResponseDto>(
                Error.Validation("ALREADY_ACTIVE", "El usuario ya ha activado su cuenta."));

        var oldToken = await _activation.GetTokenByUserAsync(user.Id, ct);
        if (oldToken is not null)
            await _activation.DeleteAsync(oldToken, ct);

        var newToken = _activation.Generate();
        await _activation.StoreAsync(newToken, user.Id, request.AdminTenantId, ct);
        await _activation.StoreUserIndexAsync(user.Id, newToken, ct);

        var activationUrl = _urls.StaffActivationUrl(newToken);
        _ = _email.SendStaffActivationEmailAsync(user.Email, user.Name, activationUrl, ct);

        return Result.Success(new StaffResendResponseDto(
            user.Id, activationUrl, DateTime.UtcNow.AddHours(48)));
    }
}

// ─── Admin-initiated Password Reset (generate link) ───────────────────────────

public record StaffPasswordResetLinkDto(
    Guid UserId,
    string Email,
    string ResetUrl,
    DateTime ExpiresAt);

public record AdminResetStaffPasswordCommand(
    Guid AdminId,
    Guid AdminTenantId,
    Guid TargetUserId) : IRequest<Result<StaffPasswordResetLinkDto>>;

public sealed class AdminResetStaffPasswordHandler
    : IRequestHandler<AdminResetStaffPasswordCommand, Result<StaffPasswordResetLinkDto>>
{
    private static readonly TimeSpan ResetTtl = TimeSpan.FromHours(1);

    private readonly IUserRepository _users;
    private readonly IUserMembershipRepository _memberships;
    private readonly IPasswordResetTokenService _tokens;
    private readonly IEmailNotificationService _email;
    private readonly IFrontendUrlService _urls;

    public AdminResetStaffPasswordHandler(
        IUserRepository users,
        IUserMembershipRepository memberships,
        IPasswordResetTokenService tokens,
        IEmailNotificationService email,
        IFrontendUrlService urls)
    {
        _users = users;
        _memberships = memberships;
        _tokens = tokens;
        _email = email;
        _urls = urls;
    }

    public async Task<Result<StaffPasswordResetLinkDto>> Handle(
        AdminResetStaffPasswordCommand request, CancellationToken ct)
    {
        var membership = await _memberships.GetByUserAndTenantAsync(request.TargetUserId, request.AdminTenantId, ct);
        if (membership is null)
            return Result.Failure<StaffPasswordResetLinkDto>(
                Error.Forbidden("No tiene permisos para gestionar este usuario."));

        var user = await _users.GetByIdAsync(request.TargetUserId, ct);
        if (user is null)
            return Result.Failure<StaffPasswordResetLinkDto>(Error.NotFound("Usuario no encontrado."));

        if (user.Status == StaffStatus.Pending)
            return Result.Failure<StaffPasswordResetLinkDto>(
                Error.Validation("NOT_ACTIVATED",
                    "La cuenta aún no ha sido activada. Use 'Reenviar enlace' de invitación."));

        var token = _tokens.Generate();
        await _tokens.StoreStaffAsync(token, user.Id, ct);

        var resetUrl = _urls.StaffPasswordResetUrl(token);
        _ = _email.SendStaffPasswordResetEmailAsync(user.Email, user.Name, resetUrl, ct);

        return Result.Success(new StaffPasswordResetLinkDto(
            user.Id, user.Email, resetUrl, DateTime.UtcNow.Add(ResetTtl)));
    }
}

// ─── Update membership roles ────────────────────────────────────────────────────

public record UpdateStaffRolesCommand(
    Guid AdminTenantId,
    Guid TargetUserId,
    IReadOnlyList<string> Roles) : IRequest<Result<StaffMemberDto>>;

public sealed class UpdateStaffRolesHandler
    : IRequestHandler<UpdateStaffRolesCommand, Result<StaffMemberDto>>
{
    private readonly IUserRepository _users;
    private readonly IUserMembershipRepository _memberships;
    private readonly IGlobalInvalidationService _invalidation;
    private readonly IStaffStationResolver _stationCache;

    public UpdateStaffRolesHandler(
        IUserRepository users,
        IUserMembershipRepository memberships,
        IGlobalInvalidationService invalidation,
        IStaffStationResolver stationCache)
    {
        _users = users;
        _memberships = memberships;
        _invalidation = invalidation;
        _stationCache = stationCache;
    }

    public async Task<Result<StaffMemberDto>> Handle(UpdateStaffRolesCommand request, CancellationToken ct)
    {
        var roles = StaffMapping.ParseRoles(request.Roles, out var roleError);
        if (roleError is not null)
            return Result.Failure<StaffMemberDto>(Error.Validation("INVALID_ROLE", roleError));

        var membership = await _memberships.GetByUserAndTenantAsync(request.TargetUserId, request.AdminTenantId, ct);
        if (membership is null)
            return Result.Failure<StaffMemberDto>(Error.NotFound("Usuario no encontrado en este restaurante."));

        // Un PDA sin sector y un Runner sin estación son estados que el sistema no sabe operar:
        // el PDA no tendría mesas que atender y el runner no vería ninguna cola. El endpoint de
        // asignación ya lo impide; sin esta comprobación, se colaría por la puerta de los roles.
        if (roles.Contains(StaffRole.PDA) && membership.AssignedFloorId is null)
            return Result.Failure<StaffMemberDto>(Error.Validation(
                "ASSIGNMENT_REQUIRED",
                "Un PDA necesita un sector asignado. Asígnaselo antes de darle el rol."));

        if (roles.Contains(StaffRole.Runner) && membership.AssignedStationId is null)
            return Result.Failure<StaffMemberDto>(Error.Validation(
                "ASSIGNMENT_REQUIRED",
                "Un Runner necesita una estación asignada. Asígnasela antes de darle el rol."));

        membership.SetRoles(roles);
        await _memberships.UpdateAsync(membership, ct);

        // Cambiar los roles cambia lo que el token autoriza: el vigente se invalida para forzar
        // reemisión, igual que hace el endpoint de asignación. Sin esto, alguien a quien acaban de
        // retirar un rol seguiría usándolo hasta que su token caducara.
        await _invalidation.InvalidateAllAsync(request.TargetUserId, ct);
        // SDD-001 v2.1: SetRoles deja la estación a null al quitar el rol Runner.
        await _stationCache.InvalidateAsync(request.TargetUserId, request.AdminTenantId, ct);

        var user = await _users.GetByIdAsync(request.TargetUserId, ct);
        return Result.Success(new StaffMemberDto(
            request.TargetUserId, user?.Name ?? "", user?.Email ?? "",
            roles.Select(r => r.ToString()).ToList(),
            user is null ? "" : StaffMapping.CombinedStatus(user, membership),
            request.AdminTenantId, membership.CreatedAt,
            membership.AssignedFloorId, membership.AssignedStationId));
    }
}

// ─── Remove staff from a tenant (membership only) ───────────────────────────────

public record RemoveStaffCommand(
    Guid AdminTenantId,
    Guid TargetUserId) : IRequest<Result>;

public sealed class RemoveStaffHandler : IRequestHandler<RemoveStaffCommand, Result>
{
    private readonly IUserMembershipRepository _memberships;
    private readonly IStaffStationResolver _stationCache;

    public RemoveStaffHandler(IUserMembershipRepository memberships, IStaffStationResolver stationCache)
    {
        _memberships = memberships;
        _stationCache = stationCache;
    }

    public async Task<Result> Handle(RemoveStaffCommand request, CancellationToken ct)
    {
        var membership = await _memberships.GetByUserAndTenantAsync(request.TargetUserId, request.AdminTenantId, ct);
        if (membership is null)
            return Result.Failure(Error.NotFound("Usuario no encontrado en este restaurante."));

        // Removes access to THIS tenant only; the global account and other memberships stay intact.
        await _memberships.RemoveAsync(membership, ct);
        // SDD-001 v2.1: tras el commit, sin membresía no hay estación.
        await _stationCache.InvalidateAsync(request.TargetUserId, request.AdminTenantId, ct);
        return Result.Success();
    }
}

// ─── List Staff (tenant memberships) ───────────────────────────────────────────

public record ListStaffQuery(Guid TenantId) : IRequest<Result<IReadOnlyList<StaffMemberDto>>>;

public sealed class ListStaffHandler
    : IRequestHandler<ListStaffQuery, Result<IReadOnlyList<StaffMemberDto>>>
{
    private readonly IUserRepository _users;
    private readonly IUserMembershipRepository _memberships;

    public ListStaffHandler(IUserRepository users, IUserMembershipRepository memberships)
    {
        _users = users;
        _memberships = memberships;
    }

    public async Task<Result<IReadOnlyList<StaffMemberDto>>> Handle(
        ListStaffQuery request, CancellationToken ct)
    {
        var memberships = await _memberships.GetByTenantAsync(request.TenantId, ct);
        var dtos = new List<StaffMemberDto>();
        foreach (var m in memberships)
        {
            var user = await _users.GetByIdAsync(m.UserId, ct);
            if (user is null) continue;
            dtos.Add(new StaffMemberDto(
                user.Id, user.Name, user.Email,
                m.Roles.Select(r => r.ToString()).ToList(),
                StaffMapping.CombinedStatus(user, m),
                m.TenantId, m.CreatedAt,
                m.AssignedFloorId, m.AssignedStationId));
        }
        return Result.Success<IReadOnlyList<StaffMemberDto>>(dtos);
    }
}

// ─── Self-service Password Reset (global account) ──────────────────────────────

public record RequestStaffPasswordResetCommand(string Email, Guid TenantId) : IRequest<Result>;

public sealed class RequestStaffPasswordResetHandler
    : IRequestHandler<RequestStaffPasswordResetCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IPasswordResetTokenService _tokens;
    private readonly IEmailNotificationService _email;
    private readonly IFrontendUrlService _urls;

    public RequestStaffPasswordResetHandler(
        IUserRepository users,
        IPasswordResetTokenService tokens,
        IEmailNotificationService email,
        IFrontendUrlService urls)
    {
        _users = users;
        _tokens = tokens;
        _email = email;
        _urls = urls;
    }

    public async Task<Result> Handle(RequestStaffPasswordResetCommand request, CancellationToken ct)
    {
        var email = request.Email.ToLowerInvariant();
        var user = await _users.GetByEmailAsync(email, ct);

        if (user is not null && user.IsActive)
        {
            var token = _tokens.Generate();
            await _tokens.StoreStaffAsync(token, user.Id, ct);
            _ = _email.SendStaffPasswordResetEmailAsync(
                user.Email, user.Name, _urls.StaffPasswordResetUrl(token), ct);
        }

        return Result.Success(); // Always 200 (no account enumeration)
    }
}

public record ConfirmStaffPasswordResetCommand(
    string Token,
    string NewPassword,
    string ConfirmPassword) : IRequest<Result>;

public sealed class ConfirmStaffPasswordResetHandler
    : IRequestHandler<ConfirmStaffPasswordResetCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IPasswordResetTokenService _tokens;
    private readonly IPasswordService _passwords;

    public ConfirmStaffPasswordResetHandler(
        IUserRepository users,
        IPasswordResetTokenService tokens,
        IPasswordService passwords)
    {
        _users = users;
        _tokens = tokens;
        _passwords = passwords;
    }

    public async Task<Result> Handle(ConfirmStaffPasswordResetCommand request, CancellationToken ct)
    {
        if (request.NewPassword != request.ConfirmPassword)
            return Result.Failure(Error.Validation("PASSWORD_MISMATCH", "Las contraseñas no coinciden."));

        var userId = await _tokens.GetStaffIdAsync(request.Token, ct);
        if (userId is null)
            return Result.Failure(
                Error.Validation("INVALID_TOKEN", "El enlace de recuperación es inválido o ha expirado."));

        var user = await _users.GetByIdAsync(userId.Value, ct);
        if (user is null)
            return Result.Failure(
                Error.Validation("INVALID_TOKEN", "El enlace de recuperación es inválido o ha expirado."));

        user.ChangePassword(_passwords.Hash(request.NewPassword));
        await _users.UpdateAsync(user, ct);
        await _tokens.DeleteStaffAsync(request.Token, ct);

        return Result.Success();
    }
}

// ─── Change Password (authenticated, forced first-login) ──────────────────────

public record ChangeStaffPasswordCommand(
    Guid StaffId,
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword) : IRequest<Result>;

public sealed class ChangeStaffPasswordHandler
    : IRequestHandler<ChangeStaffPasswordCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IPasswordService _passwords;

    public ChangeStaffPasswordHandler(IUserRepository users, IPasswordService passwords)
    {
        _users = users;
        _passwords = passwords;
    }

    public async Task<Result> Handle(ChangeStaffPasswordCommand request, CancellationToken ct)
    {
        if (request.NewPassword != request.ConfirmPassword)
            return Result.Failure(Error.Validation("PASSWORD_MISMATCH", "Las contraseñas no coinciden."));

        var user = await _users.GetByIdAsync(request.StaffId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure(Error.Unauthorized("Cuenta no válida."));

        if (!_passwords.Verify(request.CurrentPassword, user.PasswordHash!))
            return Result.Failure(
                Error.Validation("INVALID_CURRENT_PASSWORD", "La contraseña actual es incorrecta."));

        if (_passwords.Verify(request.NewPassword, user.PasswordHash!))
            return Result.Failure(
                Error.Validation("SAME_PASSWORD", "La nueva contraseña debe ser distinta de la actual."));

        user.ChangePassword(_passwords.Hash(request.NewPassword));
        await _users.UpdateAsync(user, ct);

        return Result.Success();
    }
}
