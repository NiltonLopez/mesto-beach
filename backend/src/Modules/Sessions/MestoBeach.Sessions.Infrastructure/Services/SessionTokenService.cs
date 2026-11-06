using MestoBeach.Sessions.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace MestoBeach.Sessions.Infrastructure.Services;

/// <summary>
/// Generates and validates short-lived RS256 JWTs for session join links
/// (QR dynamic and invite tokens).
/// </summary>
public sealed class SessionTokenService : ISessionTokenService
{
    private readonly RsaSecurityKey _signingKey;
    private readonly RsaSecurityKey _validationKey;

    public SessionTokenService(IConfiguration config)
    {
        var privatePem = config["Jwt:PrivateKeyPem"]
            ?? throw new InvalidOperationException("Jwt:PrivateKeyPem missing.");
        var publicPem = config["Jwt:PublicKeyPem"]
            ?? throw new InvalidOperationException("Jwt:PublicKeyPem missing.");

        var privRsa = RSA.Create();
        privRsa.ImportFromPem(privatePem);
        _signingKey = new RsaSecurityKey(privRsa);

        var pubRsa = RSA.Create();
        pubRsa.ImportFromPem(publicPem);
        _validationKey = new RsaSecurityKey(pubRsa);
    }

    public string GenerateJoinToken(Guid sessionId, Guid tenantId, string type, TimeSpan ttl)
    {
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim("sessionId", sessionId.ToString()),
                new Claim("tenantId", tenantId.ToString()),
                new Claim("type", type)
            ]),
            Expires = DateTime.UtcNow.Add(ttl),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256)
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    public bool ValidateJoinToken(
        string token, string expectedType,
        out Guid sessionId, out Guid tenantId)
    {
        sessionId = Guid.Empty;
        tenantId = Guid.Empty;

        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _validationKey,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }, out _);

            var type = principal.Claims.FirstOrDefault(c => c.Type == "type")?.Value;
            if (type != expectedType) return false;

            var sid = principal.Claims.FirstOrDefault(c => c.Type == "sessionId")?.Value;
            var tid = principal.Claims.FirstOrDefault(c => c.Type == "tenantId")?.Value;

            return Guid.TryParse(sid, out sessionId) && Guid.TryParse(tid, out tenantId);
        }
        catch
        {
            return false;
        }
    }
}
