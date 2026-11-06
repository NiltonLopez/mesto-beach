using FluentValidation;
using static MestoBeach.Orders.Application.Queries.ItemDtoMapping;
using MediatR;
using MestoBeach.Orders.Application.Commands;
using MestoBeach.Orders.Application.Common;
using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Enums;
using MestoBeach.Orders.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Orders.Application.Queries;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record OrderItemDto(
    Guid Id,
    string CatalogItemId,
    string ItemNameSnapshot,
    int ItemPriceSnapshotCents,
    int Quantity,
    string? Notes,
    bool LoyaltyRedeemed,
    string? SelectedModifiers = null,
    // Ciclo por ítem (ADR-025): la interfaz necesita el estado de CADA plato para pintar las dos
    // colas del KDS y los platos de otras estaciones en gris. Opcionales para no romper a los
    // clientes actuales, que ignoran lo que no conocen.
    string? Status = null,
    string? Timing = null,
    Guid? StationId = null,
    // Sellos por estado: el KDS pinta cuánto llevó el plato en cada paso (suyo y de la otra
    // estación). El tramo de un paso es la diferencia con el siguiente sello; el del paso actual
    // se calcula en vivo contra "ahora".
    DateTime? QueuedAt = null,
    DateTime? PreparationStartedAt = null,
    DateTime? ReadyAt = null,
    DateTime? DeliveredAt = null);

public record OrderDetailDto(
    Guid Id,
    Guid SessionId,
    Guid TenantId,
    Guid ParticipantId,
    string Status,
    Guid? StationId,
    string? CancellationReason,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    List<OrderItemDto> Items,
    string? CustomerName = null,
    string? Observacion = null);

public record OrderSummaryDto(
    Guid Id,
    Guid SessionId,
    Guid ParticipantId,
    string Status,
    Guid? StationId,
    string? CancellationReason,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    int ItemCount,
    int TotalCents,
    List<OrderItemDto> Items,
    string? CustomerName = null,
    string? Observacion = null);

// ─── GET /api/v1/orders/{orderId} ─────────────────────────────────────────────

public record GetOrderQuery(
    Guid OrderId,
    Guid? CallerTenantId,
    Guid? CallerParticipantId,
    string CallerType)          // "staff" | "customer" | "kds"
    : IRequest<Result<OrderDetailDto>>;

public sealed class GetOrderValidator : AbstractValidator<GetOrderQuery>
{
    public GetOrderValidator() =>
        RuleFor(q => q.OrderId).NotEmpty();
}

public sealed class GetOrderHandler : IRequestHandler<GetOrderQuery, Result<OrderDetailDto>>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<GetOrderHandler> _logger;

    public GetOrderHandler(IOrderRepository orders, ILogger<GetOrderHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result<OrderDetailDto>> Handle(GetOrderQuery q, CancellationToken ct)
    {
        var order = await _orders.GetByIdWithItemsAsync(q.OrderId, ct);
        if (order is null)
            return Result.Failure<OrderDetailDto>(Error.NotFound("Orden no encontrada."));

        // Tenant check for staff / KDS
        if (q.CallerType is "staff" or "kds")
        {
            if (q.CallerTenantId.HasValue && q.CallerTenantId.Value != order.TenantId)
                return Result.Failure<OrderDetailDto>(
                    Error.Forbidden("No tiene acceso a órdenes de otro tenant."));
        }

        // Customer: can only see their own order
        if (q.CallerType == "customer")
        {
            if (q.CallerParticipantId != order.ParticipantId)
                return Result.Failure<OrderDetailDto>(
                    Error.Forbidden("No puede ver órdenes de otro participante."));
        }

        _logger.LogInformation(
            "order.fetched {OrderId} {TenantId} {RequestedBy} {Role}",
            order.Id, order.TenantId, q.CallerParticipantId ?? q.CallerTenantId, q.CallerType);

        return Result.Success(ToDto(order));
    }

    private static OrderDetailDto ToDto(Domain.Entities.Order o) =>
        new(o.Id, o.SessionId, o.TenantId, o.ParticipantId,
            o.Status.ToString().ToUpperInvariant().Replace("INPREPARATION", "IN_PREPARATION"),
            o.StationId, o.CancellationReason, o.CreatedAt, o.ConfirmedAt,
            o.Items.Select(i => new OrderItemDto(
                i.Id, i.CatalogItemId, i.ItemNameSnapshot,
                i.ItemPriceSnapshotCents, i.Quantity, i.Notes, i.LoyaltyRedeemed,
                i.SelectedModifiers, ItemStatusStr(i.Status), ItemTimingStr(i.Timing), i.StationId,
                i.QueuedAt, i.PreparationStartedAt, i.ReadyAt, i.DeliveredAt)).ToList(),
            CustomerName: o.CustomerName, Observacion: o.Observacion);
}

