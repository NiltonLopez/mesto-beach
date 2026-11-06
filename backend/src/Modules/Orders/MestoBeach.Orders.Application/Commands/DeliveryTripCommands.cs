using FluentValidation;
using MediatR;
using MestoBeach.Orders.Application.Common;
using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Enums;
using MestoBeach.Orders.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Orders.Application.Commands;

// ══════════════════════════════════════════════════════════════════════════════
// VIAJES DE ENTREGA (ADR-027)
//
// El runner arma una bandeja con platos "listos" de su estación, la reparte por varios
// puestos y la cierra de una vez. Lo delicado es el reclamo: varios runners compiten por la
// misma cola y ningún plato puede salir dos veces. Por eso la transición a "en entrega" no se
// hace en memoria sino con una actualización condicional en base de datos
// (IDeliveryTripRepository.TryClaimItemsAsync).
// ══════════════════════════════════════════════════════════════════════════════

// ─── DTOs ─────────────────────────────────────────────────────────────────────
//
// El manifiesto del viaje vive aquí, en el comando que lo crea, y GET /delivery-trips/mine lo
// reutiliza tal cual: el SDD fija el MISMO cuerpo para los dos endpoints, así que definirlo dos
// veces solo serviría para que se desincronizaran.

public record DeliveryTripStopItemDto(
    Guid ItemId,
    Guid OrderId,
    string Name,
    int Quantity,
    string? Notes,
    string? SelectedModifiers,
    string Status,
    string? Observacion = null,
    // PRD-019: nombre del comensal (null = comanda del personal; la app la rotula
    // «Comanda de sala · {tableCode}») y snapshot orders.priority (> 0 = «Reservado»).
    string? CustomerName = null,
    int Priority = 0);

/// <summary>Una parada de la ruta: el puesto, dónde está en el mapa y qué se le lleva.</summary>
public record DeliveryTripStopDto(
    string? TableCode,
    Guid? FloorId,
    double? PosX,
    double? PosY,
    List<DeliveryTripStopItemDto> Items);

/// <summary>
/// El manifiesto del viaje, ya agrupado por puesto: es exactamente lo que pinta el mapa del
/// runner, sin que el cliente tenga que cruzar datos.
/// </summary>
public record DeliveryTripDto(
    Guid TripId,
    Guid? StationId,
    string Status,
    DateTime CreatedAt,
    List<DeliveryTripStopDto> Stops);

public record CompleteDeliveryTripDto(
    Guid TripId,
    string Status,
    int Delivered,
    int Returned,
    List<Guid> FinalizedOrderIds);

public record ReleaseDeliveryTripDto(
    Guid TripId,
    string Status,
    int ReleasedItems);

/// <summary>Errores de los viajes de entrega, con los códigos exactos del SDD-004 v2.0.</summary>
internal static class TripErrors
{
    public static Error NotFound() =>
        new("TRIP_NOT_FOUND", "El viaje no existe o no es tuyo.", ErrorType.NotFound);

    public static Error NotOpen() =>
        new("TRIP_NOT_OPEN", "El viaje ya se cerró o se liberó.", ErrorType.Conflict);

    public static Error AlreadyOpen() =>
        new("TRIP_ALREADY_OPEN", "Ya tienes un viaje sin cerrar; ciérralo antes de armar otro.",
            ErrorType.Conflict);

    public static Error AlreadyClaimed() =>
        new("ITEM_ALREADY_CLAIMED",
            "Otro runner se adelantó: al menos uno de esos platos ya no está en el pase.",
            ErrorType.Conflict);

    // Red de seguridad: el 422 que pide el SDD lo devuelve el controlador, que sí puede contar
    // los ids sin tocar la base. Si alguien llama al comando por otra vía, aquí sale como 400.
    public static Error TooManyItems(int max) =>
        new("TOO_MANY_ITEMS", $"Un viaje admite como mucho {max} platos.", ErrorType.Validation);

