using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Enums;

namespace MestoBeach.Orders.Domain.Repositories;

/// <summary>
/// Un plato esperando runner en el pase (ADR-027): la línea, la comanda de la que sale y el
/// puesto al que va, con sus coordenadas. Es el gemelo de
/// <see cref="TripManifestRow"/> —que es lo mismo pero ya cargado en una bandeja— y comparte
/// con él la convención de identificar el puesto por su <b>código</b>.
/// <para>
/// <c>TableCode</c> viene de la <b>sesión</b> (siempre está); las coordenadas vienen del
/// <b>puesto</b> y pueden ser nulas si nadie lo ha colocado todavía en el plano.
/// </para>
/// </summary>
public sealed record DeliveryQueueRow(
    OrderItem Item,
    Guid SessionId,
    string? TableCode,
    Guid? FloorId,
    double? PosX,
    double? PosY,
    string? CustomerName,
    string? Observacion,
    int Priority = 0);

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid orderId, CancellationToken ct = default);
    Task<Order?> GetByIdWithItemsAsync(Guid orderId, CancellationToken ct = default);

    Task<List<Order>> ListBySessionAsync(
        Guid sessionId,
        OrderStatus? status,
        Guid? stationId,
        bool filterNullStation,   // true when stationId query param is the literal "null"
        CancellationToken ct = default);

    /// <summary>
    /// KDS authoritative read: ALL active orders (Confirmed/InPreparation/Ready) for a tenant
    /// in one query, with items and the table code resolved from the session read-model.
    /// This is the source of truth the KDS polls — sockets are only a latency hint.
    /// </summary>
    Task<List<(Order Order, string? TableCode)>> ListActiveByTenantAsync(
        Guid tenantId,
        Guid? stationId,
        bool filterNullStation,
        CancellationToken ct = default);

    /// <summary>
    /// Cola del pase (RF-1830): los platos <c>listos</c> de una estación que ningún runner ha
    /// reclamado, con su puesto y su comanda. Es lo que el runner mira antes de armar bandeja.
    /// Orden FIFO por <c>ready_at</c>: el que lleva más tiempo en el pase sale primero.
    /// <para>
    /// Vive aquí y no en <see cref="IDeliveryTripRepository"/> porque es una <b>lectura</b>, no
    /// una operación sobre viajes: cuando se consulta todavía no hay viaje ninguno.
    /// </para>
    /// </summary>
    Task<List<DeliveryQueueRow>> ListReadyItemsByStationAsync(
        Guid tenantId, Guid stationId, CancellationToken ct = default);

    Task AddAsync(Order order, CancellationToken ct = default);

    Task<bool> HasActiveOrdersForStationAsync(Guid stationId, CancellationToken ct = default);

    /// <summary>For Sessions module integration: checks if session has active orders.</summary>
    Task<bool> HasActiveOrdersForSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Returns total item count + total in euro cents for a session (used by GetSession in Sessions).</summary>
    Task<(int TotalItems, int TotalCents, int PendingItems, int DeliveredItems)>
        GetSessionSummaryAsync(Guid sessionId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
