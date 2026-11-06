using FluentValidation;
using MediatR;
using MestoBeach.Orders.Application.Common;
using MestoBeach.Orders.Domain.Entities;
using MestoBeach.Orders.Domain.Enums;
using MestoBeach.Orders.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Orders.Application.Commands;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record OrderItemRequest(
    string CatalogItemId,
    int Quantity,
    string? Notes,
    bool LoyaltyRedeemed = false,
    List<Guid>? SelectedOptionIds = null,
    // Coursing (RF-1811): "NOW" o "LATER". Ausente equivale a "NOW", que es lo que mandan
    // todos los clientes anteriores al ciclo por ítem.
    string? Timing = null);

// ══════════════════════════════════════════════════════════════════════════════
// CREATE ORDER
// ══════════════════════════════════════════════════════════════════════════════

public record CreateOrderDto(
    Guid Id, Guid SessionId, Guid TenantId, Guid ParticipantId,
    string Status, Guid? StationId, string? CancellationReason,
    DateTime CreatedAt, DateTime? ConfirmedAt,
    List<Queries.OrderItemDto> Items,
    string? Observacion = null);

public record CreateOrderCommand(
    Guid SessionId,
    Guid ParticipantId,
    List<OrderItemRequest> Items,
    string? Observacion,
    Guid CallerTenantId,
    Guid CallerParticipantId) : IRequest<Result<CreateOrderDto>>;

public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.ParticipantId).NotEmpty();
        RuleFor(c => c.Observacion).MaximumLength(500).When(c => c.Observacion != null);
        RuleFor(c => c.Items)
            .NotEmpty().WithMessage("items no puede estar vacío.")
            .Must(i => i.Count <= 30).WithMessage("Máximo 30 ítems distintos por pedido.");
        RuleForEach(c => c.Items).ChildRules(item =>
        {
            // Catalog item ids are GUIDs (Catalog module's item `id`), not the Mongo document _id.
            item.RuleFor(i => i.CatalogItemId)
                .NotEmpty()
                .Matches(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")
                .WithMessage("catalogItemId debe ser un GUID válido.");
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, 50);
            item.RuleFor(i => i.Notes).MaximumLength(300).When(i => i.Notes != null);
            // Coursing (RF-1811). Se acepta ausente para no romper a los clientes que aún no
            // lo mandan: sin valor, el plato se pide "ahora".
            item.RuleFor(i => i.Timing)
                .Must(IsTimingValido)
                .WithMessage("timing debe ser NOW o LATER.");
        });
    }

    private static bool IsTimingValido(string? timing) =>
        string.IsNullOrWhiteSpace(timing)
        || timing.Trim().Equals("NOW", StringComparison.OrdinalIgnoreCase)
        || timing.Trim().Equals("LATER", StringComparison.OrdinalIgnoreCase);
}