    // DESVIACIÓN CONOCIDA: el SDD pide 422 y sale 400. Esta no se puede resolver en el borde
    // —hay que leer los platos del viaje para saberlo— y la taxonomía compartida (ErrorType)
    // no tiene 422 al que mapear.
    public static Error ItemNotInTrip() =>
        new("ITEM_NOT_IN_TRIP", "Alguno de los platos devueltos no pertenece a este viaje.",
            ErrorType.Validation);
}

// ══════════════════════════════════════════════════════════════════════════════
// ARMAR LA BANDEJA — POST /api/v1/delivery-trips
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Reclamo atómico de los platos que el runner carga en su bandeja.
/// </summary>
/// <param name="RunnerStationId">
/// Estación a la que está adscrito el runner (ADR-027). Mientras Identity no modele esa
/// adscripción llega <c>null</c>, y entonces la estación del viaje se deduce de los propios
/// platos —que tienen que ser todos de la misma—.
/// </param>
/// <param name="CallerRoles">
/// Lista completa de roles del token, si el llamador la tiene. Opcional: con
/// <paramref name="CallerRole"/> basta.
/// </param>
public record CreateDeliveryTripCommand(
    IReadOnlyList<Guid> ItemIds,
    Guid CallerTenantId,
    Guid RunnerId,
    string? CallerRole,
    Guid? RunnerStationId = null,
    IReadOnlyList<string>? CallerRoles = null) : IRequest<Result<DeliveryTripDto>>;

public sealed class CreateDeliveryTripValidator : AbstractValidator<CreateDeliveryTripCommand>
{
    public CreateDeliveryTripValidator()
    {
        RuleFor(c => c.CallerTenantId).NotEmpty();
        RuleFor(c => c.RunnerId).NotEmpty();
        RuleFor(c => c.ItemIds)
            .NotNull().WithMessage("itemIds es requerido.")
            .Must(ids => ids is { Count: > 0 }).WithMessage("itemIds no puede estar vacío.")
            .Must(ids => ids is null || ids.All(id => id != Guid.Empty))
                .WithMessage("Cada itemId debe ser un UUID válido.")
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
                .WithMessage("itemIds no puede traer repetidos.");
        // El tope de 30 lo aplica el handler, para devolver el código TOO_MANY_ITEMS del SDD
        // en lugar del VALIDATION_ERROR genérico del pipeline.
    }
}

