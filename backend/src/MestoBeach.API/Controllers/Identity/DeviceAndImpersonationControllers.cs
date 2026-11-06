using MediatR;
using MestoBeach.API.Controllers;
using MestoBeach.API.Middleware;
using MestoBeach.Identity.Application.Devices.Commands;
using MestoBeach.Identity.Application.Auth.Platform.Commands;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Identity;

// ─── Device Tokens Controller ─────────────────────────────────────────────────

/// <summary>KDS device token registration and lifecycle management.</summary>
[Route("api/v1/identity/device-tokens")]
public sealed class DeviceTokensController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public DeviceTokensController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    // POST /api/v1/identity/device-tokens
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceRequest req,
        CancellationToken ct)
    {
        var tenantId = HttpContext.Items["tenant_id"] as Guid?;
        if (tenantId is null) return Unauthorized();

        var cmd = new RegisterDeviceCommand(
            AdminId: _currentUser.UserId,
            TenantId: tenantId.Value,
            DeviceId: req.DeviceId,
            Name: req.Name,
            StationId: req.StationId
        );
        return ToResponse(await Mediator.Send(cmd, ct), successStatus: 201);
    }

    // POST /api/v1/identity/device-tokens/refresh
    [HttpPost("refresh")]
    [Authorize(Policy = AuthorizationPolicies.DeviceJwt)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var tenantId = HttpContext.Items["tenant_id"] as Guid?;
        var deviceId = HttpContext.Items["device_id"] as string;
        var stationId = HttpContext.Items["station_id"] as Guid?;

        if (tenantId is null || string.IsNullOrEmpty(deviceId)) return Unauthorized();

        var cmd = new RefreshDeviceTokenCommand(
            DeviceTokenRecordId: _currentUser.UserId,   // sub = device token record id
            TenantId: tenantId.Value,
            DeviceId: deviceId,
            StationId: stationId
        );
        return ToResponse(await Mediator.Send(cmd, ct));
    }

    // DELETE /api/v1/identity/device-tokens/{tokenId}
    [HttpDelete("{tokenId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> Revoke(Guid tokenId, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["tenant_id"] as Guid?;
        if (tenantId is null) return Unauthorized();

        var cmd = new RevokeDeviceCommand(
            AdminId: _currentUser.UserId,
            AdminTenantId: tenantId.Value,
            DeviceTokenId: tokenId
        );
        return ToResponse(await Mediator.Send(cmd, ct));
    }
}

// ─── Impersonation Controller ─────────────────────────────────────────────────

/// <summary>Ops impersonation token lifecycle: start, renew, end.</summary>
[Route("api/v1/identity/impersonate")]
public sealed class ImpersonationController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public ImpersonationController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    // POST /api/v1/identity/impersonate
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.PlatformAdminJwt)]
    public async Task<IActionResult> Start(
        [FromBody] StartImpersonationRequest req,
        CancellationToken ct)
    {
        var cmd = new StartImpersonationCommand(
            OpsUserId: _currentUser.UserId,
            TargetTenantId: req.TenantId,
            Reason: req.Reason
        );
        return ToResponse(await Mediator.Send(cmd, ct));
    }

    // POST /api/v1/identity/impersonate/renew
    [HttpPost("renew")]
    [Authorize(Policy = AuthorizationPolicies.ImpersonationJwt)]
    public async Task<IActionResult> Renew(CancellationToken ct)
    {
        var impersonatingTenantId = HttpContext.Items["impersonating_tenant_id"] as Guid?;
        if (impersonatingTenantId is null) return Unauthorized();

        var cmd = new RenewImpersonationCommand(
            OpsUserId: _currentUser.UserId,
            TargetTenantId: impersonatingTenantId.Value,
            OldJti: _currentUser.Jti ?? string.Empty
        );
        return ToResponse(await Mediator.Send(cmd, ct));
    }

    // POST /api/v1/identity/impersonate/end
    [HttpPost("end")]
    [Authorize(Policy = AuthorizationPolicies.ImpersonationJwt)]
    public async Task<IActionResult> End(CancellationToken ct)
    {
        var impersonatingTenantId = HttpContext.Items["impersonating_tenant_id"] as Guid?;
        if (impersonatingTenantId is null) return Unauthorized();

        var expiry = HttpContext.Items["token_expiry"] as DateTime? ?? DateTime.UtcNow;
        var cmd = new EndImpersonationCommand(
            OpsUserId: _currentUser.UserId,
            TargetTenantId: impersonatingTenantId.Value,
            Jti: _currentUser.Jti ?? string.Empty,
            TokenExpiry: expiry
        );
        return ToResponse(await Mediator.Send(cmd, ct));
    }
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public sealed record RegisterDeviceRequest(string DeviceId, string Name, Guid? StationId);
public sealed record StartImpersonationRequest(Guid TenantId, string Reason);
