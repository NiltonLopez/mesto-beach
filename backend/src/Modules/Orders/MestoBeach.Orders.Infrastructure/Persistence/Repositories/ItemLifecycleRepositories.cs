using Microsoft.Extensions.Logging;
using MestoBeach.Shared.Venue;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Enums;
using MestoBeach.Orders.Domain.Repositories;

namespace MestoBeach.Orders.Infrastructure.Persistence.Repositories;

/// <summary>Acceso a los platos por su id, para marchar y dar por listo (ADR-025).</summary>
public sealed class OrderItemRepository : IOrderItemRepository
{
    private readonly OrdersDbContext _db;
    public OrderItemRepository(OrdersDbContext db) => _db = db;

    public Task<Order?> GetOrderByItemIdAsync(Guid itemId, Guid tenantId, CancellationToken ct) =>
        _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Items.Any(i => i.Id == itemId), ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<(bool Applied, bool OrderStatusChanged)> TryReadyFromQueueAsync(
        Order order, OrderItem item, Guid staffId, DateTime now, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // a) La regla viaja DENTRO del UPDATE (replica ReadyFromQueue en SQL). Con dos tablets
            //    pulsando a la vez, Postgres serializa la fila y la segunda no casa el WHERE.
            var affected = await _db.OrderItems
                .Where(i => i.Id == item.Id
                         && i.TenantId == order.TenantId
                         && (i.Status == OrderItemStatus.Queued || i.Status == OrderItemStatus.InPreparation))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, OrderItemStatus.Ready)
                    .SetProperty(i => i.ReadyAt, now)
                    .SetProperty(i => i.PreparationStartedAt, i => i.PreparationStartedAt ?? now)
                    .SetProperty(i => i.PreparationOrderedBy, i => i.PreparationOrderedBy ?? staffId)
                    .SetProperty(i => i.UpdatedAt, now), ct);

            // b) Perdió la carrera: nada se persiste.
            if (affected == 0)
            {
                await tx.RollbackAsync(ct);
                return (false, false);
            }

            // c) La entidad rastreada pasa a reflejar lo que quedó en la fila (incluido un
            //    preparation_started_at que el COALESCE conservó). No se re-aplica ReadyFromQueue().
            await _db.Entry(item).ReloadAsync(ct);
            var changed = order.RecalculateStatusFromItems();
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (true, changed);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task ReloadItemAsync(OrderItem item, CancellationToken ct) => _db.Entry(item).ReloadAsync(ct);
}

/// <summary>
/// Viajes de entrega (ADR-027). El método que importa es <see cref="TryClaimItemsAsync"/>:
/// el reclamo de platos es la única operación de todo el módulo con dos escritores compitiendo
/// por la misma fila, y se resuelve en la base de datos, no en memoria.
/// </summary>
public sealed class DeliveryTripRepository : IDeliveryTripRepository
{
    private readonly OrdersDbContext _db;
    private readonly IVenueModeProvider _venueMode;
    private readonly ILogger<DeliveryTripRepository> _logger;

    public DeliveryTripRepository(
        OrdersDbContext db, IVenueModeProvider venueMode, ILogger<DeliveryTripRepository> logger)
    {
        _db = db;
        _venueMode = venueMode;
        _logger = logger;
    }

    public Task<DeliveryTrip?> GetByIdAsync(Guid tripId, CancellationToken ct) =>
        _db.DeliveryTrips.FirstOrDefaultAsync(t => t.Id == tripId, ct);

    public Task<DeliveryTrip?> GetOpenByRunnerAsync(Guid tenantId, Guid runnerId, CancellationToken ct) =>
        _db.DeliveryTrips.FirstOrDefaultAsync(
            t => t.TenantId == tenantId
              && t.RunnerId == runnerId
              && t.Status == DeliveryTripStatus.Open, ct);

