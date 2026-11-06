using Microsoft.Extensions.Logging;
using MestoBeach.Shared.Venue;
using Microsoft.EntityFrameworkCore;
using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Enums;
using MestoBeach.Orders.Domain.Repositories;
using MestoBeach.Orders.Infrastructure.Persistence;

namespace MestoBeach.Orders.Infrastructure.Persistence.Repositories;

// Order.StationId está marcado [Obsolete] (ADR-025: la estación vive en el ítem), pero estas
// consultas alimentan el tablero KDS vigente, que sigue razonando por pedido. Se silencia aquí
// a propósito; el aviso desaparece cuando la UI migre al contrato por ítem.
#pragma warning disable CS0618

public sealed class OrderRepository : IOrderRepository
{
    private readonly OrdersDbContext _db;
    private readonly IVenueModeProvider _venueMode;
    private readonly ILogger<OrderRepository> _logger;

    public OrderRepository(OrdersDbContext db, IVenueModeProvider venueMode, ILogger<OrderRepository> logger)
    {
        _db = db;
        _venueMode = venueMode;
        _logger = logger;
    }

    private static readonly OrderStatus[] ActiveStatuses =
        [OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.InPreparation, OrderStatus.Ready];

    public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken ct) =>
        _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);

    public Task<Order?> GetByIdWithItemsAsync(Guid orderId, CancellationToken ct) =>
        _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);

    public async Task<List<Order>> ListBySessionAsync(
        Guid sessionId, OrderStatus? status,
        Guid? stationId, bool filterNullStation, CancellationToken ct)
    {
        var q = _db.Orders.Include(o => o.Items)
            .Where(o => o.SessionId == sessionId);

        if (status.HasValue) q = q.Where(o => o.Status == status.Value);

        if (filterNullStation)
            q = q.Where(o => o.StationId == null);
        else if (stationId.HasValue)
            q = q.Where(o => o.StationId == stationId.Value);

        return await q.OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
    }

    // KDS active statuses: confirmed orders that the kitchen still has to act on.
    // (Pending = cart not yet confirmed; Delivered/Cancelled = done.)
    private static readonly OrderStatus[] KdsActiveStatuses =
        [OrderStatus.Confirmed, OrderStatus.InPreparation, OrderStatus.Ready];

    public async Task<List<(Order Order, string? TableCode)>> ListActiveByTenantAsync(
        Guid tenantId, Guid? stationId, bool filterNullStation, CancellationToken ct)
    {
        var q = _db.Orders.Include(o => o.Items)
            .Where(o => o.TenantId == tenantId && KdsActiveStatuses.Contains(o.Status));

        if (filterNullStation)
            q = q.Where(o => o.StationId == null);
        else if (stationId.HasValue)
            q = q.Where(o => o.StationId == stationId.Value);

        // ADR-038: reservados (priority 1) primero; a igual prioridad, FIFO por created_at.
        var orders = await q.OrderByDescending(o => o.Priority).ThenBy(o => o.CreatedAt).ToListAsync(ct);
        if (orders.Count == 0) return [];

        // Resolve table codes in a single extra query (session read-model lives in this context).
        var sessionIds = orders.Select(o => o.SessionId).Distinct().ToList();
        var tableCodes = await _db.Sessions
            .Where(s => sessionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.TableCode, ct);

        return orders
            .Select(o => (o, tableCodes.TryGetValue(o.SessionId, out var tc) ? tc : null))
            .ToList();
    }

    /// <summary>
    /// Cola del pase de una estación (RF-1830). Misma forma que el manifiesto del viaje en
    /// <c>DeliveryTripRepository.ListManifestAsync</c> —ítem → comanda → sesión, y luego una
    /// consulta extra para las coordenadas del puesto— para que la cola y la ruta hablen del
    /// mismo sitio de la misma manera.
    /// </summary>
    public async Task<List<DeliveryQueueRow>> ListReadyItemsByStationAsync(
        Guid tenantId, Guid stationId, CancellationToken ct)
    {
        var filas = await (
            from i in _db.OrderItems.AsNoTracking()
            join o in _db.Orders.AsNoTracking() on i.OrderId equals o.Id
            join s in _db.Sessions.AsNoTracking() on o.SessionId equals s.Id into sj
            from s in sj.DefaultIfEmpty()
            where i.TenantId == tenantId
               && i.StationId == stationId
               && i.Status == OrderItemStatus.Ready
            // ADR-038: reservados primero (priority DESC); a igual prioridad, FIFO: el plato que
            // lleva más rato en el pase se está enfriando, sale primero.
            orderby o.Priority descending, i.ReadyAt
            select new
            {
                Item = i,
                o.SessionId,
                TableCode = s != null ? s.TableCode : null,
                TableId = s != null ? (Guid?)s.TableId : null,
                o.CustomerName,
                o.Observacion,
                o.Priority
            }).ToListAsync(ct);

        if (filas.Count == 0) return [];

        // Coordenadas del puesto (PRD-019): sessions.table_id → table_layouts del modo activo,
        // leído una vez por petición. Unión externa a propósito — un puesto sin fila en el modo,
        // apagado en él o sin colocar se lista igual, solo que sin chincheta.
        var mesas = filas.Where(f => f.TableId.HasValue)
                         .Select(f => f.TableId!.Value)
                         .Distinct()
                         .ToList();

        var puestos = await TableLayoutLookup.LoadActiveModeAsync(
            _db, _venueMode, _logger, tenantId, mesas, ct);

        return filas.Select(f =>
        {
            TableLayoutReadModel? puesto = null;
            if (f.TableId is Guid tid) puestos.TryGetValue(tid, out puesto);
            return new DeliveryQueueRow(
                f.Item, f.SessionId, f.TableCode,
                puesto?.FloorId, puesto?.PosX, puesto?.PosY,
                f.CustomerName, f.Observacion, f.Priority);
        }).ToList();
    }

    public async Task AddAsync(Order order, CancellationToken ct) =>
        await _db.Orders.AddAsync(order, ct);

    public Task<bool> HasActiveOrdersForStationAsync(Guid stationId, CancellationToken ct) =>
        _db.Orders.AnyAsync(
            o => o.StationId == stationId && ActiveStatuses.Contains(o.Status), ct);

    public Task<bool> HasActiveOrdersForSessionAsync(Guid sessionId, CancellationToken ct) =>
        _db.Orders.AnyAsync(
            o => o.SessionId == sessionId && ActiveStatuses.Contains(o.Status), ct);

    public async Task<(int TotalItems, int TotalCents, int PendingItems, int DeliveredItems)>
        GetSessionSummaryAsync(Guid sessionId, CancellationToken ct)
    {
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.SessionId == sessionId && o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);

        int totalItems = orders.Sum(o => o.Items.Sum(i => i.Quantity));
        int totalCents = orders.Sum(o => o.Items.Sum(i => i.ItemPriceSnapshotCents * i.Quantity));
        int pendingItems = orders
            .Where(o => o.Status == OrderStatus.Pending)
            .Sum(o => o.Items.Sum(i => i.Quantity));
        // FINALIZED es el DELIVERED derivado del ciclo por ítem (ADR-025): para "Mi cuenta"
        // y el resumen de la sesión, un pedido finalizado está entregado.
        int deliveredItems = orders
            .Where(o => o.Status is OrderStatus.Delivered or OrderStatus.Finalized)
            .Sum(o => o.Items.Sum(i => i.Quantity));

        return (totalItems, totalCents, pendingItems, deliveredItems);
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}

#pragma warning restore CS0618
