using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;

namespace MestoBeach.API.Middleware;

/// <summary>
/// Validates that the authenticated user has the expected JWT type and role
/// for the requested endpoint, based on authorization policies.
/// </summary>
public static class AuthorizationPolicies
{
    public const string StaffJwt = "StaffJwt";
    public const string StaffAdmin = "StaffAdmin";
    public const string StaffAdminOrCajero = "StaffAdminOrCajero";

    // ── Roles de Mesto Beach (SDD-001 v2.0 · PRD-018 §3 · ADR-026/ADR-027) ───────
    // El ciclo por ítem dejó de ser "cualquiera del personal": marchar es potestad del PDA,
    // dar por listo es de la estación y repartir es del Runner. Cada una tiene su puerta.
    public const string StaffPdaOrAdmin = "StaffPdaOrAdmin";
    /// <summary>Marchar: mete el plato en la cola de su estación. Cocina, PDA o Administración.</summary>
    public const string StaffPdaKdsOrAdmin = "StaffPdaKdsOrAdmin";
    public const string StaffRunnerOrAdmin = "StaffRunnerOrAdmin";
    public const string StaffHostOrAdmin = "StaffHostOrAdmin";
    public const string StaffKdsOrAdmin = "StaffKdsOrAdmin";

    public const string CustomerJwt = "CustomerJwt";
    public const string PlatformAdminJwt = "PlatformAdminJwt";
    public const string TotpChallengeJwt = "TotpChallengeJwt";
    public const string TotpSetupOrPlatformAdmin = "TotpSetupOrPlatformAdmin";
    public const string DeviceJwt = "DeviceJwt";

    /// <summary>
    /// Staff JWT de cualquier rol o KDS Device Token (GET /api/v1/venue, SDD-001 §7). Una sola
    /// política: apilar StaffJwt y DeviceJwt exigiría las dos y respondería 403.
    /// </summary>
    public const string StaffOrDeviceJwt = "StaffOrDeviceJwt";
    public const string ImpersonationJwt = "ImpersonationJwt";

    public static void ConfigurePolicies(AuthorizationOptions opts)
    {
        opts.AddPolicy(StaffJwt, p =>
            p.RequireAssertion(ctx =>
            {
                var user = ctx.Resource as HttpContext is { } hc
                    ? hc.RequestServices.GetService<ICurrentUser>()
                    : null;
                return user?.IsAuthenticated == true && user.UserType == "staff";
            }));

        opts.AddPolicy(StaffAdmin, p =>
            p.RequireAssertion(ctx =>
            {
                var user = GetCurrentUser(ctx);
                return user?.IsAuthenticated == true &&
                       user.UserType == "staff" &&
                       user.HasRole("Admin");
            }));

        opts.AddPolicy(StaffAdminOrCajero, p =>
            p.RequireAssertion(ctx =>
            {
                var user = GetCurrentUser(ctx);
                return user?.IsAuthenticated == true &&
                       user.UserType == "staff" &&
                       (user.HasRole("Admin") || user.HasRole("Cajero"));
            }));

        // ── Puertas por rol del ciclo por ítem ────────────────────────────────
        // Filtran el ACCESO: quien no lleve el rol no llega al handler y se lleva un 403 sin
        // que se toque la base. Las reglas finas —de qué estación es este plato, de quién es
        // este viaje— siguen viviendo en el handler, que es su dueño.

        // Marchar es potestad exclusiva del PDA (ADR-026). `Mesero` entra porque es su alias
        // heredado: el mismo puesto con el nombre viejo mientras conviven ambas nomenclaturas
        // (SDD-001 v2.0; el nombre visible definitivo es OQ-09 de PRD-018).
        opts.AddPolicy(StaffPdaOrAdmin, p =>
            p.RequireAssertion(ctx => EsStaffCon(ctx,
                StaffRoleNames.Pda, StaffRoleNames.Mesero, StaffRoleNames.Admin)));

        // Marchar ya no enciende fuegos: solo mete el plato en la COLA de su estación, así que
        // la cocina entra junto al PDA (el pase adelanta lo que ve venir). La orden de
        // preparación —los fuegos— sigue siendo solo de la estación (StaffKdsOrAdmin).
        opts.AddPolicy(StaffPdaKdsOrAdmin, p =>
            p.RequireAssertion(ctx => EsStaffCon(ctx,
                StaffRoleNames.Pda, StaffRoleNames.Mesero, StaffRoleNames.Kds, StaffRoleNames.Admin)));

        // Armar bandeja, consultar la cola y cerrar o liberar la ruta (ADR-027).
        opts.AddPolicy(StaffRunnerOrAdmin, p =>
            p.RequireAssertion(ctx => EsStaffCon(ctx,
                StaffRoleNames.Runner, StaffRoleNames.Admin)));

        // Asignar mesas con `pax` y mover el mapa en vivo. Todavía sin endpoints propios: el
        // salón sigue colgando de StaffAdminOrCajero hasta que se especifique el flujo del Host.
        opts.AddPolicy(StaffHostOrAdmin, p =>
            p.RequireAssertion(ctx => EsStaffCon(ctx,
                StaffRoleNames.Host, StaffRoleNames.Admin)));

        // La estación da el plato por listo. Solo tokens de personal; el KDS Device Token es
        // otro tipo de token y hoy no pasa por aquí (ver la nota de OrderItemsController).
        opts.AddPolicy(StaffKdsOrAdmin, p =>
            p.RequireAssertion(ctx => EsStaffCon(ctx,
                StaffRoleNames.Kds, StaffRoleNames.Admin)));

        opts.AddPolicy(CustomerJwt, p =>
            p.RequireAssertion(ctx =>
            {
                var user = GetCurrentUser(ctx);
                return user?.IsAuthenticated == true && user.UserType == "customer";
            }));

        opts.AddPolicy(PlatformAdminJwt, p =>
            p.RequireAssertion(ctx =>
            {
                var user = GetCurrentUser(ctx);
                return user?.IsAuthenticated == true && user.UserType == "platform_admin";
            }));

        opts.AddPolicy(TotpChallengeJwt, p =>
            p.RequireAssertion(ctx =>
            {
                var user = GetCurrentUser(ctx);
                return user?.IsAuthenticated == true && user.UserType == "totp_challenge";
            }));

        opts.AddPolicy(TotpSetupOrPlatformAdmin, p =>
            p.RequireAssertion(ctx =>
            {
                var user = GetCurrentUser(ctx);
                if (user?.IsAuthenticated != true) return false;
                if (user.UserType == "platform_admin") return true;
                // TOTP challenge token with setupRequired = true
                if (user.UserType == "totp_challenge")
                {
                    var hc = GetHttpContext(ctx);
                    var setupRequired = hc?.Items["totp_setup_required"] as bool? ?? false;
                    return setupRequired;
                }
                return false;
            }));

        opts.AddPolicy(DeviceJwt, p =>
            p.RequireAssertion(ctx =>
            {
                var user = GetCurrentUser(ctx);
                return user?.IsAuthenticated == true && user.UserType == "device";
            }));

        opts.AddPolicy(StaffOrDeviceJwt, p =>
            p.RequireAssertion(ctx =>
            {
                var user = GetCurrentUser(ctx);
                return user?.IsAuthenticated == true && user.UserType is "staff" or "device";
            }));

        opts.AddPolicy(ImpersonationJwt, p =>
            p.RequireAssertion(ctx =>
            {
                var user = GetCurrentUser(ctx);
                return user?.IsAuthenticated == true && user.UserType == "impersonation";
            }));
    }

