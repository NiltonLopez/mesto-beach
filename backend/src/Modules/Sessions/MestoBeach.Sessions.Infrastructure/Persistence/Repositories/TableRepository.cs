using Microsoft.EntityFrameworkCore;
using MestoBeach.Sessions.Domain.Entities;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Sessions.Infrastructure.Persistence;

namespace MestoBeach.Sessions.Infrastructure.Persistence.Repositories;

public sealed class TableRepository : ITableRepository
{
    private readonly SessionsDbContext _db;
    public TableRepository(SessionsDbContext db) => _db = db;

    public Task<DiningTable?> GetByIdAsync(Guid tableId, CancellationToken ct) =>
        _db.Tables.FirstOrDefaultAsync(t => t.Id == tableId, ct);

    public Task<DiningTable?> GetByCodeAsync(Guid tenantId, string tableCode, CancellationToken ct) =>
        _db.Tables.FirstOrDefaultAsync(
            t => t.TenantId == tenantId && t.TableCode == tableCode, ct);

    public Task<List<DiningTable>> ListAsync(
        Guid tenantId, bool? active, string? zone, CancellationToken ct)
    {
        var q = _db.Tables.Where(t => t.TenantId == tenantId);
        if (active.HasValue) q = q.Where(t => t.Active == active.Value);
        if (zone is not null) q = q.Where(t => t.Zone == zone);
        return q.ToListAsync(ct);
    }

    public Task<List<DiningTable>> ListByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        _db.Tables.Where(t => t.TenantId == tenantId && ids.Contains(t.Id)).ToListAsync(ct);

    public async Task AddAsync(DiningTable table, CancellationToken ct) =>
        await _db.Tables.AddAsync(table, ct);

    public Task RemoveAsync(DiningTable table, CancellationToken ct)
    {
        _db.Tables.Remove(table);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);

    public Task<TableLayout?> GetLayoutAsync(Guid tableId, string mode, CancellationToken ct) =>
        _db.TableLayouts.FirstOrDefaultAsync(l => l.TableId == tableId && l.Mode == mode, ct);

    public Task<List<TableLayout>> ListLayoutsAsync(Guid tenantId, string mode, CancellationToken ct) =>
        _db.TableLayouts.Where(l => l.TenantId == tenantId && l.Mode == mode).ToListAsync(ct);

    public Task<List<TableLayout>> ListLayoutsByFloorAsync(Guid floorId, CancellationToken ct) =>
        _db.TableLayouts.Where(l => l.FloorId == floorId).ToListAsync(ct);

    public async Task AddLayoutAsync(TableLayout layout, CancellationToken ct) =>
        await _db.TableLayouts.AddAsync(layout, ct);
}
