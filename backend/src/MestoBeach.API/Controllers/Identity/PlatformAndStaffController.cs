using MediatR;
using MestoBeach.Identity.Application.Auth.Platform.Commands;
using MestoBeach.Identity.Application.Staff.Commands;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Identity;

/// <summary>
/// Platform Admin authentication endpoints (TOTP MFA).
/// Base path: /api/v1/identity/platform
/// </summary>
[Route("api/v1/identity/platform")]
public sealed class PlatformAuthController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public PlatformAuthController(ISender mediator, ICurrentUser currentUser)
        : base(mediator)
    {
        _currentUser = currentUser;
    }

    // POST /api/v1/identity/platform/login  (Phase 1)
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PlatformLoginResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(
        [FromBody] PlatformLoginRequest req,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new LoginPlatformCommand(req.Email, req.Password), ct);
        if (!result.IsSuccess)
            return ToResponse(result);

        return result.Value.Token is not null && result.Value.User is not null
            ? Ok(new
            {
                token = result.Value.Token,
                user = result.Value.User,
                expiresIn = result.Value.ExpiresIn
            })
            : Ok(new
            {
                challengeToken = result.Value.ChallengeToken,
                setupRequired = result.Value.SetupRequired,
                expiresIn = result.Value.ExpiresIn
            });
    }

    // POST /api/v1/identity/platform/totp/verify  (Phase 2)
    [HttpPost("totp/verify")]
    [Authorize(Policy = "TotpChallengeJwt")]
    [ProducesResponseType(typeof(PlatformAdminAuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyTotp(
        [FromBody] VerifyTotpRequest req,
        CancellationToken ct)
    {
        var setupRequired = HttpContext.Items["totp_setup_required"] as bool? ?? false;
        var result = await Mediator.Send(
            new VerifyTotpCommand(_currentUser.UserId, setupRequired, req.TotpCode), ct);

        return result.IsSuccess
            ? Ok(new
            {
                token = result.Value.Token,
                tokenType = "Bearer",
                expiresIn = result.Value.ExpiresIn,
                user = result.Value.User
            })
            : ToResponse(result);
    }

    // POST /api/v1/identity/platform/totp/setup
    [HttpPost("totp/setup")]
    [Authorize(Policy = "TotpSetupOrPlatformAdmin")]
    [ProducesResponseType(typeof(TotpSetupResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetupTotp(CancellationToken ct)
    {
        var result = await Mediator.Send(new SetupTotpCommand(_currentUser.UserId), ct);
        return result.IsSuccess
            ? Ok(new
            {
                otpauthUrl = result.Value.OtpauthUrl,
                secret = result.Value.Secret,
                emergencyCode = result.Value.EmergencyCode,
                message = result.Value.Message
            })
            : ToResponse(result);
    }

    // POST /api/v1/identity/platform/totp/recover
    [HttpPost("totp/recover")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ChallengeTokenResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecoverTotp(
        [FromBody] RecoverTotpRequest req,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new RecoverTotpCommand(req.Email, req.EmergencyCode), ct);
        return result.IsSuccess
            ? Ok(new
            {
                challengeToken = result.Value.ChallengeToken,
                setupRequired = result.Value.SetupRequired,
                expiresIn = result.Value.ExpiresIn,
                message = "Código de emergencia validado. Configure su nuevo autenticador TOTP antes de que expire este token."
            })
            : ToResponse(result);
    }
}

/// <summary>
/// Staff management endpoints: invite, activate, resend, password reset.
/// Base path: /api/v1/identity/staff
/// </summary>
[Route("api/v1/identity/staff")]
public sealed class StaffController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public StaffController(ISender mediator, ICurrentUser currentUser)
        : base(mediator)
    {
        _currentUser = currentUser;
    }

    // GET /api/v1/identity/staff
    [HttpGet]
    [Authorize(Policy = "StaffAdmin")]
    [ProducesResponseType(typeof(IReadOnlyList<StaffMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListStaff(CancellationToken ct)
    {
        var tenantId = (Guid)HttpContext.Items["tenant_id"]!;
        var result = await Mediator.Send(new ListStaffQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : ToResponse(result);
    }

    // POST /api/v1/identity/staff/invite
    [HttpPost("invite")]
    [Authorize(Policy = "StaffAdmin")]
    [ProducesResponseType(typeof(StaffInviteResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invite(
        [FromBody] InviteStaffRequest req,
        CancellationToken ct)
    {
        var tenantId = (Guid)HttpContext.Items["tenant_id"]!;
        var accessMode = string.IsNullOrWhiteSpace(req.AccessMode)
            ? StaffAccessMode.ActivationLink
            : req.AccessMode;
        var result = await Mediator.Send(
            new InviteStaffCommand(_currentUser.UserId, tenantId, req.Name, req.Email, req.Roles, accessMode), ct);

        return result.IsSuccess ? Created(string.Empty, result.Value) : ToResponse(result);
    }

    // POST /api/v1/identity/staff/activate
    [HttpPost("activate")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Activate(
        [FromBody] ActivateStaffRequest req,
        CancellationToken ct)
    {
        var result = await Mediator.Send(
            new ActivateStaffCommand(req.Token, req.Password, req.ConfirmPassword), ct);

        return result.IsSuccess
            ? Ok(new { message = "Cuenta activada exitosamente.", staff = result.Value })
            : ToResponse(result);
    }

    // POST /api/v1/identity/staff/{userId}/resend-invitation
    [HttpPost("{userId:guid}/resend-invitation")]
    [Authorize(Policy = "StaffAdmin")]
    [ProducesResponseType(typeof(StaffResendResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResendInvitation(Guid userId, CancellationToken ct)
    {
        var tenantId = (Guid)HttpContext.Items["tenant_id"]!;
        var result = await Mediator.Send(
            new ResendStaffInvitationCommand(_currentUser.UserId, tenantId, userId), ct);

        return result.IsSuccess ? Ok(result.Value) : ToResponse(result);
    }

    // POST /api/v1/identity/staff/{userId}/reset-password
    // Admin generates a password-reset link for an active staff member who lost their password.
    [HttpPost("{userId:guid}/reset-password")]
    [Authorize(Policy = "StaffAdmin")]
    [ProducesResponseType(typeof(StaffPasswordResetLinkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetStaffPassword(Guid userId, CancellationToken ct)
    {
        var tenantId = (Guid)HttpContext.Items["tenant_id"]!;
        var result = await Mediator.Send(
            new AdminResetStaffPasswordCommand(_currentUser.UserId, tenantId, userId), ct);

        return result.IsSuccess ? Ok(result.Value) : ToResponse(result);
    }

    // PATCH /api/v1/identity/staff/{userId}/roles — change the member's roles in this tenant.
    [HttpPatch("{userId:guid}/roles")]
    [Authorize(Policy = "StaffAdmin")]
    [ProducesResponseType(typeof(StaffMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStaffRoles(
        Guid userId, [FromBody] UpdateStaffRolesBody req, CancellationToken ct)
    {
        var tenantId = (Guid)HttpContext.Items["tenant_id"]!;
        var result = await Mediator.Send(
            new UpdateStaffRolesCommand(tenantId, userId, req.Roles), ct);
        return result.IsSuccess ? Ok(result.Value) : ToResponse(result);
    }

    // PATCH /api/v1/identity/staff/{userId}/assignment — sector del PDA y estación del Runner.
    //
    // La asignación es fija por persona y solo la cambia Administración (D-09 de PRD-018), por
    // eso el endpoint es de Admin y no del propio interesado. Cambiarla invalida su token para
    // que el siguiente traiga las claims nuevas.
    [HttpPatch("{userId:guid}/assignment")]
    [Authorize(Policy = "StaffAdmin")]
    [ProducesResponseType(typeof(StaffAssignmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateStaffAssignment(
        Guid userId, [FromBody] UpdateStaffAssignmentBody req, CancellationToken ct)
    {
        var tenantId = (Guid)HttpContext.Items["tenant_id"]!;
        var result = await Mediator.Send(
            new UpdateStaffAssignmentCommand(
                _currentUser.UserId, tenantId, userId,
                req.AssignedFloorId, req.AssignedStationId), ct);

        if (result.IsSuccess) return Ok(result.Value);

        // El SDD pide 422 para "el rol exige asignación y viene vacía". La taxonomía compartida
        // (ErrorType) no llega a 422 —saldría como 400—, así que se eleva aquí por el código,
        // igual que hace DeliveryTripsController con TOO_MANY_ITEMS.
        if (result.Error.Code == StaffAssignmentErrors.AssignmentRequiredCode)
            return UnprocessableEntity(new ProblemDetails
            {
                Title = result.Error.Code,
                Detail = result.Error.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            });

        return ToResponse(result);
    }

    // DELETE /api/v1/identity/staff/{userId} — remove the member from THIS tenant only.
    [HttpDelete("{userId:guid}")]
    [Authorize(Policy = "StaffAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveStaff(Guid userId, CancellationToken ct)
    {
        var tenantId = (Guid)HttpContext.Items["tenant_id"]!;
        var result = await Mediator.Send(new RemoveStaffCommand(tenantId, userId), ct);
        return result.IsSuccess
            ? Ok(new { message = "Acceso removido de este restaurante." })
            : ToResponse(result);
    }

    // POST /api/v1/identity/staff/password-reset/request
    [HttpPost("password-reset/request")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestPasswordReset(
        [FromBody] StaffPasswordResetRequestBody req,
        CancellationToken ct)
    {
        await Mediator.Send(new RequestStaffPasswordResetCommand(req.Email, req.TenantId), ct);
        return Ok(new { message = "Si el correo está registrado, recibirás las instrucciones en breve." });
    }

    // POST /api/v1/identity/staff/password-reset/confirm
    [HttpPost("password-reset/confirm")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmPasswordReset(
        [FromBody] StaffPasswordResetConfirmBody req,
        CancellationToken ct)
    {
        var result = await Mediator.Send(
            new ConfirmStaffPasswordResetCommand(req.Token, req.NewPassword, req.ConfirmPassword), ct);

        return result.IsSuccess
            ? Ok(new { message = "Contraseña actualizada exitosamente." })
            : ToResponse(result);
    }
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────
public record PlatformLoginRequest(string Email, string Password);
public record VerifyTotpRequest(string TotpCode);
public record RecoverTotpRequest(string Email, string EmergencyCode);
public record InviteStaffRequest(string Name, string Email, List<string> Roles, string? AccessMode = null);
public record UpdateStaffRolesBody(List<string> Roles);

/// <summary>
/// Cuerpo de PATCH /staff/{userId}/assignment. Los dos campos viajan siempre: enviar <c>null</c>
/// es la forma de retirar el sector o la estación, así que omitirlos equivale a dejar a la
/// persona sin asignación (y el caso de uso lo rechazará si su rol la exige).
/// </summary>
public record UpdateStaffAssignmentBody(Guid? AssignedFloorId, Guid? AssignedStationId);
public record ActivateStaffRequest(string Token, string Password, string ConfirmPassword);
public record StaffPasswordResetRequestBody(string Email, Guid TenantId);
public record StaffPasswordResetConfirmBody(string Token, string NewPassword, string ConfirmPassword);