    /// <summary>
    /// True si el token es de personal y su membresía trae alguno de los roles indicados.
    /// Mira el rol principal y la lista completa (<see cref="ICurrentUser.HasRole"/>): no
    /// siempre coinciden según cómo se emitiera el JWT.
    /// </summary>
    private static bool EsStaffCon(AuthorizationHandlerContext ctx, params string[] roles)
    {
        var user = GetCurrentUser(ctx);
        return user?.IsAuthenticated == true &&
               user.UserType == "staff" &&
               roles.Any(user.HasRole);
    }

    private static ICurrentUser? GetCurrentUser(AuthorizationHandlerContext ctx)
    {
        var hc = GetHttpContext(ctx);
        return hc?.RequestServices.GetService<ICurrentUser>();
    }

    private static HttpContext? GetHttpContext(AuthorizationHandlerContext ctx)
    {
        return ctx.Resource switch
        {
            HttpContext hc => hc,
            _ => null
        };
    }
}

/// <summary>
/// Nombres de los roles del personal tal y como viajan en el Staff JWT (SDD-001 v2.0).
/// Se comparan como cadenas a propósito: los roles se persisten y se serializan por NOMBRE —
/// nunca por ordinal—, y la capa de API no referencia el enum de Identity.
/// </summary>
public static class StaffRoleNames
{
    public const string Admin  = "Admin";
    public const string Cajero = "Cajero";
    public const string Kds    = "KDS";

    /// <summary>Asigna mesas con <c>pax</c> y mueve el mapa del salón; ve todos los sectores.</summary>
    public const string Host = "Host";

    /// <summary>Comanda por las mesas de su sector y <b>marcha</b> los platos (ADR-026).</summary>
    public const string Pda = "PDA";

    /// <summary>Ve la cola de su estación, arma la bandeja y entrega (ADR-027).</summary>
    public const string Runner = "Runner";

    /// <summary>
    /// Alias heredado de <see cref="Pda"/> mientras conviven ambas nomenclaturas (SDD-001 v2.0;
    /// el nombre visible definitivo es OQ-09 de PRD-018). Quien lo tenga puede lo mismo que el PDA.
    /// </summary>
    public const string Mesero = "Mesero";

    /// <summary>
    /// Traduce el alias una sola vez y en el borde: si el token trae <see cref="Mesero"/>, la
    /// lista que baja al handler trae también <see cref="Pda"/>. Aguas adentro solo existe la
    /// nomenclatura nueva, así que ningún módulo tiene que conocer el nombre viejo — y el día
    /// que el alias muera, se borra aquí y en la política, en ningún sitio más.
    /// </summary>
    public static IReadOnlyList<string> ExpandirAlias(IReadOnlyList<string> roles)
    {
        var tieneMesero = roles.Any(r => string.Equals(r, Mesero, StringComparison.OrdinalIgnoreCase));
        var tienePda = roles.Any(r => string.Equals(r, Pda, StringComparison.OrdinalIgnoreCase));
        if (!tieneMesero || tienePda) return roles;

        return new List<string>(roles) { Pda };
    }
}
