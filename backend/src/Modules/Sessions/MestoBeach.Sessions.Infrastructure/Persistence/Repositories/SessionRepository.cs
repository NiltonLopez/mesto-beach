using Microsoft.EntityFrameworkCore;
using MestoBeach.Sessions.Domain.Entities;
using MestoBeach.Sessions.Domain.Enums;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Sessions.Infrastructure.Persistence;

namespace MestoBeach.Sessions.Infrastructure.Persistence.Repositories;

public sealed class SessionRepository : ISessionRepository
{
    private readonly SessionsDbContext _db;
    public SessionRepository(SessionsDbContext db) => _db = db;

    public Task<Session?> GetByIdAsync(Guid sessionId, CancellationToken ct) =>
        _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public Task<Session?> GetOpenByTableIdAsync(Guid tableId, CancellationToken ct) =>
        _db.Sessions.FirstOrDefaultAsync(
            s => s.TableId == tableId && s.Status == SessionStatus.Open, ct);

    public Task<Session?> GetOpenByTableCodeAsync(Guid tenantId, string tableCode, CancellationToken ct) =>
        _db.Sessions.FirstOrDefaultAsync(
            s => s.TenantId == tenantId && s.TableCode == tableCode && s.Status == SessionStatus.Open, ct);

    public Task<List<Session>> GetOpenByTenantAsync(Guid tenantId, CancellationToken ct) =>
        _db.Sessions
            .Where(s => s.TenantId == tenantId && s.Status == SessionStatus.Open)
            .ToListAsync(ct);

    private IQueryable<Session> ClosedQuery(Guid tenantId, DateTime? from, DateTime? to, string? tableCode)
    {
        var q = _db.Sessions.Where(s => s.TenantId == tenantId && s.Status == SessionStatus.Closed);
        if (from is not null) q = q.Where(s => s.ClosedAt >= from);
        if (to is not null) q = q.Where(s => s.ClosedAt <= to);
        if (!string.IsNullOrWhiteSpace(tableCode)) q = q.Where(s => s.TableCode == tableCode);
        return q;
    }

    public Task<List<Session>> GetClosedByTenantAsync(
        Guid tenantId, DateTime? from, DateTime? to, string? tableCode, int limit, int offset, CancellationToken ct) =>
        ClosedQuery(tenantId, from, to, tableCode)
            .OrderByDescending(s => s.ClosedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

    public Task<int> CountClosedByTenantAsync(
        Guid tenantId, DateTime? from, DateTime? to, string? tableCode, CancellationToken ct) =>
        ClosedQuery(tenantId, from, to, tableCode).CountAsync(ct);

    public Task<bool> HasOpenByTableIdAsync(Guid tableId, CancellationToken ct) =>
        _db.Sessions.AnyAsync(
            s => s.TableId == tableId && s.Status == SessionStatus.Open, ct);

    public async Task AddAsync(Session session, CancellationToken ct) =>
        await _db.Sessions.AddAsync(session, ct);

    public Task<int> CountParticipantsAsync(Guid sessionId, CancellationToken ct) =>
        _db.Participants.CountAsync(p => p.SessionId == sessionId, ct);

    public Task<SessionParticipant?> GetParticipantAsync(Guid sessionId, string deviceId, CancellationToken ct) =>
        _db.Participants.FirstOrDefaultAsync(
            p => p.SessionId == sessionId && p.DeviceId == deviceId, ct);

    public Task<SessionParticipant?> GetParticipantByIdAsync(Guid participantId, CancellationToken ct) =>
        _db.Participants.FirstOrDefaultAsync(p => p.Id == participantId, ct);

    public Task<List<SessionParticipant>> GetParticipantsAsync(Guid sessionId, CancellationToken ct) =>
        _db.Participants.Where(p => p.SessionId == sessionId).ToListAsync(ct);

    public async Task AddParticipantAsync(SessionParticipant participant, CancellationToken ct) =>
        await _db.Participants.AddAsync(participant, ct);

    public async Task<(SessionParticipant participant, bool created)> AddOrGetParticipantAsync(
        SessionParticipant candidate, CancellationToken ct)
    {
        var existing = await GetParticipantAsync(candidate.SessionId, candidate.DeviceId, ct);
        if (existing is not null) return (existing, false);

        await _db.Participants.AddAsync(candidate, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
            return (candidate, true);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race: a concurrent request (e.g. another tab) inserted the participant first.
            // Drop our candidate and return the one that won.
            _db.Entry(candidate).State = EntityState.Detached;
            var winner = await GetParticipantAsync(candidate.SessionId, candidate.DeviceId, ct);
            if (winner is null) throw;
            return (winner, false);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    /// <summary>
    /// Checks for active orders. Since Orders module isn't built yet, always returns false.
    /// Replace with actual cross-module query when Orders module is available.
    /// </summary>
    public Task<bool> HasActiveOrdersAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(false);

    public async Task<string?> GetStaffNameAsync(Guid staffId, CancellationToken ct)
    {
        var staff = await _db.Staff.FirstOrDefaultAsync(s => s.Id == staffId, ct);
        return staff?.Name;
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}