    public async Task<List<OrderItem>> ListItemsByIdsAsync(
        Guid tenantId, IReadOnlyList<Guid> itemIds, CancellationToken ct)
    {
        var ids = itemIds.Distinct().ToList();
        // Sin seguimiento: son solo para validar y dar buenos mensajes de error. La verdad la
        // fija el UPDATE condicional de TryClaimItemsAsync, que no pasa por el ChangeTracker.
        return await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && ids.Contains(i.Id))
            .ToListAsync(ct);
    }

    public async Task<ClaimOutcome> TryClaimItemsAsync(
        DeliveryTrip trip, IReadOnlyList<Guid> itemIds, CancellationToken ct)
    {
        var ids = itemIds.Distinct().ToList();
        var now = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _db.DeliveryTrips.AddAsync(trip, ct);
            // Puede reventar contra ux_delivery_trips_runner_open si otro dispositivo del mismo
            // runner abrió un viaje entre la comprobación previa y este insert.
            await _db.SaveChangesAsync(ct);

            // ── El reclamo atómico ────────────────────────────────────────────
            // Nada de leer-y-luego-escribir: la condición `status = READY` viaja DENTRO del
            // UPDATE. Con dos runners simultáneos, Postgres serializa la fila y el segundo
            // vuelve a evaluar el WHERE sobre la versión ya modificada: no casa, y su recuento
            // de filas afectadas sale corto.
            var affected = await _db.OrderItems
                .Where(i => i.TenantId == trip.TenantId
                         && ids.Contains(i.Id)
                         && i.Status == OrderItemStatus.Ready)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, OrderItemStatus.InDelivery)
                    .SetProperty(i => i.DeliveryTripId, trip.Id)
                    .SetProperty(i => i.UpdatedAt, now), ct);

            if (affected != ids.Count)
            {
                // Todo o nada: ni viaje ni platos reclamados.
                await tx.RollbackAsync(ct);
                Forget(trip);
                return ClaimOutcome.Conflict;
            }

            await tx.CommitAsync(ct);
            return ClaimOutcome.Claimed;
        }
        catch (DbUpdateException ex) when (IsRunnerTripDuplicate(ex))
        {
            await tx.RollbackAsync(ct);
            Forget(trip);
            return ClaimOutcome.TripAlreadyOpen;
        }
        catch (PostgresException ex) when (ex.SqlState is "40P01" or "40001")
        {
            // Interbloqueo o fallo de serialización entre dos reclamos que se solapan. La
            // transacción ya está abortada: para el runner es exactamente el mismo caso que
            // llegar tarde, así que se le devuelve el mismo conflicto.
            await tx.RollbackAsync(ct);
            Forget(trip);
            return ClaimOutcome.Conflict;
        }
    }

    public async Task<List<TripManifestRow>> ListManifestAsync(Guid tripId, CancellationToken ct)
    {
        var rows = await (
            from i in _db.OrderItems.AsNoTracking()
            join o in _db.Orders.AsNoTracking() on i.OrderId equals o.Id
            join s in _db.Sessions.AsNoTracking() on o.SessionId equals s.Id into sj
            from s in sj.DefaultIfEmpty()
            where i.DeliveryTripId == tripId
            select new
            {
                Item = i,
                o.TenantId,
                o.Observacion,
                o.CustomerName,
                o.Priority,
                TableCode = s != null ? s.TableCode : null,
                TableId = s != null ? (Guid?)s.TableId : null
            }).ToListAsync(ct);

        if (rows.Count == 0) return [];

        // Coordenadas del puesto (PRD-019): sessions.table_id → table_layouts del modo activo,
        // leído una vez por petición. Una sola consulta extra.
        var tenantId = rows[0].TenantId;
        var tableIds = rows.Where(r => r.TableId.HasValue)
                           .Select(r => r.TableId!.Value)
                           .Distinct()
                           .ToList();

        var layouts = await TableLayoutLookup.LoadActiveModeAsync(
            _db, _venueMode, _logger, tenantId, tableIds, ct);

        return rows.Select(r =>
        {
            TableLayoutReadModel? layout = null;
            if (r.TableId is Guid tid) layouts.TryGetValue(tid, out layout);
            return new TripManifestRow(
                r.Item, r.TableCode, layout?.FloorId, layout?.PosX, layout?.PosY, r.Observacion,
                r.CustomerName, r.Priority);
        }).ToList();
    }

    public Task<List<Order>> ListOrdersByTripAsync(Guid tripId, CancellationToken ct) =>
        _db.Orders
            .Include(o => o.Items)
            .Where(o => o.Items.Any(i => i.DeliveryTripId == tripId))
            .ToListAsync(ct);

    public async Task<bool> TryCloseAsync(
        Guid tripId, DeliveryTripStatus target, CancellationToken ct)
    {
        // La condición `status = OPEN` viaja dentro del UPDATE, igual que en el reclamo: si dos
        // peticiones intentan cerrar el mismo viaje, solo una afecta filas.
        var affected = await _db.DeliveryTrips
            .Where(t => t.Id == tripId && t.Status == DeliveryTripStatus.Open)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, target)
                .SetProperty(t => t.CompletedAt, DateTime.UtcNow), ct);

        return affected == 1;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Saca del ChangeTracker un viaje que no llegó a existir: si se quedara "Added", el
    /// siguiente SaveChanges de la misma petición intentaría insertarlo otra vez.
    /// </summary>
    private void Forget(DeliveryTrip trip) => _db.Entry(trip).State = EntityState.Detached;

    private static bool IsRunnerTripDuplicate(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" } pg
        && (pg.ConstraintName?.Contains("delivery_trips", StringComparison.OrdinalIgnoreCase) ?? false);
}
