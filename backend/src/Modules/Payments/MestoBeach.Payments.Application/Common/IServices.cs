namespace MestoBeach.Payments.Application.Common;

// ─── Session close (cross-module) ────────────────────────────────────────────

public interface ISessionCloser
{
    /// <summary>
    /// Closes the session via Sessions module (MediatR CloseSessionCommand).
    /// Returns false if session was already closed or not found — without throwing.
    /// </summary>
    Task<bool> CloseAsync(Guid tenantId, Guid sessionId, Guid requesterId, CancellationToken ct = default);
}

// ─── Session orders total (cross-module) ─────────────────────────────────────

public record SessionOrdersTotal(
    int TotalCents,           // Sum of (price * qty) for non-cancelled orders
    int OrderCount);

public interface ISessionOrderReader
{
    Task<SessionOrdersTotal> GetTotalAsync(Guid sessionId, CancellationToken ct = default);
}

// ─── Session existence check (cross-module) ──────────────────────────────────

public record SessionInfo(Guid Id, Guid TenantId, bool IsOpen);

public interface ISessionReader
{
    Task<SessionInfo?> GetAsync(Guid sessionId, CancellationToken ct = default);
}
