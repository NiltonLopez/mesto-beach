using Microsoft.EntityFrameworkCore;
using MestoBeach.Sessions.Domain.Entities;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Sessions.Infrastructure.Persistence;

namespace MestoBeach.Sessions.Infrastructure.Persistence.Repositories;

public sealed class FloorRepository : IFloorRepository
{
    private readonly SessionsDbContext _db;
    public FloorRepository(SessionsDbContext db) => _db = db;

    public Task<Floor?> GetByIdAsync(Guid floorId, CancellationToken ct) =>
        _db.Floors.FirstOrDefaultAsync(f => f.Id == floorId, ct);

    public Task<List<Floor>> ListAsync(Guid tenantId, CancellationToken ct) =>
        _db.Floors.Where(f => f.TenantId == tenantId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Floor floor, CancellationToken ct) =>
        await _db.Floors.AddAsync(floor, ct);

    public Task RemoveAsync(Floor floor, CancellationToken ct)
    {
        _db.Floors.Remove(floor);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}
