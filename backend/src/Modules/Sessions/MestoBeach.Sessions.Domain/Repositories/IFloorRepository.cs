using MestoBeach.Sessions.Domain.Entities;

namespace MestoBeach.Sessions.Domain.Repositories;

public interface IFloorRepository
{
    Task<Floor?> GetByIdAsync(Guid floorId, CancellationToken ct = default);
    Task<List<Floor>> ListAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(Floor floor, CancellationToken ct = default);
    Task RemoveAsync(Floor floor, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
