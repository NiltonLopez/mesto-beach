namespace MestoBeach.Shared.Venue;

/// <summary>
/// Única puerta al modo del local para el resto de módulos (SDD-001 §7). Implementación única en
/// Identity.Infrastructure, registrada como Scoped: no la consuma ningún singleton.
/// </summary>
public interface IVenueModeProvider
{
    /// Modo vigente del local. Redis → (miss) Postgres + SET NX EX 600 (solo si el tenant existe) → (Redis caído) Postgres sin SET. Lanza VenueModeUnavailableException solo si fallan Redis y Postgres; nunca devuelve un modo por defecto para un tenant existente.
    Task<VenueMode> GetAsync(Guid tenantId, CancellationToken ct = default);
}
