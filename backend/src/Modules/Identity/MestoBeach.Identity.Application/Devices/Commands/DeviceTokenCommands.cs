using FluentValidation;
using MediatR;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Entities;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Identity.Application.Devices.Commands;

// ─── Register Device ──────────────────────────────────────────────────────────

public sealed record RegisterDeviceCommand(
    Guid AdminId,
    Guid TenantId,
    string DeviceId,
    string Name,
    Guid? StationId
) : IRequest<Result<RegisterDeviceResponse>>;

public sealed record RegisterDeviceResponse(
    Guid Id,
    string DeviceId,
    string Name,
    Guid TenantId,
    Guid? StationId,
    string Token,
    string TokenType,
    int ExpiresIn,
    DateTime CreatedAt
);

public sealed class RegisterDeviceCommandValidator : AbstractValidator<RegisterDeviceCommand>
{
    public RegisterDeviceCommandValidator()
    {
        RuleFor(x => x.DeviceId)
            .NotEmpty()
            .Length(3, 100)
            .Matches(@"^[a-zA-Z0-9_-]{3,100}$")
            .WithMessage("deviceId solo puede contener letras, números, guiones y underscores (3-100 caracteres).");

        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(2, 100)
            .WithMessage("name debe tener entre 2 y 100 caracteres.");
    }
}

public sealed class RegisterDeviceCommandHandler
    : IRequestHandler<RegisterDeviceCommand, Result<RegisterDeviceResponse>>
{
    private readonly IDeviceTokenRepository _devices;
    private readonly IJwtService _jwt;
    private readonly IDeviceSessionService _sessions;
    private readonly ILogger<RegisterDeviceCommandHandler> _log;

    public RegisterDeviceCommandHandler(
        IDeviceTokenRepository devices,
        IJwtService jwt,
        IDeviceSessionService sessions,
        ILogger<RegisterDeviceCommandHandler> log)
    {
        _devices = devices;
        _jwt = jwt;
        _sessions = sessions;
        _log = log;
    }

    public async Task<Result<RegisterDeviceResponse>> Handle(
        RegisterDeviceCommand cmd,
        CancellationToken ct)
    {
        // Uniqueness check: active device with same deviceId in this tenant
        var existing = await _devices.GetActiveByDeviceIdAndTenantAsync(cmd.DeviceId, cmd.TenantId, ct);
        if (existing is not null)
            return Result.Failure<RegisterDeviceResponse>(
                Error.Conflict("Ya existe un dispositivo activo con este ID en el restaurante."));

        // Create entity
        var id = Guid.NewGuid();
        var device = new DeviceToken(id, cmd.TenantId, cmd.AdminId, cmd.DeviceId, cmd.Name, cmd.StationId);
        await _devices.AddAsync(device, ct);

        // Issue JWT
        var token = _jwt.GenerateDeviceToken(id, cmd.TenantId, cmd.DeviceId, cmd.StationId, out _);

        // Write Redis rolling-TTL key
        await _sessions.SetAsync(cmd.TenantId, cmd.DeviceId, cmd.StationId, ct);

        _log.LogInformation(
            "device.register.success adminId={AdminId} deviceTokenId={Id} deviceId={DeviceId} tenantId={TenantId} stationId={StationId}",
            cmd.AdminId, id, cmd.DeviceId, cmd.TenantId, cmd.StationId);

        return Result.Success(new RegisterDeviceResponse(
            Id: id,
            DeviceId: cmd.DeviceId,
            Name: cmd.Name,
            TenantId: cmd.TenantId,
            StationId: cmd.StationId,
            Token: token,
            TokenType: "Bearer",
            ExpiresIn: 60 * 60 * 24 * 30,   // 30d in seconds
            CreatedAt: device.CreatedAt
        ));
    }
}

// ─── Refresh Device Token ─────────────────────────────────────────────────────

public sealed record RefreshDeviceTokenCommand(
    Guid DeviceTokenRecordId,
    Guid TenantId,
    string DeviceId,
    Guid? StationId
) : IRequest<Result<RefreshDeviceTokenResponse>>;

public sealed record RefreshDeviceTokenResponse(string Token, string TokenType, int ExpiresIn);

