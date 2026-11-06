using MestoBeach.Sessions.Domain.Entities;

namespace MestoBeach.Sessions.Domain.Repositories;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid sessionId, CancellationToken ct = default);
    Task<Session?> GetOpenByTableIdAsync(Guid tableId, CancellationToken ct = default);
    Task<Session?> GetOpenByTableCodeAsync(Guid tenantId, string tableCode, CancellationToken ct = default);

    /// <summary>Todas las sesiones abiertas de un tenant. El mapa de la Host lo lee de una vez
    /// para el pax por mesa, en vez de una consulta por mesa ocupada (RF-1841).</summary>
    Task<List<Session>> GetOpenByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Closed sessions of a tenant for history, filtered by date range and/or table code.</summary>
    Task<List<Session>> GetClosedByTenantAsync(
        Guid tenantId, DateTime? from, DateTime? to, string? tableCode, int limit, int offset, CancellationToken ct = default);
    Task<int> CountClosedByTenantAsync(
        Guid tenantId, DateTime? from, DateTime? to, string? tableCode, CancellationToken ct = default);
    Task AddAsync(Session session, CancellationToken ct = default);
    Task<int> CountParticipantsAsync(Guid sessionId, CancellationToken ct = default);

    Task<SessionParticipant?> GetParticipantAsync(Guid sessionId, string deviceId, CancellationToken ct = default);
    Task<SessionParticipant?> GetParticipantByIdAsync(Guid participantId, CancellationToken ct = default);
    Task<List<SessionParticipant>> GetParticipantsAsync(Guid sessionId, CancellationToken ct = default);
    Task AddParticipantAsync(SessionParticipant participant, CancellationToken ct = default);
    /// <summary>
    /// Atomically returns the participant for (session, device): the existing one if present,
    /// otherwise persists and returns <paramref name="candidate"/>. Safe against concurrent joins
    /// (e.g. the same invite link opened in multiple tabs): on a unique-violation race it returns
    /// the participant the winning request created. `created` is true only when the candidate was inserted.
    /// </summary>
    Task<(SessionParticipant participant, bool created)> AddOrGetParticipantAsync(
        SessionParticipant candidate, CancellationToken ct = default);

    Task<bool> HasOpenByTableIdAsync(Guid tableId, CancellationToken ct = default);
    Task<bool> HasActiveOrdersAsync(Guid sessionId, CancellationToken ct = default);

    Task<string?> GetStaffNameAsync(Guid staffId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
