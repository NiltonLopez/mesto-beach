namespace MestoBeach.Orders.Domain.Enums;

/// <summary>
/// Estado del pedido (comanda). Desde ADR-025 este estado se <b>deriva</b> de sus ítems
/// (ver <c>Order.RecalculateStatusFromItems</c>); se conserva porque el tablero KDS, la caja
/// y el cliente de API vigentes razonan por pedido.
/// </summary>
public enum OrderStatus
{
    Pending,
    Confirmed,
    InPreparation,
    Ready,
    Delivered,
    Cancelled,

    /// <summary>
    /// Auto-finalización (ADR-025 / RF-1814): todos los ítems están entregados y ninguno
    /// quedó retenido. Se añade al final del enum a propósito para no desplazar los valores
    /// ordinales de los anteriores.
    /// </summary>
    Finalized
}

/// <summary>
/// Ciclo de vida de un plato (ADR-025 / RF-1810). El plato, no el pedido, es la unidad de estado.
/// Recorrido normal: <c>Held → InPreparation → Ready → InDelivery → Delivered</c>,
/// más <c>Returned</c> (el runner no pudo entregarlo) y <c>Cancelled</c> (anulado).
/// </summary>
public enum OrderItemStatus
{
    /// <summary>En espera: el comensal lo pidió "luego"; solo el PDA puede marcharlo (ADR-026).</summary>
    Held,
    /// <summary>En preparación: está en la cola activa de su estación.</summary>
    InPreparation,
    /// <summary>Listo: en el pase, disponible para que un runner lo reclame.</summary>
    Ready,
    /// <summary>En entrega: reclamado por un runner y cargado en su bandeja (viaje abierto).</summary>
    InDelivery,
    /// <summary>Entregado en el puesto del comensal. Estado terminal.</summary>
    Delivered,
    /// <summary>Devuelto: el runner no pudo entregarlo y vuelve a la cola de su estación de origen.</summary>
    Returned,
    /// <summary>Anulado. Estado terminal; se ignora al derivar el estado del pedido.</summary>
    Cancelled,

    /// <summary>
    /// En cola: el plato está marchado y espera a que el encargado de cocina dé la <b>orden de
    /// preparación</b>. Es el paso intermedio entre "en espera" y "en preparación": marchar mete
    /// el plato en la cola de su estación; la orden de preparación es la que enciende los fuegos.
    /// Se añade al final del enum a propósito para no desplazar los ordinales anteriores.
    /// </summary>
    Queued
}

/// <summary>
/// Coursing (ADR-026 / RF-1811): al añadir el producto el comensal elige cuándo lo quiere.
/// <c>Now</c> entra directo en preparación; <c>Later</c> queda retenido a la espera del PDA.
/// </summary>
public enum ItemTiming
{
    Now,
    Later
}

/// <summary>
/// Estado de un viaje de entrega (ADR-027). Un runner solo puede tener un viaje <c>Open</c>
/// a la vez (índice único parcial sobre <c>delivery_trips.runner_id</c>).
/// </summary>
public enum DeliveryTripStatus
{
    /// <summary>Abierto: el runner está en ruta con los platos reclamados.</summary>
    Open,
    /// <summary>Cerrado por el runner: lo entregado quedó entregado y lo demás devuelto.</summary>
    Completed,
    /// <summary>Liberado: se soltaron los platos de un viaje abandonado y volvieron a la cola.</summary>
    Released
}
