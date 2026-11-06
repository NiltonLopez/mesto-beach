using MestoBeach.Sessions.Domain.Entities;

namespace MestoBeach.Sessions.Domain.Repositories;

public interface ITableRepository
{
    Task<DiningTable?> GetByIdAsync(Guid tableId, CancellationToken ct = default);
    Task<DiningTable?> GetByCodeAsync(Guid tenantId, string tableCode, CancellationToken ct = default);
    Task<List<DiningTable>> ListAsync(Guid tenantId, bool? active, string? zone, CancellationToken ct = default);
    Task<List<DiningTable>> ListByIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task AddAsync(DiningTable table, CancellationToken ct = default);
    Task RemoveAsync(DiningTable table, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    // ─── table_layouts (PRD-019, ADR-038): una fila por (mesa, modo) ──────────────
    Task<TableLayout?> GetLayoutAsync(Guid tableId, string mode, CancellationToken ct = default);
    Task<List<TableLayout>> ListLayoutsAsync(Guid tenantId, string mode, CancellationToken ct = default);
    Task<List<TableLayout>> ListLayoutsByFloorAsync(Guid floorId, CancellationToken ct = default);
    Task AddLayoutAsync(TableLayout layout, CancellationToken ct = default);
}
