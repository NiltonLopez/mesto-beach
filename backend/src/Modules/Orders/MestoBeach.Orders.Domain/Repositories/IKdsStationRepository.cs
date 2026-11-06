using MestoBeach.Orders.Domain.Entities;

namespace MestoBeach.Orders.Domain.Repositories;

public interface IKdsStationRepository
{
    Task<KdsStation?> GetByIdAsync(Guid stationId, CancellationToken ct = default);
    Task<List<KdsStation>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<KdsStation?> GetByNameAsync(Guid tenantId, string name, CancellationToken ct = default);
    Task<List<KdsStation>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(KdsStation station, CancellationToken ct = default);
    Task DeleteAsync(KdsStation station, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
