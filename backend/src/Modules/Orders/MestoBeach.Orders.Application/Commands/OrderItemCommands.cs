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
// CICLO DE VIDA POR ÍTEM — COURSING (ADR-025 / ADR-026)
//
// Estos comandos operan sobre el PLATO. Conviven con los comandos por pedido de
// OrderCommands.cs, que siguen alimentando el tablero KDS vigente; el estado del pedido
// pasa a derivarse de sus platos tras cada acción (Order.RecalculateStatusFromItems).
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Roles del personal que gobiernan el ciclo por ítem. <b>PDA</b> y <b>Runner</b> todavía no
/// existen en <c>StaffRole</c> (Identity los añade en su propia fase, RF-1830), por eso aquí se
/// comparan como cadenas: el token trae los roles de la membresía y esta capa solo decide.
/// </summary>
public static class OrderStaffRoles
{
    public const string Admin  = "Admin";
    public const string Pda    = "PDA";
    public const string Runner = "Runner";
    public const string Kds    = "KDS";

    public static bool Has(IReadOnlyList<string>? roles, params string[] any) =>
        roles is not null &&
        roles.Any(r => any.Any(a => string.Equals(r, a, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Une la lista completa de roles del token con el rol principal. No siempre coinciden
    /// según cómo se emitiera el JWT, y quien solo tenga a mano <c>ICurrentUser.Role</c> debe
    /// poder llamar igual: así el handler ve lo mismo que vería <c>ICurrentUser.HasRole</c>.
    /// </summary>
    public static IReadOnlyList<string> Merge(IReadOnlyList<string>? roles, string? role)
    {
        var todos = roles is null ? new List<string>() : new List<string>(roles);
        if (!string.IsNullOrWhiteSpace(role) &&
            !todos.Contains(role, StringComparer.OrdinalIgnoreCase))
            todos.Add(role);
        return todos;
    }
}

/// <summary>Nombres de estado tal y como viajan por la API y por SignalR (SCREAMING_SNAKE).</summary>
public static class OrderItemNaming
{
    public static string Wire(OrderItemStatus status) => status switch
    {
        OrderItemStatus.InPreparation => "IN_PREPARATION",
        OrderItemStatus.InDelivery    => "IN_DELIVERY",
        _                             => status.ToString().ToUpperInvariant()
    };

    public static string Wire(OrderStatus status) => status switch
    {
        OrderStatus.InPreparation => "IN_PREPARATION",
        _                         => status.ToString().ToUpperInvariant()
    };

    public static string Wire(DeliveryTripStatus status) => status.ToString().ToUpperInvariant();
}

/// <summary>Errores del ciclo por ítem, con los códigos exactos del SDD-004 v2.0.</summary>
internal static class ItemErrors
{
    public static Error NotFound() =>
        new("ITEM_NOT_FOUND", "El plato no existe o pertenece a otro local.", ErrorType.NotFound);

    public static Error InvalidTransition(string detalle) =>
        new("INVALID_TRANSITION", detalle, ErrorType.Conflict);

    public static Error SessionClosed() =>
        new("SESSION_CLOSED", "La sesión de la mesa ya se cerró.", ErrorType.Conflict);

    public static Error ForbiddenRole(string detalle) =>
        new("FORBIDDEN_ROLE", detalle, ErrorType.Forbidden);

    public static Error WrongStation() =>
        new("WRONG_STATION", "El plato pertenece a otra estación.", ErrorType.Forbidden);
}

// ══════════════════════════════════════════════════════════════════════════════
// MARCHAR UN PLATO — POST /api/v1/orders/items/{itemId}/march
// ══════════════════════════════════════════════════════════════════════════════

public record MarchOrderItemDto(
    Guid ItemId,
    Guid OrderId,
    string Status,
    Guid? StationId,
    DateTime MarchedAt);

/// <summary>
/// Marcha un plato retenido: <c>HELD → QUEUED</c>, es decir, lo mete en la <b>cola</b> de su
/// estación. Lo pueden hacer la <b>cocina</b> (KDS) y el <b>PDA</b> / Administración; el comensal
/// nunca. Encender los fuegos es una decisión distinta —la orden de preparación— y esa es solo
/// de la estación.
/// </summary>
public record MarchOrderItemCommand(
    Guid ItemId,
    Guid TenantId,
    Guid StaffId,
    IReadOnlyList<string> CallerRoles) : IRequest<Result<MarchOrderItemDto>>;

public sealed class MarchOrderItemValidator : AbstractValidator<MarchOrderItemCommand>
{
    public MarchOrderItemValidator()
    {
        RuleFor(c => c.ItemId).NotEmpty();
        RuleFor(c => c.TenantId).NotEmpty();
    }
}

public sealed class MarchOrderItemHandler
    : IRequestHandler<MarchOrderItemCommand, Result<MarchOrderItemDto>>
{
    private readonly IOrderItemRepository _items;
    private readonly ISessionReadService _sessionRead;
    private readonly IOrderSignalRNotifier _signalR;
    private readonly ILogger<MarchOrderItemHandler> _logger;

    public MarchOrderItemHandler(
        IOrderItemRepository items,
        ISessionReadService sessionRead,
        IOrderSignalRNotifier signalR,
        ILogger<MarchOrderItemHandler> logger)
    {
        _items = items;
        _sessionRead = sessionRead;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<MarchOrderItemDto>> Handle(
        MarchOrderItemCommand cmd, CancellationToken ct)
    {
        // ── Rol ───────────────────────────────────────────────────────────────
        // Se comprueba antes de leer nada: quien no puede marchar tampoco tiene por qué
        // averiguar si el plato existe. La cocina entra porque marchar ya solo mete el plato en
        // la cola —no enciende fuegos—, y el pase necesita poder adelantar lo que ve venir.
        if (!OrderStaffRoles.Has(cmd.CallerRoles,
                OrderStaffRoles.Pda, OrderStaffRoles.Kds, OrderStaffRoles.Admin))
            return Result.Failure<MarchOrderItemDto>(ItemErrors.ForbiddenRole(
                "Solo cocina, un PDA o Administración pueden marchar un plato."));

        var order = await _items.GetOrderByItemIdAsync(cmd.ItemId, cmd.TenantId, ct);
        if (order is null)
            return Result.Failure<MarchOrderItemDto>(ItemErrors.NotFound());

        var item = order.Items.First(i => i.Id == cmd.ItemId);

        // ── La mesa sigue viva ────────────────────────────────────────────────
        var session = await _sessionRead.GetSessionAsync(order.SessionId, ct);
        if (session is null || !session.IsOpen)
            return Result.Failure<MarchOrderItemDto>(ItemErrors.SessionClosed());

        if (item.Status != OrderItemStatus.Held)
            return Result.Failure<MarchOrderItemDto>(ItemErrors.InvalidTransition(
                $"El plato «{item.ItemNameSnapshot}» no está en espera: " +
                $"está {OrderItem.Describe(item.Status)}."));

        item.March(cmd.StaffId);
        var estadoPedidoCambio = order.RecalculateStatusFromItems();
        await _items.SaveChangesAsync(ct);

        var marchedAt = item.MarchedAt!.Value;
        // Aproximación: el ítem no lleva sello propio de creación, así que la espera se mide
        // desde que se creó la comanda. Sirve para la métrica de atención del PDA.
        var heldForSeconds = (int)Math.Max(0, (marchedAt - order.CreatedAt).TotalSeconds);

        _logger.LogInformation(
            "order_item_marched {ItemId} {OrderId} {TenantId} {StationId} {MarchedBy} {HeldForSeconds} {OrderStatusChanged}",
            item.Id, order.Id, order.TenantId, item.StationId, cmd.StaffId,
            heldForSeconds, estadoPedidoCambio);

        await NotifyAsync(order, item, session.TableCode, marchedAt, ct);

        return Result.Success(new MarchOrderItemDto(
            item.Id, order.Id, OrderItemNaming.Wire(item.Status), item.StationId, marchedAt));
    }

    private async Task NotifyAsync(
        Order order, OrderItem item, string? tableCode, DateTime marchedAt, CancellationToken ct)
    {
        var payload = new
        {
            type = "ITEM_MARCHED",
            itemId = item.Id,
            orderId = order.Id,
            sessionId = order.SessionId,
            stationId = item.StationId,
            status = OrderItemNaming.Wire(item.Status),
            orderStatus = OrderItemNaming.Wire(order.Status),
            itemName = item.ItemNameSnapshot,
            tableCode,
            marchedAt
        };

        try
        {
            // Tablero de la estación, tablero del local y el móvil del comensal.
            if (item.StationId.HasValue)
                await _signalR.NotifyStationAsync(order.TenantId, item.StationId.Value, payload, ct);
            await _signalR.NotifyTenantAsync(order.TenantId, payload, ct);
            await _signalR.NotifySessionAsync(order.TenantId, order.SessionId, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "order_item.signalr_march_failed {ItemId}", item.Id);
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// DAR LA ORDEN DE PREPARACIÓN — POST /api/v1/orders/items/{itemId}/start-preparation
//
// El "¡a preparar!" del pase: el encargado de cocina saca el plato de la cola y lo pone en los
// fuegos (QUEUED → IN_PREPARATION). Marchar y dar la orden son decisiones distintas y de manos
// distintas: marchar lo mete en la cola (cocina o PDA), la orden la da SOLO la estación.
// ══════════════════════════════════════════════════════════════════════════════

public record StartItemPreparationDto(
    Guid ItemId,
    Guid OrderId,
    string Status,
    Guid? StationId,
    DateTime PreparationStartedAt);

/// <summary>
/// La estación da la orden de preparación: <c>QUEUED → IN_PREPARATION</c>. Solo cocina (KDS) o
/// Administración; el ritmo de los fuegos lo marca quien cocina.
/// </summary>
/// <param name="CallerStationId">
/// Estación del token (los tokens de dispositivo KDS la traen). Si viene, tiene que coincidir
/// con la del plato; Administración se salta la comprobación.
/// </param>
public record StartItemPreparationCommand(
    Guid ItemId,
    Guid TenantId,
    Guid StaffId,
    IReadOnlyList<string> CallerRoles,
    Guid? CallerStationId = null) : IRequest<Result<StartItemPreparationDto>>;

public sealed class StartItemPreparationValidator : AbstractValidator<StartItemPreparationCommand>
{
    public StartItemPreparationValidator()
    {
        RuleFor(c => c.ItemId).NotEmpty();
        RuleFor(c => c.TenantId).NotEmpty();
    }
}

public sealed class StartItemPreparationHandler
    : IRequestHandler<StartItemPreparationCommand, Result<StartItemPreparationDto>>
{
    private readonly IOrderItemRepository _items;
    private readonly ISessionReadService _sessionRead;
    private readonly IOrderSignalRNotifier _signalR;
    private readonly ILogger<StartItemPreparationHandler> _logger;

    public StartItemPreparationHandler(
        IOrderItemRepository items,
        ISessionReadService sessionRead,
        IOrderSignalRNotifier signalR,
        ILogger<StartItemPreparationHandler> logger)
    {
        _items = items;
        _sessionRead = sessionRead;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<StartItemPreparationDto>> Handle(
        StartItemPreparationCommand cmd, CancellationToken ct)
    {
        if (!OrderStaffRoles.Has(cmd.CallerRoles, OrderStaffRoles.Kds, OrderStaffRoles.Admin))
            return Result.Failure<StartItemPreparationDto>(ItemErrors.ForbiddenRole(
                "Solo la cocina o Administración pueden dar la orden de preparación."));

        var order = await _items.GetOrderByItemIdAsync(cmd.ItemId, cmd.TenantId, ct);
        if (order is null)
            return Result.Failure<StartItemPreparationDto>(ItemErrors.NotFound());

        var item = order.Items.First(i => i.Id == cmd.ItemId);

        // Cada plato es de una sola estación (ADR-029): quien no sea Admin solo manda en lo suyo.
        if (cmd.CallerStationId.HasValue
            && item.StationId != cmd.CallerStationId
            && !OrderStaffRoles.Has(cmd.CallerRoles, OrderStaffRoles.Admin))
            return Result.Failure<StartItemPreparationDto>(ItemErrors.WrongStation());

        var session = await _sessionRead.GetSessionAsync(order.SessionId, ct);
        if (session is null || !session.IsOpen)
            return Result.Failure<StartItemPreparationDto>(ItemErrors.SessionClosed());

        if (item.Status != OrderItemStatus.Queued)
            return Result.Failure<StartItemPreparationDto>(ItemErrors.InvalidTransition(
                $"El plato «{item.ItemNameSnapshot}» no está en cola: " +
                $"está {OrderItem.Describe(item.Status)}."));

        item.StartPreparation(cmd.StaffId);
        var estadoPedidoCambio = order.RecalculateStatusFromItems();
        await _items.SaveChangesAsync(ct);

        var startedAt = item.PreparationStartedAt!.Value;
        // Cuánto esperó en la cola: es la métrica del ritmo del pase.
        var queuedForSeconds = item.QueuedAt is { } q
            ? (int)Math.Max(0, (startedAt - q).TotalSeconds)
            : 0;

        _logger.LogInformation(
            "order_item_preparation_started {ItemId} {OrderId} {TenantId} {StationId} {OrderedBy} {QueuedForSeconds} {OrderStatusChanged}",
            item.Id, order.Id, order.TenantId, item.StationId, cmd.StaffId,
            queuedForSeconds, estadoPedidoCambio);

        await NotifyAsync(order, item, session.TableCode, startedAt, ct);

        return Result.Success(new StartItemPreparationDto(
            item.Id, order.Id, OrderItemNaming.Wire(item.Status), item.StationId, startedAt));
    }

    private async Task NotifyAsync(
        Order order, OrderItem item, string? tableCode, DateTime startedAt, CancellationToken ct)
    {
        var payload = new
        {
            type = "ITEM_PREPARATION_STARTED",
            itemId = item.Id,
            orderId = order.Id,
            sessionId = order.SessionId,
            stationId = item.StationId,
            status = OrderItemNaming.Wire(item.Status),
            orderStatus = OrderItemNaming.Wire(order.Status),
            itemName = item.ItemNameSnapshot,
            tableCode,
            startedAt
        };

        try
        {
            // La otra estación también mira: ve en vivo que este plato entró en fuegos (ADR-029).
            if (item.StationId.HasValue)
                await _signalR.NotifyStationAsync(order.TenantId, item.StationId.Value, payload, ct);
            await _signalR.NotifyTenantAsync(order.TenantId, payload, ct);
            await _signalR.NotifySessionAsync(order.TenantId, order.SessionId, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "order_item.signalr_start_preparation_failed {ItemId}", item.Id);
        }
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// DAR UN PLATO POR LISTO — POST /api/v1/orders/items/{itemId}/ready
// ══════════════════════════════════════════════════════════════════════════════

public record MarkOrderItemReadyDto(
    Guid ItemId,
    Guid OrderId,
    string Status,
    Guid? StationId,
    DateTime ReadyAt);

/// <summary>
/// La estación da el plato por listo: <c>IN_PREPARATION → READY</c> y entra en la cola de
/// runners de esa estación.
/// </summary>
/// <param name="CallerStationId">
/// Estación del token (los tokens de dispositivo KDS la traen). Si viene, tiene que coincidir
/// con la del plato; Administración se salta la comprobación.
/// </param>
public record MarkOrderItemReadyCommand(
    Guid ItemId,
    Guid TenantId,
    Guid StaffId,
    IReadOnlyList<string> CallerRoles,
    Guid? CallerStationId) : IRequest<Result<MarkOrderItemReadyDto>>;

public sealed class MarkOrderItemReadyValidator : AbstractValidator<MarkOrderItemReadyCommand>
{
    public MarkOrderItemReadyValidator()
    {
        RuleFor(c => c.ItemId).NotEmpty();
        RuleFor(c => c.TenantId).NotEmpty();
    }
}

public sealed class MarkOrderItemReadyHandler
    : IRequestHandler<MarkOrderItemReadyCommand, Result<MarkOrderItemReadyDto>>
{
    private readonly IOrderItemRepository _items;
    private readonly IKdsStationRepository _stations;
    private readonly ISessionReadService _sessionRead;
    private readonly IOrderSignalRNotifier _signalR;
    private readonly ILogger<MarkOrderItemReadyHandler> _logger;

    public MarkOrderItemReadyHandler(
        IOrderItemRepository items,
        IKdsStationRepository stations,
        ISessionReadService sessionRead,
        IOrderSignalRNotifier signalR,
        ILogger<MarkOrderItemReadyHandler> logger)
    {
        _items = items;
        _stations = stations;
        _sessionRead = sessionRead;
        _signalR = signalR;
        _logger = logger;
    }

    public async Task<Result<MarkOrderItemReadyDto>> Handle(
        MarkOrderItemReadyCommand cmd, CancellationToken ct)
    {
        var esAdmin = OrderStaffRoles.Has(cmd.CallerRoles, OrderStaffRoles.Admin);
        var esKds = OrderStaffRoles.Has(cmd.CallerRoles, OrderStaffRoles.Kds);

        // Un token de dispositivo KDS no lleva roles, lleva estación: esa estación ES su permiso.
        if (!esAdmin && !esKds && !cmd.CallerStationId.HasValue)
            return Result.Failure<MarkOrderItemReadyDto>(ItemErrors.ForbiddenRole(
                "Solo la estación (KDS) o Administración pueden dar un plato por listo."));

        var order = await _items.GetOrderByItemIdAsync(cmd.ItemId, cmd.TenantId, ct);
        if (order is null)
            return Result.Failure<MarkOrderItemReadyDto>(ItemErrors.NotFound());

        var item = order.Items.First(i => i.Id == cmd.ItemId);

        // Cada plato es de una sola estación (ADR-029): nadie da por listo lo de otro.
        if (!esAdmin && cmd.CallerStationId.HasValue && cmd.CallerStationId != item.StationId)
            return Result.Failure<MarkOrderItemReadyDto>(ItemErrors.WrongStation());

        var fromStatus = item.Status;

        // Perfil de flujo de la estación del plato (ADR-037): se lee de kds_stations, no del
        // token, para que un cambio de perfil valga sin volver a entrar.
        var flowProfile = KdsStation.FullPass;
        if (item.StationId is Guid stationId)
        {
            var station = await _stations.GetByIdAsync(stationId, ct);
            if (station is not null && station.TenantId == order.TenantId)
                flowProfile = station.FlowProfile;
        }
        var desdeLaCola = fromStatus == OrderItemStatus.Queued && flowProfile == KdsStation.Bar;

        // Vale desde preparación y también desde devuelto: cuando un runner no pudo entregar un
        // plato, vuelve a la cola de su estación y esta lo repone en el pase sin rehacer la comanda.
        // Sin esta segunda puerta, un plato devuelto se queda muerto y su comanda no se finaliza nunca.
        // En una barra (BAR) vale además desde la cola (QUEUED → READY, ReadyFromQueue).
        if (!desdeLaCola && item.Status is not (OrderItemStatus.InPreparation or OrderItemStatus.Returned))
            return Result.Failure<MarkOrderItemReadyDto>(ItemErrors.InvalidTransition(
                $"El plato «{item.ItemNameSnapshot}» no está en preparación ni devuelto: " +
                $"está {OrderItem.Describe(item.Status)}."));

        var desde = item.MarchedAt ?? order.ConfirmedAt ?? order.CreatedAt;

        bool estadoPedidoCambio;
        if (desdeLaCola)
        {
            // Mecánica única (SDD-004 §«Perfil de flujo BAR»): UPDATE condicional + reload en
            // transacción. En una barra con dos tablets el doble clic es la norma.
            var (applied, changed) = await _items.TryReadyFromQueueAsync(
                order, item, cmd.StaffId, DateTime.UtcNow, ct);
            if (!applied)
            {
                await _items.ReloadItemAsync(item, ct);
                _logger.LogInformation(
                    "order_item_ready_race {ItemId} {OrderId} {TenantId} {StationId} {Status}",
                    item.Id, order.Id, order.TenantId, item.StationId, item.Status);
                return Result.Failure<MarkOrderItemReadyDto>(ItemErrors.InvalidTransition(
                    $"El plato «{item.ItemNameSnapshot}» ya no está en cola: " +
                    $"está {OrderItem.Describe(item.Status)}."));
            }
            estadoPedidoCambio = changed;
        }
        else
        {
            item.MarkReady();
            estadoPedidoCambio = order.RecalculateStatusFromItems();
            await _items.SaveChangesAsync(ct);
        }

        var readyAt = item.ReadyAt!.Value;
        var prepSeconds = (int)Math.Max(0, (readyAt - desde).TotalSeconds);

        _logger.LogInformation(
            "order_item_ready {ItemId} {OrderId} {TenantId} {StationId} {PrepSeconds} {OrderStatusChanged} {FromStatus} {FlowProfile}",
            item.Id, order.Id, order.TenantId, item.StationId, prepSeconds, estadoPedidoCambio,
            OrderItemNaming.Wire(fromStatus), flowProfile);

        var session = await _sessionRead.GetSessionAsync(order.SessionId, ct);
        await NotifyAsync(order, item, session?.TableCode, readyAt, ct);

        return Result.Success(new MarkOrderItemReadyDto(
            item.Id, order.Id, OrderItemNaming.Wire(item.Status), item.StationId, readyAt));
    }

    private async Task NotifyAsync(
        Order order, OrderItem item, string? tableCode, DateTime readyAt, CancellationToken ct)
    {
        var payload = new
        {
            type = "ITEM_READY",
            itemId = item.Id,
            orderId = order.Id,
            sessionId = order.SessionId,
            stationId = item.StationId,
            status = OrderItemNaming.Wire(item.Status),
            orderStatus = OrderItemNaming.Wire(order.Status),
            itemName = item.ItemNameSnapshot,
            quantity = item.Quantity,
            tableCode,
            readyAt
        };

        try
        {
            // Al tenant entero: lo consumen la cola de runners de la estación y las demás
            // estaciones, que lo ven en gris actualizado (ADR-029).
            if (item.StationId.HasValue)
                await _signalR.NotifyStationAsync(order.TenantId, item.StationId.Value, payload, ct);
            await _signalR.NotifyTenantAsync(order.TenantId, payload, ct);
            await _signalR.NotifySessionAsync(order.TenantId, order.SessionId, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "order_item.signalr_ready_failed {ItemId}", item.Id);
        }
    }
}
