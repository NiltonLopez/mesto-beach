using MediatR;
using MestoBeach.Identity.Application.Auth.Staff.Commands;
using MestoBeach.Identity.Application.Auth.Staff.Queries;
using MestoBeach.Identity.Application.Staff.Commands;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Identity;

/// <summary>
/// Staff authentication endpoints: login, refresh, logout, logout-all, profile.
/// Base path: /api/v1/identity/auth
/// </summary>
[Route("api/v1/identity/auth")]
public sealed class AuthController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public AuthController(ISender mediator, ICurrentUser currentUser)
        : base(mediator)
    {
        _currentUser = currentUser;
    }

    // POST /api/v1/identity/auth/login/resolve
    // Discovery step: returns the tenants where (email, password) is valid, so the UI can
    // skip asking for a tenant id. No token is issued here.
    [HttpPost("login/resolve")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ResolveStaffTenantsResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked)]
    public async Task<IActionResult> ResolveTenants(
        [FromBody] ResolveTenantsRequest req, CancellationToken ct) =>
        ToResponse(await Mediator.Send(
            new ResolveStaffTenantsCommand(req.Email, req.Password), ct));

    // POST /api/v1/identity/auth/login
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginStaffResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status423Locked)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest req,
        CancellationToken ct)
    {
        var result = await Mediator.Send(
            new LoginStaffCommand(req.Email, req.Password, req.TenantId), ct);

        if (result.IsFailure) return ToResponse(result);

        // Set refresh token cookie if present (Mesero/Admin roles)
        if (result.Value.RefreshToken is not null)
        {
            Response.Cookies.Append("refresh_token", result.Value.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/v1/identity/auth",
                MaxAge = TimeSpan.FromDays(7)
            });
        }

        return Ok(new
        {
            token = result.Value.Token,
            tokenType = "Bearer",
            expiresIn = result.Value.ExpiresIn,
            staff = result.Value.Staff
        });
    }

    // POST /api/v1/identity/auth/refresh
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshToken = Request.Cookies["refresh_token"];
        var result = await Mediator.Send(new RefreshStaffTokenCommand(refreshToken ?? string.Empty), ct);

        return result.IsSuccess
            ? Ok(new { token = result.Value.Token, tokenType = "Bearer", expiresIn = result.Value.ExpiresIn })
            : ToResponse(result);
    }

    // POST /api/v1/identity/auth/logout
    [HttpPost("logout")]
    [Authorize(Policy = "StaffJwt")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var refreshToken = Request.Cookies["refresh_token"];
        var expiry = HttpContext.Items["token_expiry"] as DateTime? ?? DateTime.UtcNow.AddHours(8);

        await Mediator.Send(new LogoutStaffCommand(
            _currentUser.Jti,
            expiry,
            _currentUser.UserId,
            refreshToken), ct);

        Response.Cookies.Delete("refresh_token", new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
            Path = "/api/v1/identity/auth"
        });

        return NoContent();
    }

    // POST /api/v1/identity/auth/logout-all
    [HttpPost("logout-all")]
    [Authorize(Policy = "StaffJwt")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        var expiry = HttpContext.Items["token_expiry"] as DateTime? ?? DateTime.UtcNow.AddHours(8);

        await Mediator.Send(new LogoutAllStaffCommand(
            _currentUser.Jti,
            expiry,
            _currentUser.UserId), ct);

        Response.Cookies.Delete("refresh_token", new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
            Path = "/api/v1/identity/auth"
        });

        return NoContent();
    }

    // GET /api/v1/identity/auth/me
    [HttpGet("me")]
    [Authorize(Policy = "StaffJwt")]
    [ProducesResponseType(typeof(StaffProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var tenantId = (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;
        var result = await Mediator.Send(new GetStaffProfileQuery(_currentUser.UserId, tenantId), ct);
        return ToResponse(result);
    }

    // POST /api/v1/identity/auth/change-password
    // Used both for the forced first-login change (temporary password) and for a
    // voluntary password change by an authenticated staff member.
    [HttpPost("change-password")]
    [Authorize(Policy = "StaffJwt")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest req,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ChangeStaffPasswordCommand(
            _currentUser.UserId, req.CurrentPassword, req.NewPassword, req.ConfirmPassword), ct);

        return result.IsSuccess ? NoContent() : ToResponse(result);
    }
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public record LoginRequest(string Email, string Password, Guid TenantId);
public record ResolveTenantsRequest(string Email, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);
