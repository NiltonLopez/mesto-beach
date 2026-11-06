using MestoBeach.Identity.Domain.Enums;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Domain.Entities;

/// <summary>
/// A staff identity: one global account (unique email + a single password) that can belong to
/// several tenants through <see cref="UserMembership"/>. Replaces the old per-tenant StaffMember,
/// so a person uses the same credentials across every restaurant they're part of.
/// </summary>
public sealed class User : AggregateRoot
{
    private User() : base(Guid.Empty) { } // EF Core

    public User(Guid id, string name, string email) : base(id)
    {
        Name = name;
        Email = email.ToLowerInvariant();
        Status = StaffStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? PasswordHash { get; private set; }
    public StaffStatus Status { get; private set; }
    public bool MustChangePassword { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastLogin { get; private set; }

    public bool IsActive => Status == StaffStatus.Active;
    public bool HasCredentials => PasswordHash is not null;

    /// <summary>Sets the initial password and activates the account (from an activation link).</summary>
    public void Activate(string passwordHash, bool mustChange = false)
    {
        PasswordHash = passwordHash;
        Status = StaffStatus.Active;
        MustChangePassword = mustChange;
    }

    /// <summary>Resets the password for an existing account (admin reset or self-service reset).</summary>
    public void SetPassword(string passwordHash, bool mustChange = false)
    {
        PasswordHash = passwordHash;
        MustChangePassword = mustChange;
        if (Status == StaffStatus.Pending) Status = StaffStatus.Active;
    }

    /// <summary>Clears the must-change flag after a forced first-login change.</summary>
    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        MustChangePassword = false;
    }

    public void RecordLogin() => LastLogin = DateTime.UtcNow;
    public void UpdateName(string name) => Name = name;
    public void Deactivate() => Status = StaffStatus.Inactive;
}
