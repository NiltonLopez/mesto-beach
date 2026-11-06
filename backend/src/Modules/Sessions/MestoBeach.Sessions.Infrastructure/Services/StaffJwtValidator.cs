using MestoBeach.Sessions.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace MestoBeach.Sessions.Infrastructure.Services;

/// <summary>
/// Valida los staff JWT que llegan en el CUERPO de la petición (p. ej. <c>waiterJwt</c> en
/// OpenSession). Comprueba la firma RS256 con la misma clave pública que el middleware —igual que
/// <see cref="SessionTokenService"/> con los tokens de sesión—: antes sólo se parseaba con
/// <c>ReadJwtToken</c> y cualquier JWT fabricado con <c>type=staff</c> pasaba como identidad de mesero.
/// </summary>
public sealed class StaffJwtValidator : IStaffJwtValidator
{
    private readonly TokenValidationParameters _validationParameters;

    public StaffJwtValidator(IConfiguration config)
    {
        var publicPem = config["Jwt:PublicKeyPem"]
            ?? throw new InvalidOperationException("Jwt:PublicKeyPem missing.");
        var pubRsa = RSA.Create();
        pubRsa.ImportFromPem(publicPem);

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(pubRsa),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    }

    public StaffClaims? Validate(string jwt)
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            // Firma + exp; si algo no cuadra, lanza y devolvemos null (token rechazado).
            handler.ValidateToken(jwt, _validationParameters, out var validated);
            var token = (JwtSecurityToken)validated;

            var type = token.Claims.FirstOrDefault(c => c.Type == "type")?.Value;
            if (type != "staff") return null;

            var subStr = token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
                ?? token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var tenantStr = token.Claims.FirstOrDefault(c => c.Type == "tenantId")?.Value;
            var role = token.Claims.FirstOrDefault(c => c.Type == "role")?.Value
                ?? token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (!Guid.TryParse(subStr, out var staffId)
                || !Guid.TryParse(tenantStr, out var tenantId)
                || role is null)
                return null;

            return new StaffClaims(staffId, tenantId, role);
        }
        catch
        {
            return null;
        }
    }
}