public sealed class CreateDeliveryTripHandler
    : IRequestHandler<CreateDeliveryTripCommand, Result<DeliveryTripDto>>
{
    /// <summary>Tope de platos por bandeja (SDD-004 v2.0).</summary>
    public const int MaxItemsPerTrip = 30;

    private readonly IDeliveryTripRepository _trips;
    private readonly IOrderSignalRNotifier _signalR;
    private readonly ILogger<CreateDeliveryTripHandler> _logger;

    public CreateDeliveryTripHandler(
        IDeliveryTripRepository trips,
        IOrderSignalRNotifier signalR,
        ILogger<CreateDeliveryTripHandler> logger)
    {
        _trips = trips;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<DeliveryTripDto>> Handle(
        CreateDeliveryTripCommand cmd, CancellationToken ct)
    {
        var roles = OrderStaffRoles.Merge(cmd.CallerRoles, cmd.CallerRole);
        if (!OrderStaffRoles.Has(roles, OrderStaffRoles.Runner, OrderStaffRoles.Admin))
            return Result.Failure<DeliveryTripDto>(ItemErrors.ForbiddenRole(
                "Solo un runner o Administración pueden armar un viaje de entrega."));

        var ids = cmd.ItemIds.Distinct().ToList();
        if (ids.Count > MaxItemsPerTrip)
            return Result.Failure<DeliveryTripDto>(TripErrors.TooManyItems(MaxItemsPerTrip));

        // ── Un solo viaje abierto por runner ──────────────────────────────────
        // Comprobación amable; la garantía de verdad es el índice único parcial, que se
        // controla también más abajo por si dos dispositivos del mismo runner van a la vez.
        var abierto = await _trips.GetOpenByRunnerAsync(cmd.CallerTenantId, cmd.RunnerId, ct);
        if (abierto is not null)
            return Result.Failure<DeliveryTripDto>(TripErrors.AlreadyOpen());

        // ── Validación previa (mensajes útiles, no garantía) ──────────────────
        var items = await _trips.ListItemsByIdsAsync(cmd.CallerTenantId, ids, ct);
        if (items.Count != ids.Count)
            return Result.Failure<DeliveryTripDto>(ItemErrors.NotFound());

        // Un viaje sale de una sola estación (ADR-027).
        var estaciones = items.Select(i => i.StationId).Distinct().ToList();
        if (estaciones.Count > 1)
            return Result.Failure<DeliveryTripDto>(ItemErrors.WrongStation());

        var estacion = estaciones[0];
        if (cmd.RunnerStationId.HasValue && cmd.RunnerStationId != estacion)
            return Result.Failure<DeliveryTripDto>(ItemErrors.WrongStation());

        var noListos = items.Where(i => i.Status != OrderItemStatus.Ready).ToList();
        if (noListos.Count > 0)
            return Result.Failure<DeliveryTripDto>(TripErrors.AlreadyClaimed());

        // ── Reclamo atómico ──────────────────────────────────────────────────
        // Guid.Empty representa "sin estación": DeliveryTrip.StationId no es anulable y hay
        // locales sin estaciones configuradas todavía. Se traduce de vuelta a null al salir.
        var trip = new DeliveryTrip(
            Guid.NewGuid(), cmd.CallerTenantId, cmd.RunnerId, estacion ?? Guid.Empty);

        var outcome = await _trips.TryClaimItemsAsync(trip, ids, ct);
        if (outcome == ClaimOutcome.TripAlreadyOpen)
            return Result.Failure<DeliveryTripDto>(TripErrors.AlreadyOpen());

        if (outcome == ClaimOutcome.Conflict)
        {
            // Si esto pasa a menudo, la cola de runners necesita repensarse (métrica del SDD).
            _logger.LogWarning(
                "delivery_claim_conflict {TenantId} {RunnerId} {StationId} {ItemCount}",
                cmd.CallerTenantId, cmd.RunnerId, estacion, ids.Count);
            return Result.Failure<DeliveryTripDto>(TripErrors.AlreadyClaimed());
        }

        // ── Estado derivado de las comandas afectadas ────────────────────────
        // "Listo" y "en entrega" pesan igual en la tabla de prioridades, así que normalmente no
        // cambia nada; se recalcula igual para que la derivación viva en un solo sitio.
        await RecalculateOrdersAsync(trip.Id, ct);

        var manifiesto = await _trips.ListManifestAsync(trip.Id, ct);
        var dto = new DeliveryTripDto(
            trip.Id,
            estacion,
            OrderItemNaming.Wire(trip.Status),
            trip.CreatedAt,
            BuildStops(manifiesto));

        _logger.LogInformation(
            "delivery_trip_created {TripId} {TenantId} {RunnerId} {StationId} {ItemCount} {StopCount}",
            trip.Id, cmd.CallerTenantId, cmd.RunnerId, estacion, ids.Count, dto.Stops.Count);

        await NotifyClaimedAsync(cmd.CallerTenantId, estacion, trip.Id, cmd.RunnerId, manifiesto, ct);

        return Result.Success(dto);
    }

    private async Task RecalculateOrdersAsync(Guid tripId, CancellationToken ct)
    {
        var orders = await _trips.ListOrdersByTripAsync(tripId, ct);
        var cambios = orders.Count(o => o.RecalculateStatusFromItems());
        if (cambios > 0) await _trips.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Agrupa el manifiesto <b>por puesto</b>: el runner no razona en platos sueltos, razona en
    /// paradas. (v2.2, ADR-038) Salen por <c>priority DESC</c> —la mayor prioridad entre los
    /// platos de la parada— y luego por código de puesto, para que la lista sea estable entre
    /// recargas; el orden del recorrido lo decide el runner, no el sistema. Es el ÚNICO armado de
    /// paradas: lo usan el 201 de <c>POST /delivery-trips</c> y <c>GET /delivery-trips/mine</c>
    /// (vía <c>DeliveryTripMapping.ToDto</c>), que devuelven el mismo cuerpo.
    /// </summary>
    public static List<DeliveryTripStopDto> BuildStops(IReadOnlyList<TripManifestRow> rows) =>
        // Un puesto sin código (sesión huérfana) no debe hundir la ruta: cae en su propia
        // parada sin nombre en lugar de perderse.
        rows.GroupBy(r => r.TableCode)
            .Select(g => new
            {
                Prioridad = g.Max(r => r.Priority),
                Parada = new DeliveryTripStopDto(
                    g.Key,
                    g.First().FloorId,
                    g.First().PosX,
                    g.First().PosY,
                    g.Select(r => new DeliveryTripStopItemDto(
                        r.Item.Id,
                        r.Item.OrderId,
                        r.Item.ItemNameSnapshot,
                        r.Item.Quantity,
                        r.Item.Notes,
                        r.Item.SelectedModifiers,
                        OrderItemNaming.Wire(r.Item.Status),
                        r.Observacion,
                        r.CustomerName,
                        r.Priority)).ToList())
            })
            .OrderByDescending(s => s.Prioridad)
            .ThenBy(s => s.Parada.TableCode, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Parada)
            .ToList();

    private async Task NotifyClaimedAsync(
        Guid tenantId, Guid? stationId, Guid tripId, Guid runnerId,
        IReadOnlyList<TripManifestRow> rows, CancellationToken ct)
    {
        var payload = new
        {
            type = "ITEMS_CLAIMED",
            tripId,
            runnerId,
            stationId,
            itemIds = rows.Select(r => r.Item.Id).ToList(),
            status = OrderItemNaming.Wire(OrderItemStatus.InDelivery)
        };

        try
        {
            // Al tenant y a la estación: esos platos tienen que desaparecer de la cola del
            // resto de runners en menos de dos segundos.
            if (stationId.HasValue)
                await _signalR.NotifyStationAsync(tenantId, stationId.Value, payload, ct);
            await _signalR.NotifyTenantAsync(tenantId, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "delivery_trip.signalr_claimed_failed {TripId}", tripId);
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// CERRAR LA RUTA — POST /api/v1/delivery-trips/{tripId}/complete
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Cierra el viaje: lo que sigue marcado queda entregado y lo desmarcado vuelve a su estación
/// como devuelto. Es el "todo marcado, desmarca lo que falló" de la interfaz del runner.
/// </summary>
public record CompleteDeliveryTripCommand(
    Guid TripId,
    IReadOnlyList<Guid> ReturnedItemIds,
    Guid CallerTenantId,
    Guid RequesterId,
    string? CallerRole,
    IReadOnlyList<string>? CallerRoles = null) : IRequest<Result<CompleteDeliveryTripDto>>;

public sealed class CompleteDeliveryTripValidator : AbstractValidator<CompleteDeliveryTripCommand>
{
    public CompleteDeliveryTripValidator()
    {
        RuleFor(c => c.TripId).NotEmpty();
        RuleFor(c => c.CallerTenantId).NotEmpty();
        RuleFor(c => c.ReturnedItemIds).NotNull()
            .WithMessage("returnedItemIds no puede ser null (manda [] si no devuelves nada).");
    }
}

public sealed class CompleteDeliveryTripHandler
    : IRequestHandler<CompleteDeliveryTripCommand, Result<CompleteDeliveryTripDto>>
{
    private readonly IDeliveryTripRepository _trips;
    private readonly ISessionReadService _sessionRead;
    private readonly IOrderSignalRNotifier _signalR;
    private readonly ILogger<CompleteDeliveryTripHandler> _logger;

    public CompleteDeliveryTripHandler(
        IDeliveryTripRepository trips,
        ISessionReadService sessionRead,
        IOrderSignalRNotifier signalR,
        ILogger<CompleteDeliveryTripHandler> logger)
    {
        _trips = trips;
        _sessionRead = sessionRead;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<CompleteDeliveryTripDto>> Handle(
        CompleteDeliveryTripCommand cmd, CancellationToken ct)
    {
        var trip = await _trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null || trip.TenantId != cmd.CallerTenantId)
            return Result.Failure<CompleteDeliveryTripDto>(TripErrors.NotFound());

        // El viaje es del runner que lo armó; Administración puede cerrarlo por él.
        var roles = OrderStaffRoles.Merge(cmd.CallerRoles, cmd.CallerRole);
        var esAdmin = OrderStaffRoles.Has(roles, OrderStaffRoles.Admin);
        if (!esAdmin && trip.RunnerId != cmd.RequesterId)
            return Result.Failure<CompleteDeliveryTripDto>(TripErrors.NotFound());

        if (!trip.IsOpen)
            return Result.Failure<CompleteDeliveryTripDto>(TripErrors.NotOpen());

        var orders = await _trips.ListOrdersByTripAsync(trip.Id, ct);

        // Se fija la lista ANTES de tocar nada: devolver un plato le borra el delivery_trip_id,
        // así que un segundo recorrido ya no lo encontraría.
        var enViaje = orders
            .SelectMany(o => o.Items
                .Where(i => i.DeliveryTripId == trip.Id && i.Status == OrderItemStatus.InDelivery)
                .Select(i => (Order: o, Item: i)))
            .ToList();

        var devueltos = cmd.ReturnedItemIds.Distinct().ToHashSet();
        var idsEnViaje = enViaje.Select(x => x.Item.Id).ToHashSet();
        if (devueltos.Any(id => !idsEnViaje.Contains(id)))
            return Result.Failure<CompleteDeliveryTripDto>(TripErrors.ItemNotInTrip());

        var entregados = new List<(Order Order, OrderItem Item)>();
        var regresados = new List<(Order Order, OrderItem Item)>();

        try
        {
            foreach (var (order, item) in enViaje)
            {
                if (devueltos.Contains(item.Id))
                {
                    // Vuelve a la cola de su estación de origen marcado como devuelto (RF-1822).
                    item.Return();
                    regresados.Add((order, item));
                }
                else
                {
                    item.MarkDelivered();
                    entregados.Add((order, item));
                }
            }

            // El cierre se gana en la base, no en memoria: si otro dispositivo cerró o liberó
            // este viaje mientras tanto, la actualización condicional no afecta filas y se
            // aborta sin haber entregado nada.
            if (!await _trips.TryCloseAsync(trip.Id, DeliveryTripStatus.Completed, ct))
                return Result.Failure<CompleteDeliveryTripDto>(TripErrors.NotOpen());

            trip.Complete();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "delivery_trip.invalid_transition {TripId}", trip.Id);
            return Result.Failure<CompleteDeliveryTripDto>(ItemErrors.InvalidTransition(ex.Message));
        }

        // ── Estado derivado y auto-finalización (ADR-025 / RF-1814) ───────────
        var finalizados = new List<Guid>();
        foreach (var order in orders)
        {
            if (order.RecalculateStatusFromItems() && order.Status == OrderStatus.Finalized)
                finalizados.Add(order.Id);
        }

        await _trips.SaveChangesAsync(ct);

        var duracion = (int)Math.Max(0, (DateTime.UtcNow - trip.CreatedAt).TotalSeconds);
        _logger.LogInformation(
            "delivery_trip_completed {TripId} {TenantId} {RunnerId} {Delivered} {Returned} {FinalizedOrders} {DurationSeconds}",
            trip.Id, trip.TenantId, trip.RunnerId,
            entregados.Count, regresados.Count, finalizados.Count, duracion);

        await NotifyAsync(trip, entregados, regresados, finalizados, orders, ct);

        return Result.Success(new CompleteDeliveryTripDto(
            trip.Id,
            OrderItemNaming.Wire(trip.Status),
            entregados.Count,
            regresados.Count,
            finalizados));
    }

    private async Task NotifyAsync(
        DeliveryTrip trip,
        List<(Order Order, OrderItem Item)> entregados,
        List<(Order Order, OrderItem Item)> regresados,
        List<Guid> finalizados,
        List<Order> orders,
        CancellationToken ct)
    {
        // Los códigos de puesto se resuelven una vez por sesión, no una por plato.
        var tableCodes = new Dictionary<Guid, string?>();
        foreach (var sessionId in orders.Select(o => o.SessionId).Distinct())
        {
            var session = await _sessionRead.GetSessionAsync(sessionId, ct);
            tableCodes[sessionId] = session?.TableCode;
        }

        try
        {
            if (entregados.Count > 0)
            {
                var payloadTenant = new
                {
                    type = "ITEMS_DELIVERED",
                    tripId = trip.Id,
                    itemIds = entregados.Select(x => x.Item.Id).ToList(),
                    status = OrderItemNaming.Wire(OrderItemStatus.Delivered)
                };
                await _signalR.NotifyTenantAsync(trip.TenantId, payloadTenant, ct);

                // El comensal ve "entregado" en su móvil: un evento por sesión, con lo suyo.
                foreach (var grupo in entregados.GroupBy(x => x.Order.SessionId))
                {
                    var payloadSesion = new
                    {
                        type = "ITEMS_DELIVERED",
                        tripId = trip.Id,
                        sessionId = grupo.Key,
                        itemIds = grupo.Select(x => x.Item.Id).ToList(),
                        orderIds = grupo.Select(x => x.Order.Id).Distinct().ToList(),
                        status = OrderItemNaming.Wire(OrderItemStatus.Delivered)
                    };
                    await _signalR.NotifySessionAsync(trip.TenantId, grupo.Key, payloadSesion, ct);
                }
            }

            // Uno por plato: la estación tiene que verlo reaparecer en su cola, señalado.
            foreach (var (order, item) in regresados)
            {
                var payload = new
                {
                    type = "ITEM_RETURNED",
                    tripId = trip.Id,
                    itemId = item.Id,
                    orderId = order.Id,
                    sessionId = order.SessionId,
                    stationId = item.StationId,
                    itemName = item.ItemNameSnapshot,
                    quantity = item.Quantity,
                    tableCode = tableCodes.GetValueOrDefault(order.SessionId),
                    status = OrderItemNaming.Wire(OrderItemStatus.Returned)
                };
                if (item.StationId.HasValue)
                    await _signalR.NotifyStationAsync(trip.TenantId, item.StationId.Value, payload, ct);
                await _signalR.NotifyTenantAsync(trip.TenantId, payload, ct);
            }

            foreach (var orderId in finalizados)
            {
                var order = orders.First(o => o.Id == orderId);
                var payload = new
                {
                    type = "ORDER_FINALIZED",
                    orderId,
                    sessionId = order.SessionId,
                    tableCode = tableCodes.GetValueOrDefault(order.SessionId),
                    status = OrderItemNaming.Wire(order.Status)
                };
                await _signalR.NotifyTenantAsync(trip.TenantId, payload, ct);
                await _signalR.NotifySessionAsync(trip.TenantId, order.SessionId, payload, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "delivery_trip.signalr_completed_failed {TripId}", trip.Id);
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// LIBERAR UN VIAJE ABANDONADO — POST /api/v1/delivery-trips/{tripId}/release
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Suelta los platos de un viaje que nadie va a cerrar —el runner terminó el turno o perdió el
/// móvil— y los devuelve a "listo" para que otro se los lleve. No es una devolución: el plato
/// nunca salió del pase, solo cambió de dueño.
/// </summary>
public record ReleaseDeliveryTripCommand(
    Guid TripId,
    Guid CallerTenantId,
    Guid RequesterId,
    string? CallerRole,
    IReadOnlyList<string>? CallerRoles = null) : IRequest<Result<ReleaseDeliveryTripDto>>;

public sealed class ReleaseDeliveryTripValidator : AbstractValidator<ReleaseDeliveryTripCommand>
{
    public ReleaseDeliveryTripValidator()
    {
        RuleFor(c => c.TripId).NotEmpty();
        RuleFor(c => c.CallerTenantId).NotEmpty();
    }
}

public sealed class ReleaseDeliveryTripHandler
    : IRequestHandler<ReleaseDeliveryTripCommand, Result<ReleaseDeliveryTripDto>>
{
    private readonly IDeliveryTripRepository _trips;
    private readonly IOrderSignalRNotifier _signalR;
    private readonly ILogger<ReleaseDeliveryTripHandler> _logger;

    public ReleaseDeliveryTripHandler(
        IDeliveryTripRepository trips,
        IOrderSignalRNotifier signalR,
        ILogger<ReleaseDeliveryTripHandler> logger)
    {
        _trips = trips;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<ReleaseDeliveryTripDto>> Handle(
        ReleaseDeliveryTripCommand cmd, CancellationToken ct)
    {
        var trip = await _trips.GetByIdAsync(cmd.TripId, ct);
        if (trip is null || trip.TenantId != cmd.CallerTenantId)
            return Result.Failure<ReleaseDeliveryTripDto>(TripErrors.NotFound());

        // Lo libera Administración o el propio runner; nadie suelta la bandeja de otro.
        var roles = OrderStaffRoles.Merge(cmd.CallerRoles, cmd.CallerRole);
        var esAdmin = OrderStaffRoles.Has(roles, OrderStaffRoles.Admin);
        if (!esAdmin && trip.RunnerId != cmd.RequesterId)
            return Result.Failure<ReleaseDeliveryTripDto>(ItemErrors.ForbiddenRole(
                "Ese viaje es de otro runner; solo Administración puede liberarlo."));

        if (!trip.IsOpen)
            return Result.Failure<ReleaseDeliveryTripDto>(TripErrors.NotOpen());

        var orders = await _trips.ListOrdersByTripAsync(trip.Id, ct);
        var enViaje = orders
            .SelectMany(o => o.Items
                .Where(i => i.DeliveryTripId == trip.Id && i.Status == OrderItemStatus.InDelivery)
                .Select(i => (Order: o, Item: i)))
            .ToList();

        try
        {
            if (!await _trips.TryCloseAsync(trip.Id, DeliveryTripStatus.Released, ct))
                return Result.Failure<ReleaseDeliveryTripDto>(TripErrors.NotOpen());

            foreach (var (_, item) in enViaje) item.ReleaseFromDelivery();
            trip.Release();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "delivery_trip.invalid_release {TripId}", trip.Id);
            return Result.Failure<ReleaseDeliveryTripDto>(ItemErrors.InvalidTransition(ex.Message));
        }

        foreach (var order in orders) order.RecalculateStatusFromItems();
        await _trips.SaveChangesAsync(ct);

        _logger.LogInformation(
            "delivery_trip_released {TripId} {TenantId} {RunnerId} {ReleasedBy} {ItemCount}",
            trip.Id, trip.TenantId, trip.RunnerId, cmd.RequesterId, enViaje.Count);

        await NotifyAsync(trip, enViaje, ct);

        return Result.Success(new ReleaseDeliveryTripDto(
            trip.Id, OrderItemNaming.Wire(trip.Status), enViaje.Count));
    }

    private async Task NotifyAsync(
        DeliveryTrip trip, List<(Order Order, OrderItem Item)> liberados, CancellationToken ct)
    {
        var payload = new
        {
            type = "ITEMS_RELEASED",
            tripId = trip.Id,
            runnerId = trip.RunnerId,
            stationId = trip.StationId == Guid.Empty ? (Guid?)null : trip.StationId,
            itemIds = liberados.Select(x => x.Item.Id).ToList(),
            status = OrderItemNaming.Wire(OrderItemStatus.Ready)
        };

        try
        {
            // Reaparecen en la cola de la estación para cualquier runner.
            if (trip.StationId != Guid.Empty)
                await _signalR.NotifyStationAsync(trip.TenantId, trip.StationId, payload, ct);
            await _signalR.NotifyTenantAsync(trip.TenantId, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "delivery_trip.signalr_released_failed {TripId}", trip.Id);
        }
    }
}
