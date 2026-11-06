using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Enums;

namespace MestoBeach.Orders.Domain.Repositories;

// ─────────────────────────────────────────────────────────────────────────────
// Persistencia del ciclo de vida por ítem (ADR-025/ADR-027).
//
// Va aparte de IOrderRepository a propósito: aquel razona por pedido (lo que alimenta el
// tablero KDS vigente) y aquí se razona por plato y por viaje. Añadir contratos nuevos en
// lugar de ensanchar el existente mantiene intacto todo lo que ya funciona.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Acceso a los platos por su propio id, para las acciones de coursing.</summary>
public interface IOrderItemRepository
{
    /// <summary>
    /// Devuelve el pedido dueño del plato <b>con todos sus ítems cargados</b> —hacen falta
    /// todos para derivar el estado del pedido (<see cref="Order.RecalculateStatusFromItems"/>)—
    /// o <c>null</c> si el plato no existe o es de otro tenant.
    /// </summary>
    Task<Order?> GetOrderByItemIdAsync(Guid itemId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Mecánica única de <c>QUEUED → READY</c> en una estación <c>BAR</c> (ADR-037, SDD-004):
    /// en una transacción, UPDATE condicional (<c>status IN ('QUEUED','IN_PREPARATION')</c>, sellos
    /// con COALESCE); con 0 filas hace rollback y devuelve <c>Applied = false</c>; con 1 fila recarga
    /// el plato rastreado (<c>Entry(item).ReloadAsync</c>), recalcula el estado del pedido, guarda y
    /// hace commit. No vuelve a aplicar <c>ReadyFromQueue()</c> sobre la entidad cargada antes.
    /// </summary>
    Task<(bool Applied, bool OrderStatusChanged)> TryReadyFromQueueAsync(
        Order order, OrderItem item, Guid staffId, DateTime now, CancellationToken ct = default);

    /// <summary>Relee el plato tal como quedó en la fila (tras perder la carrera).</summary>
    Task ReloadItemAsync(OrderItem item, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Resultado del reclamo atómico de platos por un runner (ADR-027).</summary>
public enum ClaimOutcome
{
    /// <summary>Los N platos pedidos pasaron a "en entrega" y el viaje quedó creado.</summary>
    Claimed,
    /// <summary>Al menos un plato ya no estaba "listo": otro runner se adelantó. Nada se persistió.</summary>
    Conflict,
    /// <summary>El runner ya tenía un viaje abierto (índice único parcial). Nada se persistió.</summary>
    TripAlreadyOpen
}

/// <summary>Una línea del manifiesto del viaje, con el puesto y su posición en el mapa.</summary>
public sealed record TripManifestRow(
    OrderItem Item,
    string? TableCode,
    Guid? FloorId,
    double? PosX,
    double? PosY,
    string? Observacion,
    string? CustomerName = null,
    int Priority = 0);

/// <summary>Viajes de entrega: reclamo atómico, manifiesto y cierre (ADR-027).</summary>
public interface IDeliveryTripRepository
{
    Task<DeliveryTrip?> GetByIdAsync(Guid tripId, CancellationToken ct = default);

    /// <summary>El viaje abierto del runner, si lo tiene. Solo puede haber uno.</summary>
    Task<DeliveryTrip?> GetOpenByRunnerAsync(Guid tenantId, Guid runnerId, CancellationToken ct = default);

    /// <summary>Los platos indicados, <b>sin seguimiento</b>, para validar antes de reclamar.</summary>
    Task<List<OrderItem>> ListItemsByIdsAsync(
        Guid tenantId, IReadOnlyList<Guid> itemIds, CancellationToken ct = default);

    /// <summary>
    /// <b>Reclamo atómico</b> (ADR-027). Crea el viaje y mueve los platos de "listo" a
    /// "en entrega" con una actualización condicional en base de datos
    /// (<c>UPDATE … WHERE status = 'READY'</c>), comparando las filas afectadas con las pedidas.
    /// Si no coinciden se revierte <b>toda</b> la transacción: no existen viajes parciales.
    /// </summary>
    Task<ClaimOutcome> TryClaimItemsAsync(
        DeliveryTrip trip, IReadOnlyList<Guid> itemIds, CancellationToken ct = default);

    /// <summary>Manifiesto del viaje: sus platos con puesto y coordenadas, para el mapa del runner.</summary>
    Task<List<TripManifestRow>> ListManifestAsync(Guid tripId, CancellationToken ct = default);

    /// <summary>
    /// Los pedidos que tienen platos en el viaje, con todos sus ítems cargados y <b>con
    /// seguimiento</b>: es lo que permite cerrar el viaje y derivar el estado de cada comanda.
    /// </summary>
    Task<List<Order>> ListOrdersByTripAsync(Guid tripId, CancellationToken ct = default);

    /// <summary>
    /// <b>Cierre condicional</b> del viaje: pasa de abierto al estado indicado con la condición
    /// dentro del UPDATE. Devuelve <c>false</c> si ya no estaba abierto — es decir, si otro
    /// dispositivo lo cerró o lo liberó entre medias. Sin esto, cerrar y liberar el mismo viaje a
    /// la vez dejaría los platos en dos sitios: entregados y de vuelta en el pase.
    /// </summary>
    Task<bool> TryCloseAsync(
        Guid tripId, DeliveryTripStatus target, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
