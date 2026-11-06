using MediatR;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Options;

namespace MestoBeach.Identity.Application.Auth.Platform.Commands;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record ChallengeTokenResult(string ChallengeToken, bool SetupRequired, int ExpiresIn = 300);

public sealed class PlatformAuthOptions
{
    public const string SectionName = "PlatformAuth";
    public bool BypassMfa { get; set; }
}

public record PlatformAdminDto(Guid Id, string Email, string Name, string[] Roles);

public record PlatformAdminAuthResult(string Token, int ExpiresIn, PlatformAdminDto User);

public record PlatformLoginResult(
    string? ChallengeToken,
    bool? SetupRequired,
    string? Token,
    int ExpiresIn,
    PlatformAdminDto? User);

// ─── Platform Login (Phase 1 — issues TOTP challenge) ────────────────────────

public record LoginPlatformCommand(
    string Email,
    string Password) : IRequest<Result<PlatformLoginResult>>;

public sealed class LoginPlatformHandler
    : IRequestHandler<LoginPlatformCommand, Result<PlatformLoginResult>>
{
    private readonly IPlatformUserRepository _repo;
    private readonly IPasswordService _passwords;
    private readonly IJwtService _jwt;
    private readonly PlatformAuthOptions _platformAuth;

    public LoginPlatformHandler(
        IPlatformUserRepository repo,
        IPasswordService passwords,
        IJwtService jwt,
        IOptions<PlatformAuthOptions> platformAuth)
    {
        _repo = repo;
        _passwords = passwords;
        _jwt = jwt;
        _platformAuth = platformAuth.Value;
    }

    public async Task<Result<PlatformLoginResult>> Handle(
        LoginPlatformCommand request,
        CancellationToken ct)
    {
        var user = await _repo.GetByEmailAsync(request.Email.ToLowerInvariant(), ct);

        // Always same error message (anti-enumeration)
        if (user is null || !_passwords.Verify(request.Password, user.PasswordHash))
            return Result.Failure<PlatformLoginResult>(Error.Unauthorized("Credenciales inválidas."));

        if (!user.IsActive)
            return Result.Failure<PlatformLoginResult>(Error.Forbidden("Cuenta desactivada."));

        if (_platformAuth.BypassMfa)
        {
            var roles = await _repo.GetRolesAsync(user.Id, ct);
            user.RecordLogin();
            await _repo.UpdateAsync(user, ct);

            var token = _jwt.GeneratePlatformAdminToken(user.Id, roles, out _);

            return Result.Success(new PlatformLoginResult(
                ChallengeToken: null,
                SetupRequired: null,
                Token: token,
                ExpiresIn: 28800,
                User: new PlatformAdminDto(user.Id, user.Email, user.Name, roles.ToArray())));
        }

        var setupRequired = !user.TotpConfigured;
        var challengeToken = _jwt.GenerateTotpChallengeToken(user.Id, setupRequired, out _);

        return Result.Success(new PlatformLoginResult(
            ChallengeToken: challengeToken,
            SetupRequired: setupRequired,
            Token: null,
            ExpiresIn: 300,
            User: null));
    }
}

// ─── TOTP Verify (Phase 2 — exchanges challenge token for platform JWT) ───────

public record VerifyTotpCommand(
    Guid UserId,
    bool SetupRequired,
    string TotpCode) : IRequest<Result<PlatformAdminAuthResult>>;

public sealed class VerifyTotpHandler
    : IRequestHandler<VerifyTotpCommand, Result<PlatformAdminAuthResult>>
{
    private readonly IPlatformUserRepository _repo;
    private readonly ITotpService _totp;
    private readonly IJwtService _jwt;

    public VerifyTotpHandler(
        IPlatformUserRepository repo,
        ITotpService totp,
        IJwtService jwt)
    {
        _repo = repo;
        _totp = totp;
        _jwt = jwt;
    }

    public async Task<Result<PlatformAdminAuthResult>> Handle(
        VerifyTotpCommand request,
        CancellationToken ct)
    {
        if (request.SetupRequired)
            return Result.Failure<PlatformAdminAuthResult>(
                Error.Validation("SETUP_REQUIRED",
                    "Debe configurar su autenticador TOTP primero. Use POST /platform/totp/setup."));

        var user = await _repo.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure<PlatformAdminAuthResult>(Error.NotFound("Usuario no encontrado."));

        if (user.TotpSecret is null)
            return Result.Failure<PlatformAdminAuthResult>(
                Error.Validation("TOTP_NOT_CONFIGURED", "TOTP no configurado."));

        if (!_totp.ValidateCode(user.TotpSecret, request.TotpCode))
            return Result.Failure<PlatformAdminAuthResult>(
                Error.Unauthorized("Código TOTP incorrecto."));

        var roles = await _repo.GetRolesAsync(user.Id, ct);
        user.RecordLogin();
        await _repo.UpdateAsync(user, ct);

        var token = _jwt.GeneratePlatformAdminToken(user.Id, roles, out _);

        return Result.Success(new PlatformAdminAuthResult(
            token, 28800,
            new PlatformAdminDto(user.Id, user.Email, user.Name, roles.ToArray())));
    }
}