// ─── GET /api/v1/orders?sessionId= ────────────────────────────────────────────

public record ListOrdersQuery(
    Guid SessionId,
    OrderStatus? Status,
    Guid? StationId,
    bool FilterNullStation,     // true when stationId="null"
    Guid? CallerTenantId,
    Guid? CallerParticipantId,
    string CallerType)
    : IRequest<Result<ListOrdersResponse>>;

public record ListOrdersResponse(List<OrderSummaryDto> Data, int Total, bool Truncated);

public sealed class ListOrdersValidator : AbstractValidator<ListOrdersQuery>
{
    public ListOrdersValidator() =>
        RuleFor(q => q.SessionId).NotEmpty().WithMessage("sessionId es requerido.");
}

public sealed class ListOrdersHandler : IRequestHandler<ListOrdersQuery, Result<ListOrdersResponse>>
{
    private readonly IOrderRepository _orders;
    private readonly ISessionReadService _sessionRead;
    private readonly ILogger<ListOrdersHandler> _logger;

    public ListOrdersHandler(
        IOrderRepository orders,
        ISessionReadService sessionRead,
        ILogger<ListOrdersHandler> logger)
    {
        _orders = orders;
        _sessionRead = sessionRead;
        _logger = logger;
    }

    public async Task<Result<ListOrdersResponse>> Handle(ListOrdersQuery q, CancellationToken ct)
    {
        var session = await _sessionRead.GetSessionAsync(q.SessionId, ct);
        if (session is null)
            return Result.Failure<ListOrdersResponse>(Error.NotFound("Sesión no encontrada."));

        // Tenant check
        if (q.CallerType is "staff" or "kds")
        {
            if (q.CallerTenantId.HasValue && q.CallerTenantId.Value != session.TenantId)
                return Result.Failure<ListOrdersResponse>(
                    Error.Forbidden("No tiene acceso a órdenes de otro tenant."));
        }

        // Customer: must be participant in the session
        if (q.CallerType == "customer" && q.CallerParticipantId.HasValue)
        {
            var participant = await _sessionRead.GetParticipantAsync(
                q.CallerParticipantId.Value, q.SessionId, ct);
            if (participant is null)
                return Result.Failure<ListOrdersResponse>(
                    Error.Forbidden("No es participante de esta sesión."));
        }

        var orders = await _orders.ListBySessionAsync(
            q.SessionId, q.Status, q.StationId, q.FilterNullStation, ct);

        // Cap at 200; set truncated header
        bool truncated = orders.Count > 200;
        if (truncated) orders = orders.Take(200).ToList();

        var dtos = orders.Select(o =>
        {
            int totalCents = o.Items.Sum(i => i.ItemPriceSnapshotCents * i.Quantity);
            return new OrderSummaryDto(
                o.Id, o.SessionId, o.ParticipantId,
                StatusString(o.Status),
                o.StationId, o.CancellationReason,
                o.CreatedAt, o.ConfirmedAt,
                o.Items.Count, totalCents,
                o.Items.Select(i => new OrderItemDto(
                    i.Id, i.CatalogItemId, i.ItemNameSnapshot,
                    i.ItemPriceSnapshotCents, i.Quantity, i.Notes, i.LoyaltyRedeemed,
                    i.SelectedModifiers, ItemStatusStr(i.Status), ItemTimingStr(i.Timing),
                    i.StationId, i.QueuedAt, i.PreparationStartedAt, i.ReadyAt, i.DeliveredAt)).ToList(),
                CustomerName: o.CustomerName, Observacion: o.Observacion);
        }).ToList();

        _logger.LogInformation(
            "orders.listed {SessionId} {TenantId} {RequestedBy} {Role} {ResultCount}",
            q.SessionId, session.TenantId,
            q.CallerParticipantId ?? q.CallerTenantId,
            q.CallerType, dtos.Count);

        return Result.Success(new ListOrdersResponse(dtos, dtos.Count, truncated));
    }

