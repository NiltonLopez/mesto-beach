using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Infrastructure.Configuration;

namespace MestoBeach.Identity.Infrastructure.Services;

public sealed class JwtService : IJwtService
{
    private readonly JwtOptions _opts;
    private readonly RsaSecurityKey _signingKey;
    private readonly RsaSecurityKey _verificationKey;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtService(IOptions<JwtOptions> opts)
    {
        _opts = opts.Value;

        var rsa = RSA.Create();
        rsa.ImportFromPem(_opts.PrivateKeyPem);
        _signingKey = new RsaSecurityKey(rsa);

        var rsaVerify = RSA.Create();
        rsaVerify.ImportFromPem(_opts.PublicKeyPem);
        _verificationKey = new RsaSecurityKey(rsaVerify);
    }

    public string GenerateStaffToken(
        Guid userId,
        Guid tenantId,
        IReadOnlyList<string> roles,
        Guid? assignedFloorId,
        Guid? assignedStationId,
        out string jti)
    {
        jti = Guid.NewGuid().ToString();
        // Primary role kept for backward compatibility; full set in the "roles" array claim.
        var primary = roles.Count > 0 ? roles[0] : "";
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("tenantId", tenantId.ToString()),
            new("role", primary),
            new("roles", System.Text.Json.JsonSerializer.Serialize(roles)),
            new("type", "staff"),
            new(JwtRegisteredClaimNames.Jti, jti)
        };

        // Asignaciones de Mesto Beach (SDD-001 v2.0). Solo van si la membresía las tiene: quien no
        // es PDA ni Runner no lleva claim, y el consumidor puede leer su ausencia como "sin
        // asignación" en vez de tener que distinguir un Guid vacío.
        if (assignedFloorId.HasValue)
            claims.Add(new Claim("floorId", assignedFloorId.Value.ToString()));
        if (assignedStationId.HasValue)
            claims.Add(new Claim("stationId", assignedStationId.Value.ToString()));

        return CreateToken(claims, TimeSpan.FromHours(8));
    }

    public string GenerateCustomerToken(Guid customerId, out string jti)
    {
        jti = Guid.NewGuid().ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, customerId.ToString()),
            new("type", "customer"),
            new(JwtRegisteredClaimNames.Jti, jti)
        };
        return CreateToken(claims, TimeSpan.FromHours(4));
    }

    public string GenerateTotpChallengeToken(Guid userId, bool setupRequired, out string jti)
    {
        jti = Guid.NewGuid().ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("type", "totp_challenge"),
            new("setupRequired", setupRequired.ToString().ToLowerInvariant()),
            new(JwtRegisteredClaimNames.Jti, jti)
        };
        return CreateToken(claims, TimeSpan.FromMinutes(5));
    }

    public string GeneratePlatformAdminToken(Guid userId, IEnumerable<string> roles, out string jti)
    {
        jti = Guid.NewGuid().ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("type", "platform_admin"),
            new("roles", JsonSerializer.Serialize(roles.ToArray())),
            new(JwtRegisteredClaimNames.Jti, jti)
        };
        return CreateToken(claims, TimeSpan.FromHours(8));
    }

    public string GenerateDeviceToken(
        Guid deviceTokenRecordId,
        Guid tenantId,
        string deviceId,
        Guid? stationId,
        out string jti)
    {
        jti = Guid.NewGuid().ToString();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, deviceTokenRecordId.ToString()),
            new("tenantId", tenantId.ToString()),
            new("deviceId", deviceId),
            new("type", "device"),
            new(JwtRegisteredClaimNames.Jti, jti)
        };
        if (stationId.HasValue)
            claims.Add(new Claim("stationId", stationId.Value.ToString()));

        return CreateToken(claims, TimeSpan.FromDays(30));
    }

    public string GenerateImpersonationToken(Guid opsUserId, Guid targetTenantId, string jti)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, opsUserId.ToString()),
            new("role", "platform_admin"),
            new("impersonating", targetTenantId.ToString()),
            new("type", "impersonation"),
            new(JwtRegisteredClaimNames.Jti, jti)
        };
        return CreateToken(claims, TimeSpan.FromMinutes(15));
    }

    public IDictionary<string, string> ValidateToken(string token, string expectedType)
    {
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _verificationKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var principal = _handler.ValidateToken(token, validationParams, out _);

        var claims = principal.Claims.ToDictionary(c => c.Type, c => c.Value);

        if (claims.TryGetValue("type", out var type) && type != expectedType)
            throw new SecurityTokenException($"Invalid token type: expected {expectedType}, got {type}.");

        return claims;
    }

    public DateTime GetTokenExpiry(string token)
    {
        var parsed = _handler.ReadJwtToken(token);
        return parsed.ValidTo;
    }

    public string GetJti(string token)
    {
        var parsed = _handler.ReadJwtToken(token);
        return parsed.Id;
    }

    private string CreateToken(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            Expires = now.Add(lifetime),
            SigningCredentials = new SigningCredentials(
                _signingKey,
                SecurityAlgorithms.RsaSha256)
        };
        var token = _handler.CreateToken(descriptor);
        return _handler.WriteToken(token);
    }
}
