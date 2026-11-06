using MestoBeach.Payments.Application.Common;
using MestoBeach.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MestoBeach.Payments.Infrastructure.Services;

public sealed class SessionReaderService : ISessionReader
{
    private readonly PaymentsDbContext _db;
    public SessionReaderService(PaymentsDbContext db) => _db = db;

    public async Task<SessionInfo?> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        var s = await _db.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (s is null) return null;
        return new SessionInfo(s.Id, s.TenantId, IsOpen: s.Status == "OPEN");
    }
}
