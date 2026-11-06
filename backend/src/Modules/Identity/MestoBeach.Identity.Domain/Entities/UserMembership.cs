using MestoBeach.Identity.Domain.Enums;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Domain.Entities;

/// <summary>
/// Links a <see cref="User"/> to a tenant with one or more roles (an admin may grant several
/// roles to the same person, e.g. Cajero + Mesero). Deactivating a membership only removes access
/// to that tenant — the user keeps their other memberships and their global account.
/// </summary>
public sealed class UserMembership : AggregateRoot
{
    private UserMembership() : base(Guid.Empty) { } // EF Core

    public UserMembership(Guid id, Guid userId, Guid tenantId, IEnumerable<StaffRole> roles) : base(id)
    {
        UserId = userId;
        TenantId = tenantId;
        Roles = Normalize(roles);
        Status = StaffStatus.Active;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public List<StaffRole> Roles { get; private set; } = [];
    public StaffStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Sector al que atiende el PDA (<c>floors.id</c>, propiedad de Sessions). Obligatorio cuando
    /// los roles incluyen <see cref="StaffRole.PDA"/>. Fijo por persona: solo lo cambia
    /// Administración (D-09 de PRD-018), no es un turno que se elija al entrar.
    /// </summary>
    public Guid? AssignedFloorId { get; private set; }

    /// <summary>
    /// Estación a la que está adscrito el Runner (<c>kds_stations.id</c>, propiedad de Orders).
    /// Obligatoria cuando los roles incluyen <see cref="StaffRole.Runner"/> (ADR-027).
    /// </summary>
    public Guid? AssignedStationId { get; private set; }

    public bool IsActive => Status == StaffStatus.Active;
    public bool IsAdmin => Roles.Contains(StaffRole.Admin);

    /// <summary>El rol PDA no es operativo sin sector: sin él no sabe por qué mesas comanda.</summary>
    public bool RequiresFloor => Roles.Contains(StaffRole.PDA);

    /// <summary>El rol Runner no es operativo sin estación: de ella salen los platos que reparte.</summary>
    public bool RequiresStation => Roles.Contains(StaffRole.Runner);

    /// <summary>
    /// Cambia los roles de la membresía. Si el nuevo conjunto ya no necesita una asignación, esta
    /// se descarta: dejarla puesta emitiría un <c>floorId</c>/<c>stationId</c> en el JWT de alguien
    /// que ya no es PDA ni Runner. Quitar un rol lo decide Administración, igual que la asignación.
    /// </summary>
    public void SetRoles(IEnumerable<StaffRole> roles)
    {
        Roles = Normalize(roles);
        if (!RequiresFloor) AssignedFloorId = null;
        if (!RequiresStation) AssignedStationId = null;
    }

    /// <summary>
    /// Fija el sector del PDA y la estación del Runner. Las reglas de obligatoriedad y de
    /// existencia las valida el caso de uso, que es quien puede consultar Sessions y Orders.
    /// </summary>
    public void SetAssignment(Guid? assignedFloorId, Guid? assignedStationId)
    {
        AssignedFloorId = assignedFloorId;
        AssignedStationId = assignedStationId;
    }

    public void Deactivate() => Status = StaffStatus.Inactive;
    public void Reactivate() => Status = StaffStatus.Active;

    private static List<StaffRole> Normalize(IEnumerable<StaffRole> roles)
    {
        var list = (roles ?? []).Distinct().ToList();
        if (list.Count == 0)
            throw new InvalidOperationException("Una membresía debe tener al menos un rol.");
        return list;
    }
}
