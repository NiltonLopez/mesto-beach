using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Domain.Entities;

/// <summary>
/// Long-lived device token for KDS (Kitchen Display System) tablets.
/// Rolling 30-day TTL managed via Redis key TTL (Redis is authoritative for session liveness).
/// DB record is the source of truth for revocation by admin (revoked_at IS NOT NULL = revoked).
/// </summary>
public sealed class DeviceToken : AggregateRoot
{
    private DeviceToken() : base(Guid.Empty) { } // EF Core

    public DeviceToken(
        Guid id,
        Guid tenantId,
        Guid staffId,
        string deviceId,
        string deviceName,
        Guid? stationId) : base(id)
    {
        TenantId = tenantId;
        StaffId = staffId;
        DeviceId = deviceId;
        DeviceName = deviceName;
        StationId = stationId;
        CreatedAt = DateTime.UtcNow;
        LastSeenAt = DateTime.UtcNow;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Admin staff member who registered this device.</summary>
    public Guid StaffId { get; private set; }

    /// <summary>Human-readable device identifier, e.g. "kds-cocina-01". Unique per tenant (active records).</summary>
    public string DeviceId { get; private set; } = string.Empty;

    /// <summary>Human display name, e.g. "KDS Cocina Principal".</summary>
    public string DeviceName { get; private set; } = string.Empty;

    /// <summary>Optional kitchen station this device is assigned to.</summary>
    public Guid? StationId { get; private set; }

    /// <summary>Null = active; non-null = permanently revoked at this timestamp.</summary>
    public DateTime? RevokedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Updated on every successful token refresh.</summary>
    public DateTime LastSeenAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public void Revoke()
    {
        if (RevokedAt is null)
            RevokedAt = DateTime.UtcNow;
    }

    public void RecordSeen() => LastSeenAt = DateTime.UtcNow;

    public void UpdateStation(Guid? stationId) => StationId = stationId;
}
