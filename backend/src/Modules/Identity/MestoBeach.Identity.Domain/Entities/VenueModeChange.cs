using MestoBeach.Shared.Venue;

namespace MestoBeach.Identity.Domain.Entities;

/// <summary>
/// Auditoría tipada de un cambio de modo del local (tabla <c>venue_mode_changes</c>, SDD-001 §7).
/// Un cambio idempotente no deja fila: <c>FromMode != ToMode</c> siempre.
/// </summary>
public sealed class VenueModeChange
{
    private VenueModeChange() { } // EF Core

    public VenueModeChange(Guid tenantId, VenueMode fromMode, VenueMode toMode, Guid changedBy,
        DateTime changedAt, string preflightJson)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        FromMode = fromMode;
        ToMode = toMode;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
        Preflight = preflightJson;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public VenueMode FromMode { get; private set; }
    public VenueMode ToMode { get; private set; }
    /// <summary><c>users.id</c> del Admin; sin FK a propósito (la auditoría sobrevive a la cuenta).</summary>
    public Guid ChangedBy { get; private set; }
    public DateTime ChangedAt { get; private set; }
    /// <summary>Foto del pre-flight (jsonb).</summary>
    public string Preflight { get; private set; } = "{}";
}
