using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MestoBeach.Sessions.Infrastructure.Services;

namespace MestoBeach.Sessions.Tests;

/// <summary>
/// Regresión del bypass de autenticación (revisión de seguridad, agosto 2026): el validador de
/// staff JWT que llega en el cuerpo de la petición (waiterJwt de OpenSession) DEBE comprobar la
/// firma RS256, no sólo parsear el token. Antes usaba <c>ReadJwtToken</c> y cualquier JWT fabricado
/// con <c>type=staff</c> pasaba como identidad de mesero.
///
/// El test es autónomo: genera su propio par de claves, firma con la privada y configura el
/// validador con la pública. No depende de las claves del repo.
/// </summary>
public class StaffJwtValidatorTests
{
    private static readonly Guid StaffId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (StaffJwtValidator validator, RsaSecurityKey trustedKey) BuildValidator()
    {
        var trustedRsa = RSA.Create(2048);
        var publicPem = trustedRsa.ExportSubjectPublicKeyInfoPem();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:PublicKeyPem"] = publicPem })
            .Build();

        return (new StaffJwtValidator(config), new RsaSecurityKey(trustedRsa));
    }

    private static string SignToken(SecurityKey signingKey, string type = "staff", int minutesToExpiry = 30)
    {
        var handler = new JwtSecurityTokenHandler();
        // NotBefore/IssuedAt anclados en el pasado para que el caso "caducado" (exp negativo) siga
        // teniendo exp > nbf y CreateToken no lo rechace al construirlo; el rechazo debe venir de
        // ValidateToken, no de la creación.
        var issuedAt = DateTime.UtcNow.AddHours(-1);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, StaffId.ToString()),
                new Claim("tenantId", TenantId.ToString()),
                new Claim("type", type),
                new Claim("role", "Mesero"),
            ]),
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = DateTime.UtcNow.AddMinutes(minutesToExpiry),
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    [Fact]
    public void Token_firmado_con_la_clave_de_confianza_es_valido()
    {
        var (validator, trustedKey) = BuildValidator();

        var claims = validator.Validate(SignToken(trustedKey));

        claims.Should().NotBeNull();
        claims!.StaffId.Should().Be(StaffId);
        claims.TenantId.Should().Be(TenantId);
        claims.Role.Should().Be("Mesero");
    }

    [Fact]
    public void Token_firmado_con_OTRA_clave_se_rechaza()
    {
        // El atacante fabrica un token con su propia clave. La firma no cuadra con la de confianza.
        var (validator, _) = BuildValidator();
        var attackerKey = new RsaSecurityKey(RSA.Create(2048));

        validator.Validate(SignToken(attackerKey)).Should().BeNull();
    }

    [Fact]
    public void Token_con_la_firma_manipulada_se_rechaza()
    {
        var (validator, trustedKey) = BuildValidator();
        var token = SignToken(trustedKey);

        // Se altera el último carácter de la firma: el payload sigue intacto pero deja de verificar.
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        validator.Validate(tampered).Should().BeNull();
    }

    [Fact]
    public void Token_caducado_se_rechaza()
    {
        var (validator, trustedKey) = BuildValidator();

        validator.Validate(SignToken(trustedKey, minutesToExpiry: -5)).Should().BeNull();
    }

    [Fact]
    public void Token_que_no_es_de_staff_se_rechaza()
    {
        var (validator, trustedKey) = BuildValidator();

        validator.Validate(SignToken(trustedKey, type: "customer")).Should().BeNull();
    }
}
