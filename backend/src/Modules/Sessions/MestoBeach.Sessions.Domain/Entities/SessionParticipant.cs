namespace MestoBeach.Sessions.Domain.Entities;

/// <summary>A client device participating in a session.</summary>
public sealed class SessionParticipant
{
    private SessionParticipant() { } // EF Core

    public SessionParticipant(
        Guid id,
        Guid sessionId,
        string deviceId,
        Guid? customerAccountId,
        bool isHost,
        string? inviteToken)
    {
        Id = id;
        SessionId = sessionId;
        DeviceId = deviceId;
        CustomerAccountId = customerAccountId;
        IsHost = isHost;
        InviteToken = inviteToken;
        PinValidated = false;
        JoinedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public string DeviceId { get; private set; } = string.Empty;
    public Guid? CustomerAccountId { get; private set; }
    /// <summary>Name shown to staff/KDS. Captured on the menu's "identify" screen.</summary>
    public string? DisplayName { get; private set; }
    public bool IsHost { get; private set; }
    public DateTime JoinedAt { get; private set; }
    public bool PinValidated { get; private set; }
    public string? InviteToken { get; private set; }

    public void MarkPinValidated() => PinValidated = true;

    public void AssociateCustomer(Guid customerAccountId) =>
        CustomerAccountId = customerAccountId;

    public void SetDisplayName(string? displayName) =>
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? DisplayName : displayName.Trim();
}
