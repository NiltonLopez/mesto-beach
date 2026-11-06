namespace MestoBeach.Shared.Context;

/// <summary>
/// Represents the currently authenticated user (staff, customer, or platform admin).
/// Populated from the validated JWT by the authentication middleware.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    string UserType { get; } // "staff" | "customer" | "platform_admin" | "totp_challenge"
    string? Role { get; }    // Staff: primary role of the active membership; null for others
    string[] Roles { get; }  // Staff: all membership roles for the active tenant; platform: soporte|billing|admin
    string Jti { get; }
    bool IsAuthenticated { get; }

    /// <summary>
    /// Sector asignado al PDA (claim <c>floorId</c>, SDD-001 v2.0). Null para todos los demás.
    /// Sale del token, no de la base: la asignación es fija y solo la cambia Administración, que
    /// al hacerlo invalida el token vigente para forzar la reemisión.
    /// </summary>
    Guid? AssignedFloorId { get; }

    /// <summary>Estación asignada al Runner (claim <c>stationId</c>, ADR-027). Null para el resto.</summary>
    Guid? AssignedStationId { get; }

    /// <summary>True if the user holds the given role (checks both the primary role and the role list).</summary>
    bool HasRole(string role);
}

public sealed class CurrentUser : ICurrentUser
{
    public Guid UserId { get; set; }
    public string UserType { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string[] Roles { get; set; } = [];
    public string Jti { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public Guid? AssignedFloorId { get; set; }
    public Guid? AssignedStationId { get; set; }

    public bool HasRole(string role) =>
        string.Equals(Role, role, StringComparison.OrdinalIgnoreCase) ||
        Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}
