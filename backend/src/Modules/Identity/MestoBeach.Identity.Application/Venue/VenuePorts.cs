using MestoBeach.Shared.Venue;

namespace MestoBeach.Identity.Application.Venue;

// ══════════════════════════════════════════════════════════════════════════════
// MODO DEL LOCAL (ADR-036, SDD-001 §7) — puertos de la capa Application.
// La persistencia (transacción con FOR UPDATE, pre-flight sobre proyecciones de Sessions/Orders,
// historial) y la caché de Redis se implementan en Identity.Infrastructure.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>Foto del pre-flight. <c>OrphanItems</c> no bloquea.</summary>
public sealed record VenuePreflight(
    int OpenSessions,
    int ActiveItems,
    int OpenTrips,
    int OrphanItems,
    IReadOnlyList<string> Tables)
{
    public bool Ready => OpenSessions == 0 && ActiveItems == 0 && OpenTrips == 0;
}

/// <summary>Fila de <c>tenants</c> relevante para el modo.</summary>
public sealed record TenantVenueRow(VenueMode Mode, DateTime? ChangedAt);

/// <summary>Estación tal como la ve Identity (proyección de <c>kds_stations</c>, ADR-037).</summary>
public sealed record VenueStationRow(Guid Id, string Name, string FlowProfile, bool IsActive, IReadOnlyList<string> ActiveIn);

/// <summary>Resultado de la transacción de cambio.</summary>
public abstract record VenueChangeOutcome
{
    public sealed record TenantNotFound : VenueChangeOutcome;
    /// <summary>El local ya estaba en ese modo: transacción vacía, sin auditoría.</summary>
    public sealed record AlreadyInMode(VenueMode Mode, DateTime? ChangedAt) : VenueChangeOutcome;
    public sealed record NotIdle(VenuePreflight Preflight) : VenueChangeOutcome;
    public sealed record Changed(VenueMode Previous, DateTime ChangedAt, VenuePreflight Preflight) : VenueChangeOutcome;
}

public sealed record VenueHistoryRow(
    Guid Id,
    VenueMode FromMode,
    VenueMode ToMode,
    Guid ChangedBy,
    string? ChangedByName,
    DateTime ChangedAt,
    string PreflightJson);

public interface IVenueStore
{
    Task<TenantVenueRow?> GetTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Todas las estaciones del tenant (encendidas o no), por <c>created_at</c>.</summary>
    Task<IReadOnlyList<VenueStationRow>> GetStationsAsync(Guid tenantId, CancellationToken ct = default);

    Task<VenuePreflight> GetPreflightAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Pasos 1–3 de SDD-001 §7: transacción, <c>SELECT … FOR UPDATE</c> sobre el tenant,
    /// idempotencia, pre-flight dentro de la transacción y escritura (UPDATE + INSERT) con COMMIT.
    /// </summary>
    Task<VenueChangeOutcome> ChangeModeAsync(Guid tenantId, VenueMode mode, Guid changedBy,
        DateTime now, CancellationToken ct = default);

    Task<(IReadOnlyList<VenueHistoryRow> Rows, int Total)> GetHistoryAsync(Guid tenantId, int limit,
        CancellationToken ct = default);
}

/// <summary>Escritura de la clave <c>venue:{tenantId}:mode</c> (TTL 10 min).</summary>
public interface IVenueModeCache
{
    /// <summary><c>SET … EX 600</c> incondicional. Lanza si Redis falla.</summary>
    Task SetAsync(Guid tenantId, VenueMode mode, CancellationToken ct = default);

    /// <summary><c>DEL</c>. Lanza si Redis falla.</summary>
    Task DeleteAsync(Guid tenantId, CancellationToken ct = default);
}