public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Result<CreateOrderDto>>
{
    private static readonly TimeSpan CatalogTimeout = TimeSpan.FromSeconds(2);

    private readonly IOrderRepository _orders;
    private readonly IKdsStationRepository _stations;
    private readonly IVenueModeProvider _venueMode;
    private readonly ICatalogValidationService _catalogValidation;
    private readonly IPinValidationService _pinValidation;
    private readonly ISessionReadService _sessionRead;
    private readonly IOrderSignalRNotifier _signalR;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(
        IOrderRepository orders,
        IKdsStationRepository stations,
        IVenueModeProvider venueMode,
        ICatalogValidationService catalogValidation,
        IPinValidationService pinValidation,
        ISessionReadService sessionRead,
        IOrderSignalRNotifier signalR,
        ILogger<CreateOrderHandler> logger)
    {
        _orders = orders;
        _stations = stations;
        _venueMode = venueMode;
        _catalogValidation = catalogValidation;
        _pinValidation = pinValidation;
        _sessionRead = sessionRead;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<CreateOrderDto>> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ── Session & participant check ────────────────────────────────────────
        var session = await _sessionRead.GetSessionAsync(cmd.SessionId, ct);
        if (session is null)
            return Result.Failure<CreateOrderDto>(Error.NotFound("Sesión no encontrada."));

        // Caller-type tenant scoping:
        //  • Staff callers carry a tenantId claim (CallerTenantId != empty) → must match the
        //    session's tenant so they can't place orders into another tenant's session.
        //  • Anonymous customers (QR menu, [AllowAnonymous]) carry no tenant claim; they are
        //    implicitly scoped to the session they joined. The participant check below (the
        //    participantId issued at join) is their capability proof.
        if (cmd.CallerTenantId != Guid.Empty && session.TenantId != cmd.CallerTenantId)
            return Result.Failure<CreateOrderDto>(Error.NotFound("Sesión no encontrada."));

        if (!session.IsOpen)
            return Result.Failure<CreateOrderDto>(
                Error.Validation("SESSION_CLOSED", "La sesión no está abierta."));

        var participant = await _sessionRead.GetParticipantAsync(cmd.ParticipantId, cmd.SessionId, ct);
        if (participant is null)
            return Result.Failure<CreateOrderDto>(
                Error.NotFound("El participante no pertenece a esta sesión."));

        // ── Nombre obligatorio en NIGHT (SDD-004 §Enrutado por modo, fila d) ────
        // Comensal sin token de personal (CallerTenantId vacío: anónimo o Customer JWT) y sin
        // nombre → 422 NAME_REQUIRED. Staff JWT (PDA, Caja) exento. Antes del PIN y del catálogo.
        // Integración PR2+PR3+PR5b: el modo del local (ADR-041) se lee UNA sola vez por comanda,
        // aquí, y lo reutilizan el nombre obligatorio, la carta por modo y el enrutado por estación.
        // Si Redis y Postgres fallan a la vez → 503 VENUE_MODE_UNAVAILABLE.
        VenueMode venueMode;
        try { venueMode = await _venueMode.GetAsync(session.TenantId, ct); }
        catch (VenueModeUnavailableException ex)
        {
            _logger.LogError(ex, "order.venue_mode_unavailable {TenantId}", session.TenantId);
            return Result.Failure<CreateOrderDto>(Error.ServiceUnavailable(
                VenueModeUnavailableException.Code, "No se pudo leer el modo del local. Intenta de nuevo."));
        }

        if (cmd.CallerTenantId == Guid.Empty && string.IsNullOrWhiteSpace(participant.DisplayName))
        {
            if (venueMode == VenueMode.NIGHT)
            {
                _logger.LogInformation(
                    "order.name_required {TenantId} {SessionId} {ParticipantId}",
                    session.TenantId, cmd.SessionId, cmd.ParticipantId);
                return Result.Failure<CreateOrderDto>(Error.UnprocessableEntity(
                    "NAME_REQUIRED", "Dinos tu nombre para poder llevarte el pedido a la mesa."));
            }
        }

        // ── PIN check ──────────────────────────────────────────────────────────
        if (session.PinRequired)
        {
            bool pinOk = await _pinValidation.IsPinValidatedAsync(
                session.TenantId, cmd.SessionId, cmd.ParticipantId, participant.PinValidated, ct);

            if (!pinOk)
            {
                _logger.LogInformation(
                    "order.pin_required {SessionId} {TenantId} {ParticipantId}",
                    cmd.SessionId, session.TenantId, cmd.ParticipantId);
                return Result.Failure<CreateOrderDto>(
                    Error.Forbidden("PIN_REQUIRED"));
            }
        }

        // ── Catalog validation ─────────────────────────────────────────────────
        var itemIds = cmd.Items.Select(i => i.CatalogItemId).Distinct().ToList();
        CatalogValidationResult catalogResult;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(CatalogTimeout);

            catalogResult = await _catalogValidation.ValidateAsync(
                session.TenantId, itemIds, venueMode, timeoutCts.Token);
        }
        catch (CatalogUnavailableException)
        {
            _logger.LogWarning(
                "order.catalog_unavailable {TenantId} {CatalogLookupDurationMs}",
                session.TenantId, sw.ElapsedMilliseconds);
            return Result.Failure<CreateOrderDto>(
                Error.Failure("CATALOG_UNAVAILABLE", "El catálogo no está disponible en este momento."));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "order.catalog_unavailable {TenantId} {CatalogLookupDurationMs}",
                session.TenantId, sw.ElapsedMilliseconds);
            return Result.Failure<CreateOrderDto>(
                Error.Failure("CATALOG_UNAVAILABLE", "El catálogo no está disponible en este momento."));
        }

        // 409 y no 422: el carrito era válido cuando se armó; lo que cambió es el modo del local.
        // Se evalúa antes que INVALID_ITEMS y no se crea la orden.
        if (catalogResult.NotInModeItems.Count > 0)
        {
            var notInMode = catalogResult.NotInModeItems;
            _logger.LogWarning(
                "order.item_not_in_mode {TenantId} {SessionId} {VenueMode} {ItemIds}",
                session.TenantId, cmd.SessionId, venueMode, string.Join(",", notInMode.Select(i => i.Id)));
            var names = string.Join(", ", notInMode.Select(i => $"«{i.Name}»"));
            var modeLabel = venueMode == MestoBeach.Shared.Venue.VenueMode.NIGHT ? "de noche" : "de día";
            return Result.Failure<CreateOrderDto>(new Error(
                "ITEM_NOT_IN_MODE",
                notInMode.Count == 1
                    ? $"{names} no está en la carta {modeLabel}."
                    : $"{names} no están en la carta {modeLabel}.",
                ErrorType.Conflict,
                new Dictionary<string, object?> { ["itemIds"] = notInMode.Select(i => i.Id).ToList() }));
        }

        if (catalogResult.InvalidItemIds.Count > 0)
        {
            _logger.LogWarning(
                "order.invalid_items {TenantId} {SessionId} {InvalidItemIds}",
                session.TenantId, cmd.SessionId,
                string.Join(",", catalogResult.InvalidItemIds));
            return Result.Failure<CreateOrderDto>(
                Error.Validation("INVALID_ITEMS",
                    $"Ítems no válidos: {string.Join(", ", catalogResult.InvalidItemIds)}"));
        }

        // ── Enrutado a estación KDS, plato a plato (ADR-029 / RF-1815) ─────────
        // Cada ítem va a la estación de SU categoría: una misma comanda puede repartirse entre
        // Parrilla, Cocina y Barra. Se resuelve un mapa categoría → estación de una sola vez
        // (una consulta) y luego se aplica línea a línea.
        var categoryIds = catalogResult.ValidItems
            .Select(i => i.CategoryId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        // Un plato puede además fijar SU estación y saltarse la de la categoría (la excepción:
        // el postre flambeado que sale de parrilla). Se guarda qué estaciones existen de verdad
        // para no mandar una comanda a una estación borrada — ahí se quedaría sin tablero.
        var allStations = await _stations.GetByTenantAsync(session.TenantId, ct);
        var estacionesExistentes = allStations.Select(s => s.Id).ToHashSet();

        var stationByCategory = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var categoryId in categoryIds)
        {
            // Si varias estaciones reclaman la misma categoría (mala configuración), gana
            // la más antigua — la misma regla que había cuando la estación era del pedido.
            var match = allStations
                .Where(s => s.CategoryIds.Contains(categoryId))
                .OrderBy(s => s.CreatedAt)
                .FirstOrDefault();
            if (match is not null) stationByCategory[categoryId] = match.Id;
        }

        // ── Resolución de cada línea antes de crear la comanda ─────────────────
        // The server is authoritative on price: it resolves the item's modifier groups, validates
        // the customer's selection (required/min/max, availability, anti-tampering) and recomputes
        // unit price = base + Σ deltas. Never trust a client-sent price.
        var lineas = new List<LineaPreparada>();
        VenueMode? modoVigente = venueMode; // integración PR2+PR3: reutiliza la única lectura del modo (arriba)
        bool barraMesaLeida = false;    // la barra de la mesa (paso b) se lee una sola vez
        MestoBeach.Orders.Domain.Entities.KdsStation? barraMesa = null;
        foreach (var req in cmd.Items)
        {
            var snapshot = catalogResult.ValidItems.First(v => v.Id == req.CatalogItemId);

            var (line, modError) = ModifierResolution.Resolve(snapshot, req.SelectedOptionIds);
            if (modError is not null)
            {
                _logger.LogInformation(
                    "order.invalid_modifiers {TenantId} {SessionId} {CatalogItemId} {Error}",
                    session.TenantId, cmd.SessionId, req.CatalogItemId, modError);
                return Result.Failure<CreateOrderDto>(
                    Error.Validation("INVALID_MODIFIERS", modError));
            }

            // Manda la estación fijada en el plato; si no tiene (o apunta a una estación que ya
            // no existe), hereda la de su categoría. Así el ruteo por categoría sigue funcionando
            // y solo hay que declarar las excepciones plato a plato.
            Guid? itemStationId = null;
            if (!string.IsNullOrEmpty(snapshot.StationId)
                && Guid.TryParse(snapshot.StationId, out var propia)
                && estacionesExistentes.Contains(propia))
                itemStationId = propia;
            else if (!string.IsNullOrEmpty(snapshot.CategoryId)
                && stationByCategory.TryGetValue(snapshot.CategoryId, out var sid))
                itemStationId = sid;

            // ADR-037: una estación apagada en el modo vigente rechaza la comanda ENTERA. No se busca
            // otra estación ni se «rescata» con el reparto mesa → barra (SDD-004 paso a).
            if (itemStationId is Guid resuelta
                && allStations.FirstOrDefault(s => s.Id == resuelta) is { } estacion)
            {
                modoVigente ??= await _venueMode.GetAsync(session.TenantId, ct);
                if (!estacion.IsOnIn(modoVigente.Value))
                {
                    _logger.LogWarning(
                        "order.station_closed {TenantId} {SessionId} {Mode} {StationId} {StationName}",
                        session.TenantId, cmd.SessionId, modoVigente.Value, estacion.Id, estacion.Name);
                    OrderMetrics.RejectedByMode.Add(1,
                        new KeyValuePair<string, object?>("reason", "station_closed"));
                    return Result.Failure<CreateOrderDto>(
                        OrderErrors.StationClosed(estacion.Id, estacion.Name, modoVigente.Value));
                }
            }

            // ADR-038 / SDD-004 paso (b): override por mesa. Si la fila de la mesa en el modo
            // vigente apunta a una barra ENCENDIDA y el plato resolvió a una estación BAR (ya
            // encendida: el paso a lo garantiza) o a ninguna → gana la barra de la mesa. Un plato
            // que resuelve a FULL_PASS nunca se desvía. Solo en NIGHT: de día la barra de la fila
            // se guarda pero no se usa. Una barra borrada o apagada = «sin barra».
            bool candidato = itemStationId is null
                || allStations.FirstOrDefault(s => s.Id == itemStationId) is { IsBar: true };
            if (candidato)
            {
                modoVigente ??= await _venueMode.GetAsync(session.TenantId, ct);
                if (modoVigente.Value == VenueMode.NIGHT)
                {
                    if (!barraMesaLeida)
                    {
                        barraMesaLeida = true;
                        var barraId = await _sessionRead.GetTableStationAsync(
                            session.TableId, modoVigente.Value.ToString(), ct);
                        barraMesa = barraId is Guid bid
                            ? allStations.FirstOrDefault(s => s.Id == bid && s.IsOnIn(modoVigente.Value))
                            : null;
                    }
                    if (barraMesa is not null) itemStationId = barraMesa.Id;
                }
            }

            lineas.Add(new LineaPreparada(
                req,
                snapshot,
                line!,
                // Notes only persist when the item allows free-text comments.
                Notes: snapshot.AllowNotes ? req.Notes : null,
                StationId: itemStationId,
                Timing: ParseTiming(req.Timing)));
        }

        // Compatibilidad (ADR-025): Order.StationId está obsoleto pero el tablero KDS vigente
        // todavía razona por pedido, así que se sigue poblando con la estación del primer plato
        // que tenga una. Se toma la primera NO NULA —y no literalmente la del primer ítem—
        // porque es lo que hacía el enrutado anterior: si el primer plato no casa con ninguna
        // estación, la comanda seguía cayendo en la del siguiente que sí casara. Dejarla en null
        // la haría desaparecer del tablero filtrado por estación.
        var orderStationId = lineas.Select(l => l.StationId).FirstOrDefault(s => s.HasValue);

        // ── Persist ───────────────────────────────────────────────────────────
        var orderId = Guid.NewGuid();
        var order = new Order(orderId, cmd.SessionId, session.TenantId, cmd.ParticipantId, orderStationId);
        // Attribute to the customer (denormalized from the participant): name for the KDS/waiter,
        // accountId for the cross-tenant CRM (Platform).
        order.SetCustomer(participant.CustomerAccountId, participant.DisplayName);
        // Observación libre de toda la comanda (la escribe el cliente en el menú o el PDA).
        order.SetObservacion(cmd.Observacion);
        // PRD-019 / ADR-038 (SDD-004 §Enrutado por modo, paso c): snapshots de la comanda.
        // priority = 1 solo si el local está en NIGHT y la mesa es RESERVADO (tables.kind vía read
        // model); venue_mode = la única lectura del modo de arriba. De día no se consulta la mesa.
        var priority = 0;
        if (venueMode == VenueMode.NIGHT)
        {
            var kind = await _sessionRead.GetTableKindAsync(session.TableId, ct);
            if (string.Equals(kind, "RESERVADO", StringComparison.OrdinalIgnoreCase)) priority = 1;
        }
        order.SetVenueSnapshot(venueMode.ToString(), priority);

        var itemDtos = new List<Queries.OrderItemDto>();
        foreach (var l in lineas)
        {
            // ADR-025: el ítem nace con su propio ciclo de vida — "ahora" entra en preparación,
            // "luego" queda retenido esperando a que un PDA lo marche (ADR-026).
            var item = new OrderItem(
                Guid.NewGuid(), orderId, session.TenantId,
                l.Request.CatalogItemId, l.Snapshot.Name, l.Line.UnitPriceCents,
                l.Request.Quantity, l.Notes, l.Request.LoyaltyRedeemed, l.Line.Summary,
                timing: l.Timing,
                stationId: l.StationId);
            order.Items.Add(item);
            itemDtos.Add(new Queries.OrderItemDto(
                item.Id, item.CatalogItemId, item.ItemNameSnapshot,
                item.ItemPriceSnapshotCents, item.Quantity, item.Notes, item.LoyaltyRedeemed,
                item.SelectedModifiers));
        }

        // The customer confirming their cart IS the order confirmation — there is no separate
        // staff approval step in the QR-ordering flow (PRD-003: "Cliente confirma pedido → Nuevo",
        // y RF: "aparece en el KDS <2s después de ser confirmado por el cliente"). Advance straight
        // to CONFIRMED so it lands on the KDS "Nuevos" column immediately instead of getting stuck
        // in PENDING (which the KDS board filters out).
        order.Advance(OrderStatus.Confirmed);

        await _orders.AddAsync(order, ct);
        await _orders.SaveChangesAsync(ct);

        sw.Stop();
        _logger.LogInformation(
            "order.created {OrderId} {SessionId} {TenantId} {ParticipantId} {ItemCount} {StationId} {VenueMode} {Priority} {DurationMs}",
            orderId, cmd.SessionId, session.TenantId, cmd.ParticipantId,
            cmd.Items.Count, orderStationId, venueMode, priority, sw.ElapsedMilliseconds);

        // ── SignalR broadcast ─────────────────────────────────────────────────
        // Se avisa a TODAS las estaciones con platos en la comanda, no solo a la del pedido:
        // desde el enrutado por ítem, una comanda puede tocar varias (ADR-029).
        var estaciones = lineas
            .Where(l => l.StationId.HasValue)
            .Select(l => l.StationId!.Value)
            .Distinct()
            .ToList();

        var payload = new { type = "order.created", orderId, sessionId = cmd.SessionId };
        try
        {
            foreach (var estacion in estaciones)
                await _signalR.NotifyStationAsync(session.TenantId, estacion, payload, ct);
            await _signalR.NotifyTenantAsync(session.TenantId, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "order.signalr_notify_failed {OrderId}", orderId);
        }

        return Result.Success(new CreateOrderDto(
            orderId, cmd.SessionId, session.TenantId, cmd.ParticipantId,
            "CONFIRMED", orderStationId, null, order.CreatedAt, order.ConfirmedAt, itemDtos,
            Observacion: order.Observacion));
    }

    /// <summary>
    /// Una línea ya resuelta: precio autoritativo, notas, estación y coursing. Se calcula todo
    /// antes de instanciar la comanda porque <c>Order</c> necesita su estación —la del primer
    /// plato que tenga una— en el constructor.
    /// </summary>
    private sealed record LineaPreparada(
        OrderItemRequest Request,
        CatalogItemSnapshot Snapshot,
        ModifierResolution.LineResult Line,
        string? Notes,
        Guid? StationId,
        ItemTiming Timing);

    /// <summary>
    /// "LATER" retiene el plato; cualquier otra cosa —incluida la ausencia del campo— lo pide
    /// "ahora". El validador ya rechazó los valores que no son NOW ni LATER.
    /// </summary>
    private static ItemTiming ParseTiming(string? timing) =>
        !string.IsNullOrWhiteSpace(timing)
        && timing.Trim().Equals("LATER", StringComparison.OrdinalIgnoreCase)
            ? ItemTiming.Later
            : ItemTiming.Now;
}