    private static string StatusString(OrderStatus s) =>
        s == OrderStatus.InPreparation ? "IN_PREPARATION" : s.ToString().ToUpperInvariant();
}

// ─── GET /api/v1/orders/active — KDS authoritative board ──────────────────────
// Source of truth for the kitchen: ALL active orders of the tenant (optionally a
// station) in one query, with table code + readyAt. The KDS polls this so a lost
// SignalR event can never hide a plate for more than one poll cycle.

public record KdsOrderDto(
    Guid Id,
    Guid SessionId,
    Guid TenantId,
    Guid ParticipantId,
    string Status,
    Guid? StationId,
    string? TableCode,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    DateTime? ReadyAt,
    List<OrderItemDto> Items,
    DateTime UpdatedAt,
    string? CustomerName = null,
    string? Observacion = null,
    // PRD-019 / ADR-038: snapshots de la comanda, al final y con default para no romper clientes.
    int Priority = 0,
    string VenueMode = "RESTAURANT");

/// <summary>
/// KDS board payload. <c>Data</c> are the orders to upsert (all active when no cursor,
/// or only those changed since the cursor). <c>ActiveIds</c> is the authoritative set of
/// currently-active order ids so the client can prune anything that left the board
/// (delivered/cancelled). <c>ServerTime</c> is the cursor to send back next poll (P2).
/// </summary>
public record ActiveOrdersResponse(
    List<KdsOrderDto> Data, int Total, List<Guid> ActiveIds, DateTime ServerTime);

public record ListActiveOrdersQuery(
    Guid TenantId,
    Guid? StationId,
    bool FilterNullStation,
    DateTime? Since = null)
    : IRequest<Result<ActiveOrdersResponse>>;

public sealed class ListActiveOrdersValidator : AbstractValidator<ListActiveOrdersQuery>
{
    public ListActiveOrdersValidator() =>
        RuleFor(q => q.TenantId).NotEmpty().WithMessage("tenantId es requerido.");
}

