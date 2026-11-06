using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;

namespace MestoBeach.API.Middleware;

/// <summary>
/// Validates JWTs per Mesto Beach conventions:
/// 1. Signature RS256 with service public key
/// 2. exp not past
/// 3. type claim matches expected
/// 4. jti not in Redis blacklist
/// 5. auth_all_invalidated check for Staff / Platform Admin JWTs
/// Also populates ICurrentUser and tenant_id HttpContext item.
/// </summary>
public sealed class JwtValidationMiddleware
{
    private readonly RequestDelegate _next;

    public JwtValidationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Only validate for authenticated endpoints
        var endpoint = ctx.GetEndpoint();
        var allowAnon = endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null;

        var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
        string? rawToken = null;

        if (authHeader?.StartsWith("Bearer ") == true)
            rawToken = authHeader["Bearer ".Length..];
        else if (ctx.Request.Path.StartsWithSegments("/hubs"))
            rawToken = ctx.Request.Query["access_token"].FirstOrDefault();

        // For anonymous endpoints, token validation is optional but still useful when the
        // caller provides a bearer token (for example openSession with staff context).
        if (rawToken is not null)
            await ValidateAndPopulateAsync(ctx, rawToken);

        await _next(ctx);
    }

    private static async Task ValidateAndPopulateAsync(HttpContext ctx, string token)
    {
        var blacklist = ctx.RequestServices.GetRequiredService<IJwtBlacklistService>();
        var globalInvalidation = ctx.RequestServices.GetRequiredService<IGlobalInvalidationService>();
        var currentUser = ctx.RequestServices.GetRequiredService<ICurrentUser>() as CurrentUser;

        try
        {
            // Firma RS256 + exp + clave de emisión: la MISMA validación (y la misma clave, por DI)
            // que aplica el handler JwtBearer a los endpoints [Authorize]. ValidateToken lanza si la
            // firma no cuadra, si el token expiró o si está malformado; el catch de abajo lo deja
            // pasar como anónimo, que es justo lo que necesita el menú público con un token viejo.
            var tvp = ctx.RequestServices.GetRequiredService<TokenValidationParameters>();
            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(token, tvp, out var validatedToken);
            var parsed = (JwtSecurityToken)validatedToken;

            var jti = parsed.Id;
            if (string.IsNullOrWhiteSpace(jti)) return;

            // Check blacklist
            if (await blacklist.IsBlacklistedAsync(jti)) return;

            var claims = parsed.Claims.ToDictionary(c => c.Type, c => c.Value);
            if (!claims.TryGetValue("type", out var type)) return;
            if (!claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var sub)) return;
            if (!Guid.TryParse(sub, out var userId)) return;

            // Check global invalidation for staff and platform admin JWTs
            if (type is "staff" or "platform_admin")
            {
                var iat = parsed.IssuedAt.ToUnixTimeSeconds();
                if (await globalInvalidation.IsInvalidatedAsync(userId, iat)) return;
            }

            if (currentUser is null) return;

            currentUser.UserId = userId;
            currentUser.UserType = type;
            currentUser.Jti = jti;
            currentUser.IsAuthenticated = true;

            if (claims.TryGetValue("role", out var role)) currentUser.Role = role;
            if (claims.TryGetValue("roles", out var rolesJson))
            {
                try
                {
                    currentUser.Roles = System.Text.Json.JsonSerializer.Deserialize<string[]>(rolesJson) ?? [];
                }
                catch { /* ignore */ }
            }

            // Asignaciones de Mesto Beach (SDD-001 v2.0): el sector del PDA y la estación del
            // Runner viajan en el token para que la API pueda autorizar "este runner solo
            // reclama platos de su estación" sin consultar la base en cada petición.
            if (type == "staff")
            {
                if (claims.TryGetValue("floorId", out var floorIdStr) &&
                    Guid.TryParse(floorIdStr, out var floorId))
                {
                    currentUser.AssignedFloorId = floorId;
                    ctx.Items["floor_id"] = floorId;
                }
                if (claims.TryGetValue("stationId", out var staffStationStr) &&
                    Guid.TryParse(staffStationStr, out var staffStationId))
                {
                    currentUser.AssignedStationId = staffStationId;
                    // Mismo Item que ya publica el Device Token: para quien lo lee, la pregunta
                    // es la misma ("¿de qué estación es quien llama?").
                    ctx.Items["station_id"] = staffStationId;
                }

                // SDD-001 v2.1 §7 / ADR-038: la estación vigente se lee de la base en cada
                // petición (caché Redis 60 s) y sustituye a la del token, para que reasignar a un
                // runner no obligue a invalidar su JWT. Si el resolver no puede consultar nada
                // (Unresolved), se conserva la claim del token. Nunca lanza.
                if (claims.TryGetValue("tenantId", out var staffTenantStr) &&
                    Guid.TryParse(staffTenantStr, out var staffTenantId))
                {
                    var stationResolver = ctx.RequestServices.GetRequiredService<IStaffStationResolver>();
                    var resolution = await stationResolver.ResolveAsync(
                        userId, staffTenantId, ctx.RequestAborted);
                    if (resolution.Resolved)
                    {
                        currentUser.AssignedStationId = resolution.StationId;
                        if (resolution.StationId is { } resolvedStation)
                            ctx.Items["station_id"] = resolvedStation;
                        else
                            ctx.Items.Remove("station_id");
                    }
                }
            }

            // Expose tenantId for tenant-scoped endpoints
            if (claims.TryGetValue("tenantId", out var tenantIdStr) &&
                Guid.TryParse(tenantIdStr, out var tenantId))
            {
                ctx.Items["tenant_id"] = tenantId;
            }

            // Expose token expiry for logout
            ctx.Items["token_expiry"] = parsed.ValidTo;

            // Expose setupRequired for TOTP challenge
            if (type == "totp_challenge" &&
                claims.TryGetValue("setupRequired", out var sr) &&
                bool.TryParse(sr, out var setupRequired))
            {
                ctx.Items["totp_setup_required"] = setupRequired;
            }

            // Device token claims
            if (type == "device")
            {
                if (claims.TryGetValue("deviceId", out var deviceId))
                    ctx.Items["device_id"] = deviceId;
                if (claims.TryGetValue("stationId", out var stationIdStr) &&
                    Guid.TryParse(stationIdStr, out var stationId))
                    ctx.Items["station_id"] = stationId;
            }

            // Impersonation claims
            if (type == "impersonation" &&
                claims.TryGetValue("impersonating", out var impersonatingStr) &&
                Guid.TryParse(impersonatingStr, out var impersonatingTenant))
            {
                ctx.Items["impersonating_tenant_id"] = impersonatingTenant;
            }
        }
        catch
        {
            // Malformed token — leave currentUser as unauthenticated
        }
    }
}

public static class DateTimeExtensions
{
    public static long ToUnixTimeSeconds(this DateTime dt) =>
        new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
}
