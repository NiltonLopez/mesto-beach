using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Domain.Entities;

/// <summary>
/// Maps ops user roles (soporte, billing, admin). Multiple rows per user.
/// Cached in Redis as ops_roles:{userId} TTL 8h.
/// </summary>
public sealed class PlatformUserRole : Entity
{
    private PlatformUserRole() : base(Guid.Empty) { } // EF Core

    public PlatformUserRole(Guid userId, string role, Guid assignedBy) : base(Guid.NewGuid())
    {
        UserId = userId;
        Role = role;
        AssignedBy = assignedBy;
        AssignedAt = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }

    /// <summary>One of: "soporte" | "billing" | "admin"</summary>
    public string Role { get; private set; } = string.Empty;

    public Guid AssignedBy { get; private set; }
    public DateTime AssignedAt { get; private set; }
}