public sealed class ListActiveOrdersHandler
    : IRequestHandler<ListActiveOrdersQuery, Result<ActiveOrdersResponse>>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<ListActiveOrdersHandler> _logger;

    public ListActiveOrdersHandler(IOrderRepository orders, ILogger<ListActiveOrdersHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result<ActiveOrdersResponse>> Handle(ListActiveOrdersQuery q, CancellationToken ct)
    {
        // Capture the cursor BEFORE reading: any write after this lands in the next
        // poll (overlap is harmless — upserts are idempotent), nothing is missed.
        var serverTime = DateTime.UtcNow;

        var rows = await _orders.ListActiveByTenantAsync(q.TenantId, q.StationId, q.FilterNullStation, ct);

        // Snapshot backstop: the full active-id set lets the client prune orders that
        // already left the board even if their "left" event was missed.
        var activeIds = rows.Select(r => r.Order.Id).ToList();

        // Delta: with a cursor, only return orders that changed since it. Un cambio de plato que no
        // altera el estado derivado del pedido no mueve orders.updated_at: también entra el pedido
        // si alguno de sus platos se movió (order_items.updated_at, SDD-004 «Cursor del delta»).
        var changed = q.Since is { } since
            ? rows.Where(r => r.Order.UpdatedAt > since || r.Order.Items.Any(i => i.UpdatedAt > since))
            : rows;

        var dtos = changed.Select(r => new KdsOrderDto(
            r.Order.Id, r.Order.SessionId, r.Order.TenantId, r.Order.ParticipantId,
            r.Order.Status == OrderStatus.InPreparation ? "IN_PREPARATION" : r.Order.Status.ToString().ToUpperInvariant(),
            r.Order.StationId, r.TableCode,
            r.Order.CreatedAt, r.Order.ConfirmedAt, r.Order.ReadyAt,
            r.Order.Items.Select(i => new OrderItemDto(
                i.Id, i.CatalogItemId, i.ItemNameSnapshot,
                i.ItemPriceSnapshotCents, i.Quantity, i.Notes, i.LoyaltyRedeemed,
                i.SelectedModifiers, ItemStatusStr(i.Status), ItemTimingStr(i.Timing), i.StationId,
                i.QueuedAt, i.PreparationStartedAt, i.ReadyAt, i.DeliveredAt)).ToList(),
            r.Order.UpdatedAt,
            CustomerName: r.Order.CustomerName, Observacion: r.Order.Observacion,
            Priority: r.Order.Priority, VenueMode: r.Order.VenueMode)).ToList();

        _logger.LogInformation(
            "orders.active.listed {TenantId} {StationId} {Changed}/{Active} since={Since}",
            q.TenantId, q.StationId, dtos.Count, activeIds.Count, q.Since);

        return Result.Success(new ActiveOrdersResponse(dtos, activeIds.Count, activeIds, serverTime));
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ENTREGA POR VIAJES (ADR-027) — las dos lecturas del runner
// ═══════════════════════════════════════════════════════════════════════════════
//
// La cola es una lista plana —el runner elige qué carga—; el viaje va agrupado por puesto,
// que es exactamente lo que pinta el mapa-manifiesto. En ambas, el puesto se identifica por su
// CÓDIGO (es único por tenant y es lo que está impreso en el QR de la hamaca, ADR-030).

// ─── GET /api/v1/delivery-queue?stationId= ────────────────────────────────────

/// <summary>Un plato listo esperando runner, con su puesto y su comanda.</summary>
public record DeliveryQueueItemDto(
    Guid ItemId,
    Guid OrderId,
    Guid SessionId,
    Guid? StationId,
    string Name,
    int Quantity,
    string? Notes,
    string? SelectedModifiers,
    DateTime? ReadyAt,
    string? TableCode,
    Guid? FloorId,
    double? PosX,
    double? PosY,
    string? CustomerName,
    string? Observacion,
    // PRD-019 / ADR-038: snapshot orders.priority (> 0 = «Reservado»).
    int Priority = 0);

/// <summary>
/// <c>ServerTime</c> se captura <b>antes</b> de leer, igual que en el tablero KDS: la cola
/// también se sondea y el socket es solo un aviso de baja latencia (ADR-022).
/// </summary>
public record DeliveryQueueResponse(
    List<DeliveryQueueItemDto> Data, int Total, DateTime ServerTime);

/// <summary>
/// Cola de entrega de una estación (RF-1830): los ítems <c>READY</c> que ningún runner ha
/// reclamado todavía. La estación es la que tiene <b>adscrita</b> el runner; esa adscripción
/// vive en el personal (módulo Identity), así que la resuelve quien invoca la consulta.
/// </summary>
public record GetDeliveryQueueQuery(Guid TenantId, Guid StationId)
    : IRequest<Result<DeliveryQueueResponse>>;

public sealed class GetDeliveryQueueValidator : AbstractValidator<GetDeliveryQueueQuery>
{
    public GetDeliveryQueueValidator()
    {
        RuleFor(q => q.TenantId).NotEmpty().WithMessage("tenantId es requerido.");
        RuleFor(q => q.StationId).NotEmpty().WithMessage("stationId es requerido.");
    }
}

public sealed class GetDeliveryQueueHandler
    : IRequestHandler<GetDeliveryQueueQuery, Result<DeliveryQueueResponse>>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<GetDeliveryQueueHandler> _logger;

    public GetDeliveryQueueHandler(IOrderRepository orders, ILogger<GetDeliveryQueueHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result<DeliveryQueueResponse>> Handle(
        GetDeliveryQueueQuery q, CancellationToken ct)
    {
        var serverTime = DateTime.UtcNow;

        var filas = await _orders.ListReadyItemsByStationAsync(q.TenantId, q.StationId, ct);

        var dtos = filas.Select(r => new DeliveryQueueItemDto(
            r.Item.Id, r.Item.OrderId, r.SessionId, r.Item.StationId,
            r.Item.ItemNameSnapshot, r.Item.Quantity, r.Item.Notes, r.Item.SelectedModifiers,
            r.Item.ReadyAt, r.TableCode, r.FloorId, r.PosX, r.PosY,
            r.CustomerName, r.Observacion, r.Priority)).ToList();

        _logger.LogInformation(
            "delivery_queue.listed {TenantId} {StationId} {ItemCount}",
            q.TenantId, q.StationId, dtos.Count);

        return Result.Success(new DeliveryQueueResponse(dtos, dtos.Count, serverTime));
    }
}

// ─── GET /api/v1/delivery-trips/mine ──────────────────────────────────────────
//
// El cuerpo es el MISMO que devuelve POST /delivery-trips (así lo fija el SDD), así que esta
// consulta reutiliza el DTO del comando que crea el viaje —DeliveryTripDto— en lugar de definir
// una segunda forma que habría que mantener sincronizada a mano. Lo que vive aquí es el armado,
// que es de lectura pura y lo comparten los dos endpoints.

/// <summary>
/// Arma la respuesta de un viaje. La comparten <c>POST /delivery-trips</c> (al reclamar) y
/// <c>GET /delivery-trips/mine</c> (al recuperarse de un cierre de app o un corte de red).
/// </summary>
public static class DeliveryTripMapping
{
    /// <summary>
    /// Agrupa el manifiesto <b>por puesto</b>: el runner no razona en platos sueltos, razona en
    /// paradas. Delega en <c>CreateDeliveryTripHandler.BuildStops</c> (priority DESC, luego código
    /// de puesto): un único armado para el 201 de POST y GET /mine.
    /// </summary>
    public static DeliveryTripDto ToDto(DeliveryTrip trip, IReadOnlyList<TripManifestRow> manifiesto) =>
        new(trip.Id,
            trip.StationId,
            OrderItemNaming.Wire(trip.Status),
            trip.CreatedAt,
            CreateDeliveryTripHandler.BuildStops(manifiesto)); // armado único de paradas (SDD-004 v2.2)
}

/// <summary>
/// El viaje abierto del runner (RF-1832). Es la fuente de verdad de la ruta: si la app se
/// cierra o se cae la red a mitad del reparto, el manifiesto se recupera de aquí (ADR-024).
/// Devuelve <c>null</c> —que la API traduce a 204— cuando el runner no lleva bandeja.
/// </summary>
public record GetMyDeliveryTripQuery(Guid TenantId, Guid RunnerId)
    : IRequest<Result<DeliveryTripDto?>>;

public sealed class GetMyDeliveryTripValidator : AbstractValidator<GetMyDeliveryTripQuery>
{
    public GetMyDeliveryTripValidator()
    {
        RuleFor(q => q.TenantId).NotEmpty().WithMessage("tenantId es requerido.");
        RuleFor(q => q.RunnerId).NotEmpty().WithMessage("runnerId es requerido.");
    }
}

public sealed class GetMyDeliveryTripHandler
    : IRequestHandler<GetMyDeliveryTripQuery, Result<DeliveryTripDto?>>
{
    private readonly IDeliveryTripRepository _trips;
    private readonly ILogger<GetMyDeliveryTripHandler> _logger;

    public GetMyDeliveryTripHandler(
        IDeliveryTripRepository trips, ILogger<GetMyDeliveryTripHandler> logger)
    {
        _trips = trips;
        _logger = logger;
    }

    public async Task<Result<DeliveryTripDto?>> Handle(GetMyDeliveryTripQuery q, CancellationToken ct)
    {
        var viaje = await _trips.GetOpenByRunnerAsync(q.TenantId, q.RunnerId, ct);
        if (viaje is null)
        {
            _logger.LogInformation(
                "delivery_trip.none_open {TenantId} {RunnerId}", q.TenantId, q.RunnerId);
            return Result.Success<DeliveryTripDto?>(null);
        }

        var manifiesto = await _trips.ListManifestAsync(viaje.Id, ct);
        var dto = DeliveryTripMapping.ToDto(viaje, manifiesto);

        _logger.LogInformation(
            "delivery_trip.fetched {TripId} {TenantId} {RunnerId} {StopCount} {ItemCount}",
            dto.TripId, q.TenantId, q.RunnerId, dto.Stops.Count, manifiesto.Count);

        return Result.Success<DeliveryTripDto?>(dto);
    }
}

// ─── GET /api/v1/kds-stations ─────────────────────────────────────────────────

public record KdsStationDto(
    Guid Id, Guid TenantId, string Name,
    List<string> CategoryIds, DateTime CreatedAt,
    bool IsActive, string FlowProfile, List<string> ActiveIn, bool IsOn)
{
    /// <summary><c>isOn</c> se calcula con el modo vigente al responder (ADR-037); no se guarda.</summary>
    public static KdsStationDto From(KdsStation s, VenueMode mode) => new(
        s.Id, s.TenantId, s.Name, s.CategoryIds, s.CreatedAt,
        s.IsActive, s.FlowProfile, s.ActiveIn, s.IsOnIn(mode));
}

public record ListKdsStationsQuery(
    Guid TenantId,
    Guid CallerTenantId,
    Guid RequesterId,
    string CallerType) : IRequest<Result<ListKdsStationsResponse>>;

public record ListKdsStationsResponse(List<KdsStationDto> Data, int Total);

public sealed class ListKdsStationsValidator : AbstractValidator<ListKdsStationsQuery>
{
    public ListKdsStationsValidator() =>
        RuleFor(q => q.TenantId).NotEmpty().WithMessage("tenantId es requerido.");
}

public sealed class ListKdsStationsHandler
    : IRequestHandler<ListKdsStationsQuery, Result<ListKdsStationsResponse>>
{
    private readonly IKdsStationRepository _stations;
    private readonly IVenueModeProvider _venueMode;
    private readonly ILogger<ListKdsStationsHandler> _logger;

    public ListKdsStationsHandler(
        IKdsStationRepository stations, IVenueModeProvider venueMode, ILogger<ListKdsStationsHandler> logger)
    {
        _stations = stations;
        _venueMode = venueMode;
        _logger = logger;
    }

    public async Task<Result<ListKdsStationsResponse>> Handle(
        ListKdsStationsQuery q, CancellationToken ct)
    {
        if (q.TenantId != q.CallerTenantId)
            return Result.Failure<ListKdsStationsResponse>(
                Error.Forbidden("tenantId no coincide con el del token."));

        var stations = await _stations.ListByTenantAsync(q.TenantId, ct);
        // Una sola lectura del modo por petición; las apagadas se listan igual (isOn: false).
        var mode = await _venueMode.GetAsync(q.TenantId, ct);
        var dtos = stations
            .OrderBy(s => s.CreatedAt)
            .Select(s => KdsStationDto.From(s, mode))
            .ToList();

        _logger.LogInformation(
            "kds_stations.listed {TenantId} {Count} {RequestedBy} {Role}",
            q.TenantId, dtos.Count, q.RequesterId, q.CallerType);

        return Result.Success(new ListKdsStationsResponse(dtos, dtos.Count));
    }
}


// ─── Traducción de los estados del plato a la representación de la API ───────

internal static class ItemDtoMapping
{
    public static string ItemStatusStr(OrderItemStatus s) => s switch
    {
        OrderItemStatus.Held          => "HELD",
        OrderItemStatus.Queued        => "QUEUED",
        OrderItemStatus.InPreparation => "IN_PREPARATION",
        OrderItemStatus.Ready         => "READY",
        OrderItemStatus.InDelivery    => "IN_DELIVERY",
        OrderItemStatus.Delivered     => "DELIVERED",
        OrderItemStatus.Returned      => "RETURNED",
        OrderItemStatus.Cancelled     => "CANCELLED",
        _ => s.ToString().ToUpperInvariant()
    };

    public static string ItemTimingStr(ItemTiming t) => t == ItemTiming.Later ? "LATER" : "NOW";
}