// ══════════════════════════════════════════════════════════════════════════════
// UPDATE ORDER STATUS
// ══════════════════════════════════════════════════════════════════════════════

public record UpdateOrderStatusDto(Guid Id, string Status, DateTime? ConfirmedAt, DateTime UpdatedAt);

public record UpdateOrderStatusCommand(
    Guid OrderId,
    string Status,
    Guid CallerTenantId,
    string CallerType,   // "staff" | "kds"
    string? CallerRole) : IRequest<Result<UpdateOrderStatusDto>>;

public sealed class UpdateOrderStatusValidator : AbstractValidator<UpdateOrderStatusCommand>
{
    private static readonly HashSet<string> AllowedStatuses =
        ["CONFIRMED", "IN_PREPARATION", "READY", "DELIVERED"];

    public UpdateOrderStatusValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Status)
            .Must(s => AllowedStatuses.Contains(s))
            .WithMessage("status debe ser CONFIRMED, IN_PREPARATION, READY o DELIVERED.");
    }
}

public sealed class UpdateOrderStatusHandler
    : IRequestHandler<UpdateOrderStatusCommand, Result<UpdateOrderStatusDto>>
{
    private readonly IOrderRepository _orders;
    private readonly IOrderSignalRNotifier _signalR;
    private readonly ISessionReadService _sessionRead;
    private readonly ILogger<UpdateOrderStatusHandler> _logger;

    public UpdateOrderStatusHandler(
        IOrderRepository orders,
        IOrderSignalRNotifier signalR,
        ISessionReadService sessionRead,
        ILogger<UpdateOrderStatusHandler> logger)
    {
        _orders = orders;
        _signalR = signalR;
        _sessionRead = sessionRead;
        _logger = logger;
    }

    public async Task<Result<UpdateOrderStatusDto>> Handle(
        UpdateOrderStatusCommand cmd, CancellationToken ct)
    {
        // Con los platos: este endpoint es el que usa el KDS de hoy, que razona por pedido, y
        // convive con el ciclo por ítem hasta que se migre la interfaz (ADR-025). Si avanzara el
        // pedido sin arrastrar sus platos, las dos vistas del mismo servicio dirían cosas distintas.
        var order = await _orders.GetByIdWithItemsAsync(cmd.OrderId, ct);
        if (order is null)
            return Result.Failure<UpdateOrderStatusDto>(Error.NotFound("Orden no encontrada."));

        if (order.TenantId != cmd.CallerTenantId)
            return Result.Failure<UpdateOrderStatusDto>(
                Error.Forbidden("La orden pertenece a otro tenant."));

        if (order.Status == OrderStatus.Cancelled)
            return Result.Failure<UpdateOrderStatusDto>(
                Error.Gone("La orden está cancelada y no puede modificarse."));

        var target = ParseStatus(cmd.Status);

        // KDS restrictions
        if (cmd.CallerType == "kds")
        {
            if (target is OrderStatus.Confirmed or OrderStatus.Delivered)
                return Result.Failure<UpdateOrderStatusDto>(
                    Error.Forbidden("El KDS no puede ejecutar esta transición."));
        }

        if (!order.CanTransitionTo(target))
            return Result.Failure<UpdateOrderStatusDto>(
                Error.Conflict($"Transición inválida: {StatusStr(order.Status)} → {cmd.Status}."));

        var fromStatus = order.Status;
        order.Advance(target);
        ArrastrarPlatos(order, target);
        await _orders.SaveChangesAsync(ct);

        // SignalR
        var session = await _sessionRead.GetSessionAsync(order.SessionId, ct);
        if (session is not null)
        {
            var payload = new
            {
                type = "order.status_changed",
                orderId = order.Id,
                status = cmd.Status,
                tableCode = session.TableCode
            };
            try
            {
                await _signalR.NotifySessionAsync(order.TenantId, order.SessionId, payload, ct);
                await _signalR.NotifyTenantAsync(order.TenantId, payload, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "order.signalr_status_failed {OrderId}", order.Id);
            }
        }

        _logger.LogInformation(
            "order.status_changed {OrderId} {TenantId} {SessionId} {FromStatus} {ToStatus} {ChangedBy} {Role}",
            order.Id, order.TenantId, order.SessionId,
            StatusStr(fromStatus), cmd.Status, cmd.CallerTenantId, cmd.CallerType);

        return Result.Success(new UpdateOrderStatusDto(
            order.Id, cmd.Status, order.ConfirmedAt, DateTime.UtcNow));
    }

    private static OrderStatus ParseStatus(string s) => s switch
    {
        "CONFIRMED"      => OrderStatus.Confirmed,
        "IN_PREPARATION" => OrderStatus.InPreparation,
        "READY"          => OrderStatus.Ready,
        "DELIVERED"      => OrderStatus.Delivered,
        _ => throw new ArgumentException($"Unknown status: {s}")
    };

    private static string StatusStr(OrderStatus s) =>
        s == OrderStatus.InPreparation ? "IN_PREPARATION" : s.ToString().ToUpperInvariant();

    /// <summary>
    /// Arrastra los platos al avanzar el pedido desde el endpoint heredado por pedido.
    ///
    /// Mientras el KDS no se migre al ciclo por ítem (ADR-025), las dos representaciones del mismo
    /// servicio conviven. Sin este arrastre divergen: el cajero vería la comanda entregada y la cola
    /// del runner seguiría ofreciendo sus platos. Solo mueve los platos que están en un estado
    /// coherente con el salto — nunca toca los que ya lleva un runner en la bandeja, porque de esos
    /// manda su viaje.
    /// </summary>
    private static void ArrastrarPlatos(Order order, OrderStatus target)
    {
        foreach (var item in order.Items)
        {
            switch (target)
            {
                case OrderStatus.InPreparation when item.Status == OrderItemStatus.Held:
                    // El pedido entero entra en cocina: lo retenido deja de estarlo y, como el
                    // flujo antiguo no distingue cola de fuegos, se le da también la orden.
                    item.March(Guid.Empty);
                    item.StartPreparation(Guid.Empty);
                    break;
                case OrderStatus.InPreparation when item.Status == OrderItemStatus.Queued:
                    item.StartPreparation(Guid.Empty);
                    break;
                case OrderStatus.Ready when item.Status == OrderItemStatus.Queued:
                    // Atajo del flujo por pedido: pasa por los fuegos para no saltarse la máquina.
                    item.StartPreparation(Guid.Empty);
                    item.MarkReady();
                    break;
                case OrderStatus.Ready when item.Status == OrderItemStatus.InPreparation:
                    item.MarkReady();
                    break;
                case OrderStatus.Delivered when item.Status is OrderItemStatus.Ready or OrderItemStatus.Returned:
                    // Entrega registrada desde el flujo antiguo, sin pasar por un viaje de runner.
                    item.ClaimForDelivery(Guid.Empty);
                    item.MarkDelivered();
                    break;
            }
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// CANCEL ORDER
// ══════════════════════════════════════════════════════════════════════════════

public record CancelOrderDto(
    Guid Id, string Status, string CancellationReason,
    DateTime CancelledAt, Guid CancelledBy);

public record CancelOrderCommand(
    Guid OrderId,
    string Reason,
    Guid CallerTenantId,
    Guid RequesterId,
    // Quién anula: "staff" (cualquier rol del personal), "customer" (el comensal, que solo puede
    // anular SU comanda) o "anon"/otros (sin identidad válida → rechazado). El endpoint es
    // [AllowAnonymous] porque el menú deja al comensal anular su propio pedido; sin este dato el
    // handler solo comprobaba el tenant y cualquier comensal del local anulaba comandas ajenas.
    string CallerType = "staff",
    Guid? CallerParticipantId = null) : IRequest<Result<CancelOrderDto>>;

public sealed class CancelOrderValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("reason es requerido.")
            .MinimumLength(5).WithMessage("reason debe tener al menos 5 caracteres.")
            .MaximumLength(300);
    }
}

public sealed class CancelOrderHandler : IRequestHandler<CancelOrderCommand, Result<CancelOrderDto>>
{
    private readonly IOrderRepository _orders;
    private readonly IOrderSignalRNotifier _signalR;
    private readonly ISessionReadService _sessionRead;
    private readonly ILogger<CancelOrderHandler> _logger;

    public CancelOrderHandler(
        IOrderRepository orders,
        IOrderSignalRNotifier signalR,
        ISessionReadService sessionRead,
        ILogger<CancelOrderHandler> logger)
    {
        _orders = orders;
        _signalR = signalR;
        _sessionRead = sessionRead;
        _logger = logger;
    }

    public async Task<Result<CancelOrderDto>> Handle(CancelOrderCommand cmd, CancellationToken ct)
    {
        // Con los platos: cancelar la comanda tiene que cancelarlos también (ADR-025), o se
        // quedan vivos en las colas de las estaciones y el pedido no se finaliza jamás.
        var order = await _orders.GetByIdWithItemsAsync(cmd.OrderId, ct);
        if (order is null)
            return Result.Failure<CancelOrderDto>(Error.NotFound("Orden no encontrada."));

        if (order.TenantId != cmd.CallerTenantId)
            return Result.Failure<CancelOrderDto>(
                Error.Forbidden("La orden pertenece a otro tenant."));

        // Quién anula. El personal puede anular cualquier comanda del local; el comensal, solo la
        // suya; sin identidad válida (token ausente o de otro tipo), no se anula nada. El tenant ya
        // casa, pero eso no basta: el token de un comensal y el de servicio llevan tenantId.
        if (cmd.CallerType == "customer")
        {
            if (cmd.CallerParticipantId is null || cmd.CallerParticipantId != order.ParticipantId)
                return Result.Failure<CancelOrderDto>(
                    Error.Forbidden("No puede anular la comanda de otro comensal."));
        }
        else if (cmd.CallerType != "staff")
        {
            return Result.Failure<CancelOrderDto>(
                Error.Forbidden("Se requiere autenticación para anular una comanda."));
        }

        if (order.Status == OrderStatus.Cancelled)
            return Result.Failure<CancelOrderDto>(
                Error.Gone("La orden ya está cancelada."));

        if (order.Status is OrderStatus.Ready or OrderStatus.Delivered)
            return Result.Failure<CancelOrderDto>(
                Error.Conflict("No se puede cancelar una orden en estado READY o DELIVERED."));

        // Un plato que ya va en la bandeja de un runner no se anula: se entrega o se devuelve.
        // Si lo hubiera, se rechaza la cancelación entera en vez de dejar la comanda a medias.
        var enReparto = order.Items
            .Where(i => i.Status == OrderItemStatus.InDelivery)
            .Select(i => i.ItemNameSnapshot)
            .ToList();
        if (enReparto.Count > 0)
            return Result.Failure<CancelOrderDto>(Error.Conflict(
                $"Un runner ya lleva en la bandeja: {string.Join(", ", enReparto)}. " +
                "Cierra o libera su ruta antes de anular la comanda."));

        var fromStatus = order.Status;
        order.Cancel(cmd.Reason, cmd.RequesterId);
        foreach (var item in order.Items.Where(i =>
                     i.Status is not (OrderItemStatus.Delivered or OrderItemStatus.Cancelled)))
            item.CancelItem();
        await _orders.SaveChangesAsync(ct);

        // SignalR
        var session = await _sessionRead.GetSessionAsync(order.SessionId, ct);
        if (session is not null)
        {
            var payload = new { type = "order.cancelled", orderId = order.Id, reason = cmd.Reason };
            try
            {
                await _signalR.NotifySessionAsync(order.TenantId, order.SessionId, payload, ct);
                await _signalR.NotifyTenantAsync(order.TenantId, payload, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "order.signalr_cancel_failed {OrderId}", order.Id);
            }
        }

        _logger.LogInformation(
            "order.cancelled {OrderId} {TenantId} {SessionId} {CancelledBy} {FromStatus} {Reason}",
            order.Id, order.TenantId, order.SessionId, cmd.RequesterId, fromStatus, cmd.Reason);

        return Result.Success(new CancelOrderDto(
            order.Id, "CANCELLED", cmd.Reason, order.CancelledAt!.Value, cmd.RequesterId));
    }
}

// ─── STATION_CLOSED (ADR-037) ─────────────────────────────────────────────────

public static class OrderErrors
{
    public const string StationClosedCode = "STATION_CLOSED";

    /// <summary>409 con <c>stationId</c> en la raíz del ProblemDetails y el detalle legible.</summary>
    public static Error StationClosed(Guid stationId, string stationName, VenueMode mode) => new(
        StationClosedCode,
        $"La estación «{stationName}» está cerrada en modo {ModeLabel(mode)}.",
        ErrorType.Conflict,
        new Dictionary<string, object?> { ["stationId"] = stationId });

    private static string ModeLabel(VenueMode mode) => mode switch
    {
        VenueMode.NIGHT => "Night",
        VenueMode.RESTAURANT => "Restaurante",
        _ => mode.ToString()
    };
}

public static class OrderMetrics
{
    public static readonly System.Diagnostics.Metrics.Meter Meter = new("MestoBeach.Orders");
    /// <summary>tag <c>reason = item_not_in_mode | station_closed | name_required</c>.</summary>
    public static readonly System.Diagnostics.Metrics.Counter<long> RejectedByMode =
        Meter.CreateCounter<long>("orders.rejected_by_mode");
}
