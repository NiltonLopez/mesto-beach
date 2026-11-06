using Microsoft.EntityFrameworkCore;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Infrastructure.Persistence;

namespace MestoBeach.Sessions.Infrastructure.Services;

/// <summary>Lee <c>kds_stations</c> (de Orders) en solo lectura para validar la barra de una mesa.</summary>
public sealed class StationReadService : IStationReadService
{
    private readonly SessionsDbContext _db;
    public StationReadService(SessionsDbContext db) => _db = db;

    public async Task<StationInfo?> GetAsync(Guid stationId, CancellationToken ct = default)
    {
        var s = await _db.KdsStations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == stationId, ct);
        return s is null ? null : new StationInfo(s.Id, s.TenantId, s.Name, s.FlowProfile, s.IsActive, s.ActiveIn);
    }
}
