using MestoBeach.Orders.Application.Common;
using MestoBeach.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Orders.Infrastructure.Services;

public sealed class SessionReadService : ISessionReadService
{
    private readonly OrdersDbContext _db;
    private readonly ILogger<SessionReadService> _logger;

    public SessionReadService(OrdersDbContext db, ILogger<SessionReadService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session is null) return null;

        return new SessionInfo(
            session.Id,
            session.TenantId,
            session.TableCode,
            IsOpen: session.Status == "OPEN",
            PinRequired: session.PinRequired,
            TableId: session.TableId);
    }

    public async Task<ParticipantInfo?> GetParticipantAsync(
        Guid participantId, Guid sessionId, CancellationToken ct = default)
    {
        var p = await _db.Participants
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == participantId && x.SessionId == sessionId, ct);

        return p is null ? null : new ParticipantInfo(p.Id, p.SessionId, p.CustomerAccountId, p.DisplayName, p.PinValidated);
    }

    public async Task<string?> GetTableKindAsync(Guid tableId, CancellationToken ct = default)
    {
        if (tableId == Guid.Empty) return null;
        return await _db.Set<TableReadModel>()
            .AsNoTracking()
            .Where(t => t.Id == tableId)
            .Select(t => t.Kind)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Guid?> GetTableStationAsync(Guid tableId, string mode, CancellationToken ct = default)
    {
        if (tableId == Guid.Empty) return null;
        return await _db.Set<TableLayoutReadModel>()
            .AsNoTracking()
            .Where(l => l.TableId == tableId && l.Mode == mode)
            .Select(l => l.StationId)
            .FirstOrDefaultAsync(ct);
    }
}