// ─── TOTP Setup ───────────────────────────────────────────────────────────────

public record TotpSetupResult(string OtpauthUrl, string Secret, string EmergencyCode, string Message);

public record SetupTotpCommand(Guid UserId) : IRequest<Result<TotpSetupResult>>;

public sealed class SetupTotpHandler : IRequestHandler<SetupTotpCommand, Result<TotpSetupResult>>
{
    private readonly IPlatformUserRepository _repo;
    private readonly ITotpService _totp;

    public SetupTotpHandler(IPlatformUserRepository repo, ITotpService totp)
    {
        _repo = repo;
        _totp = totp;
    }

    public async Task<Result<TotpSetupResult>> Handle(
        SetupTotpCommand request,
        CancellationToken ct)
    {
        var user = await _repo.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure<TotpSetupResult>(Error.NotFound("Usuario no encontrado."));

        var base32Secret = _totp.GenerateSecret();
        var encryptedSecret = _totp.EncryptSecret(base32Secret);
        var emergencyCode = _totp.GenerateEmergencyCode();
        var emergencyHash = _totp.HashEmergencyCode(emergencyCode);

        user.SetTotpSecret(encryptedSecret, emergencyHash);
        await _repo.UpdateAsync(user, ct);

        var otpauthUrl = _totp.BuildOtpAuthUrl(user.Email, base32Secret);

        return Result.Success(new TotpSetupResult(
            otpauthUrl,
            base32Secret,
            emergencyCode,
            "Guarda el código de emergencia en un lugar seguro. No se mostrará nuevamente."));
    }
}

// ─── TOTP Recover (via emergency code) ───────────────────────────────────────

public record RecoverTotpCommand(
    string Email,
    string EmergencyCode) : IRequest<Result<ChallengeTokenResult>>;

public sealed class RecoverTotpHandler : IRequestHandler<RecoverTotpCommand, Result<ChallengeTokenResult>>
{
    private readonly IPlatformUserRepository _repo;
    private readonly ITotpService _totp;
    private readonly IJwtService _jwt;

    public RecoverTotpHandler(IPlatformUserRepository repo, ITotpService totp, IJwtService jwt)
    {
        _repo = repo;
        _totp = totp;
        _jwt = jwt;
    }

    public async Task<Result<ChallengeTokenResult>> Handle(
        RecoverTotpCommand request,
        CancellationToken ct)
    {
        // All failures return the same message (anti-enumeration)
        const string generic = "Código de emergencia inválido.";

        var user = await _repo.GetByEmailAsync(request.Email.ToLowerInvariant(), ct);
        if (user is null)
            return Result.Failure<ChallengeTokenResult>(Error.Validation("INVALID_CODE", generic));

        if (user.EmergencyCodeHash is null)
            return Result.Failure<ChallengeTokenResult>(Error.Validation("INVALID_CODE", generic));

        if (!_totp.VerifyEmergencyCode(request.EmergencyCode, user.EmergencyCodeHash))
            return Result.Failure<ChallengeTokenResult>(Error.Validation("INVALID_CODE", generic));

        // Single-use: clear TOTP
        user.ClearTotp();
        await _repo.UpdateAsync(user, ct);

        var challengeToken = _jwt.GenerateTotpChallengeToken(user.Id, setupRequired: true, out _);
        return Result.Success(new ChallengeTokenResult(challengeToken, SetupRequired: true));
    }
}
