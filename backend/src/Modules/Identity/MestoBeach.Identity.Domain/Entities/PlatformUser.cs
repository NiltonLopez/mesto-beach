using MestoBeach.Identity.Domain.Enums;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Domain.Entities;

/// <summary>
/// A Mesto Beach platform operations user (Ops team). Authentication requires MFA (TOTP).
/// </summary>
public sealed class PlatformUser : AggregateRoot
{
    private PlatformUser() : base(Guid.Empty) { } // EF Core

    public PlatformUser(
        Guid id,
        string email,
        string name,
        string passwordHash) : base(id)
    {
        Email = email.ToLowerInvariant();
        Name = name;
        PasswordHash = passwordHash;
        Status = PlatformUserStatus.Active;
        CreatedAt = DateTime.UtcNow;
    }

    public string Email { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// AES-256-GCM encrypted TOTP secret stored as "{iv_hex}:{ciphertext_hex}:{tag_hex}".
    /// Null means TOTP not yet configured — setupRequired = true.
    /// </summary>
    public string? TotpSecret { get; private set; }

    /// <summary>
    /// BCrypt hash of the 24-digit emergency code. Single-use; set to null after use.
    /// </summary>
    public string? EmergencyCodeHash { get; private set; }

    public PlatformUserStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastLogin { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? DeactivatedAt { get; private set; }
    public Guid? DeactivatedBy { get; private set; }

    public bool TotpConfigured => TotpSecret is not null;
    public bool IsActive => Status == PlatformUserStatus.Active;

    public void Deactivate(Guid deactivatedBy)
    {
        Status = PlatformUserStatus.Inactive;
        DeactivatedAt = DateTime.UtcNow;
        DeactivatedBy = deactivatedBy;
    }

    public void RecordLogin() => LastLogin = DateTime.UtcNow;

    public void SetTotpSecret(string encryptedSecret, string emergencyCodeHash)
    {
        TotpSecret = encryptedSecret;
        EmergencyCodeHash = emergencyCodeHash;
    }

    /// <summary>
    /// Clears TOTP configuration after emergency recovery. Forces re-setup.
    /// </summary>
    public void ClearTotp()
    {
        TotpSecret = null;
        EmergencyCodeHash = null;
    }
}
