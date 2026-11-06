namespace MestoBeach.Identity.Domain.Enums;

/// <summary>
/// Roles del personal dentro de un restaurante (SDD-001 v2.0).
///
/// Los tres últimos describen cómo trabaja Mesto Beach (PRD-018 §3):
/// <b>Host</b> asigna mesas y mantiene el mapa, <b>PDA</b> comanda y marcha platos en su sector
/// (ADR-026) y <b>Runner</b> reparte los platos listos de su estación (ADR-027).
///
/// <para><b>Se añaden al final a propósito.</b> Hoy el rol se persiste por <i>nombre</i>
/// (<c>user_memberships.roles</c> guarda "Admin,Cajero" y se relee con <c>Enum.Parse</c>), así que
/// el ordinal no viaja a la base ni al JWT; ponerlos al final mantiene además estable cualquier
/// serialización numérica que apareciera más adelante.</para>
///
/// <para><c>Mesero</c> queda como <b>alias heredado de PDA</b> mientras conviven ambas
/// nomenclaturas (OQ-09 de PRD-018).</para>
/// </summary>
public enum StaffRole
{
    Admin,
    Cajero,
    Mesero,
    KDS,
    Host,
    PDA,
    Runner
}

public enum StaffStatus
{
    Pending,
    Active,
    Inactive
}

public enum TenantStatus
{
    Pending,
    Trial,
    Active,
    Suspended,
    Cancelled
}

public enum QrMode
{
    Fixed,
    Dynamic
}

public enum OperationMode
{
    Staffed,
    SelfService
}

public enum TenantPlan
{
    None,
    Commission,
    Subscription,
    Hybrid
}

public enum PlatformUserStatus
{
    Active,
    Inactive
}