public sealed class RefreshDeviceTokenCommandHandler
    : IRequestHandler<RefreshDeviceTokenCommand, Result<RefreshDeviceTokenResponse>>
{
    private readonly IDeviceTokenRepository _devices;
    private readonly IJwtService _jwt;
    private readonly IDeviceSessionService _sessions;
    private readonly ILogger<RefreshDeviceTokenCommandHandler> _log;

    public RefreshDeviceTokenCommandHandler(
        IDeviceTokenRepository devices,
        IJwtService jwt,
        IDeviceSessionService sessions,
        ILogger<RefreshDeviceTokenCommandHandler> log)
    {
        _devices = devices;
        _jwt = jwt;
        _sessions = sessions;
        _log = log;
    }

    public async Task<Result<RefreshDeviceTokenResponse>> Handle(
        RefreshDeviceTokenCommand cmd,
        CancellationToken ct)
    {
        // Load device record
        var device = await _devices.GetByIdAsync(cmd.DeviceTokenRecordId, ct);
        if (device is null)
            return Result.Failure<RefreshDeviceTokenResponse>(
                Error.Unauthorized("Dispositivo no encontrado."));

        if (device.IsRevoked)
            return Result.Failure<RefreshDeviceTokenResponse>(
                Error.Unauthorized("Dispositivo revocado."));

        // Check Redis TTL (authoritative for session liveness)
        var redisAlive = await _sessions.ExistsAsync(cmd.TenantId, cmd.DeviceId, ct);
        if (!redisAlive)
            return Result.Failure<RefreshDeviceTokenResponse>(
                Error.Unauthorized("Sesión de dispositivo expirada. Registre el dispositivo nuevamente."));

        // Renew Redis TTL + update last_seen
        await _sessions.SetAsync(cmd.TenantId, cmd.DeviceId, cmd.StationId, ct);
        device.RecordSeen();
        await _devices.UpdateAsync(device, ct);

        var newToken = _jwt.GenerateDeviceToken(
            cmd.DeviceTokenRecordId, cmd.TenantId, cmd.DeviceId, cmd.StationId, out _);

        _log.LogInformation(
            "device.token_refresh.success deviceTokenId={Id} deviceId={DeviceId} tenantId={TenantId}",
            cmd.DeviceTokenRecordId, cmd.DeviceId, cmd.TenantId);

        return Result.Success(
            new RefreshDeviceTokenResponse(newToken, "Bearer", 60 * 60 * 24 * 30));
    }
}

// ─── Revoke Device ────────────────────────────────────────────────────────────

public sealed record RevokeDeviceCommand(
    Guid AdminId,
    Guid AdminTenantId,
    Guid DeviceTokenId
) : IRequest<Result<Unit>>;

public sealed class RevokeDeviceCommandHandler
    : IRequestHandler<RevokeDeviceCommand, Result<Unit>>
{
    private readonly IDeviceTokenRepository _devices;
    private readonly IDeviceSessionService _sessions;
    private readonly IAuditLogService _audit;
    private readonly ILogger<RevokeDeviceCommandHandler> _log;

    public RevokeDeviceCommandHandler(
        IDeviceTokenRepository devices,
        IDeviceSessionService sessions,
        IAuditLogService audit,
        ILogger<RevokeDeviceCommandHandler> log)
    {
        _devices = devices;
        _sessions = sessions;
        _audit = audit;
        _log = log;
    }

    public async Task<Result<Unit>> Handle(RevokeDeviceCommand cmd, CancellationToken ct)
    {
        var device = await _devices.GetByIdAsync(cmd.DeviceTokenId, ct);
        if (device is null)
            return Result.Failure<Unit>(
                Error.NotFound("Dispositivo no encontrado."));

        // Tenant boundary check
        if (device.TenantId != cmd.AdminTenantId)
            return Result.Failure<Unit>(
                Error.Forbidden("No tiene permisos para gestionar este dispositivo."));

        if (device.IsRevoked)
            return Result.Failure<Unit>(
                Error.Validation("Device.AlreadyRevoked", "El dispositivo ya está revocado."));

        device.Revoke();
        await _devices.UpdateAsync(device, ct);

        // Invalidate Redis session
        await _sessions.DeleteAsync(device.TenantId, device.DeviceId, ct);

        await _audit.RecordAsync(
            "device_revoked", cmd.AdminId, cmd.DeviceTokenId.ToString(), cmd.AdminTenantId,
            new Dictionary<string, object?> { ["deviceId"] = device.DeviceId, ["name"] = device.DeviceName },
            ct);

        _log.LogInformation(
            "device.revoke.success adminId={AdminId} tokenId={TokenId} deviceId={DeviceId} tenantId={TenantId}",
            cmd.AdminId, cmd.DeviceTokenId, device.DeviceId, cmd.AdminTenantId);

        return Result.Success(Unit.Value);
    }
}
