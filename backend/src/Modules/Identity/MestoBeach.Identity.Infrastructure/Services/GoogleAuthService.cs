using Google.Apis.Auth;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace MestoBeach.Identity.Infrastructure.Services;

public sealed class GoogleAuthService : IGoogleAuthService
{
    private readonly GoogleAuthOptions _opts;

    public GoogleAuthService(IOptions<GoogleAuthOptions> opts) => _opts = opts.Value;

    public async Task<(string Email, string Name, string GoogleId)> ValidateAsync(
        string idToken, CancellationToken ct = default)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_opts.ClientId]
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            return (payload.Email, payload.Name, payload.Subject);
        }
        catch (InvalidJwtException ex)
        {
            throw new UnauthorizedAccessException("Token de Google inválido.", ex);
        }
    }
}
