using Microsoft.EntityFrameworkCore;
using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Repositories;
using MestoBeach.Orders.Infrastructure.Persistence;

namespace MestoBeach.Orders.Infrastructure.Persistence.Repositories;

public sealed class KdsStationRepository : IKdsStationRepository
{
    private readonly OrdersDbContext _db;
    public KdsStationRepository(OrdersDbContext db) => _db = db;

    public Task<KdsStation?> GetByIdAsync(Guid stationId, CancellationToken ct) =>
        _db.KdsStations.FirstOrDefaultAsync(s => s.Id == stationId, ct);

    public Task<List<KdsStation>> ListByTenantAsync(Guid tenantId, CancellationToken ct) =>
        _db.KdsStations.Where(s => s.TenantId == tenantId).OrderBy(s => s.CreatedAt).ToListAsync(ct);

    public Task<KdsStation?> GetByNameAsync(Guid tenantId, string name, CancellationToken ct) =>
        _db.KdsStations.FirstOrDefaultAsync(
            s => s.TenantId == tenantId &&
                 s.Name.ToLower() == name.ToLower().Trim(), ct);

    public Task<List<KdsStation>> GetByTenantAsync(Guid tenantId, CancellationToken ct) =>
        _db.KdsStations.Where(s => s.TenantId == tenantId).ToListAsync(ct);

    public async Task AddAsync(KdsStation station, CancellationToken ct) =>
        await _db.KdsStations.AddAsync(station, ct);

    public Task DeleteAsync(KdsStation station, CancellationToken ct)
    {
        _db.KdsStations.Remove(station);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}
