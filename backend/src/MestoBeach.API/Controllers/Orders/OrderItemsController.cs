using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Orders.Application.Commands;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Orders;

/// <summary>
/// Ciclo de vida por ítem (ADR-025 / SDD-004 v2.0). El plato es la unidad de estado.
///
/// POST /api/v1/orders/items/{itemId}/march  - El PDA marcha un plato retenido (HELD → IN_PREPARATION)
/// POST /api/v1/orders/items/{itemId}/ready  - La estación lo da por listo (IN_PREPARATION → READY)
///
/// Se añade <b>al lado</b> del contrato por pedido: <see cref="OrdersController"/> y
/// PATCH /orders/{orderId}/status siguen vivos hasta que el tablero KDS migre al plato.
/// </summary>
[Route("api/v1/orders/items")]
public sealed class OrderItemsController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public OrderItemsController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    private Guid TenantId => (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

    /// <summary>Estación del token; solo la traen los tokens de dispositivo KDS.</summary>
    private Guid? CallerStationId => HttpContext.Items["station_id"] as Guid?;

    // ── POST /api/v1/orders/items/{itemId}/march ──────────────────────────────
    // SDD-004 v2.0 · ADR-026: marchar es potestad exclusiva del PDA (o Administración).
    //
    // DÓNDE VIVE LA DECISIÓN (no está duplicada: está repartida a propósito)
    //  · La POLÍTICA es la puerta: quien no sea PDA —ni su alias Mesero— ni Admin se lleva un
    //    403 antes de que se toque la base. Es lo que saldó la deuda de usar StaffJwt aquí.
    //  · El HANDLER sigue siendo el dueño de la regla: MarchOrderItemHandler vuelve a mirar el
    //    rol antes de leer nada. No es un resto de la política —el comando se invoca sin pasar
    //    por HTTP (tests, y mañana otra entrada), y la regla es del módulo, no del borde—; lo
    //    que cambia es que desde HTTP ya no se alcanza. También suyas son las reglas finas:
    //    el plato está en HELD y la sesión de la mesa sigue abierta.
    //  · La traducción del alias Mesero → PDA vive SOLO aquí, en RolesDelToken: el handler
    //    conoce únicamente la nomenclatura nueva.

    [HttpPost("{itemId:guid}/march")]
    [Authorize(Policy = AuthorizationPolicies.StaffPdaKdsOrAdmin)]
    [ProducesResponseType(typeof(MarchOrderItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> March(Guid itemId, CancellationToken ct)
    {
        if (TenantId == Guid.Empty)
            return BadRequest("Tenant no resuelto.");

        return ToResponse(await Mediator.Send(new MarchOrderItemCommand(
            itemId,
            TenantId,
            StaffId: _currentUser.UserId,
            CallerRoles: RolesDelToken()), ct));
    }

    // ── POST /api/v1/orders/items/{itemId}/start-preparation ──────────────────
    // El "¡a preparar!" del pase: el encargado de cocina saca el plato de la cola y lo pone en
    // los fuegos (QUEUED → IN_PREPARATION). Es la mitad de la decisión que antes hacía /march:
    // marchar mete en la cola (cocina o PDA), la orden de preparación la da SOLO la estación.
    //
    // Igual que en /ready: la política filtra el acceso (KDS o Administración) y el handler manda
    // en lo fino —de qué estación es el plato (WRONG_STATION), en qué estado está y si la mesa
    // sigue abierta—.

    [HttpPost("{itemId:guid}/start-preparation")]
    [Authorize(Policy = AuthorizationPolicies.StaffKdsOrAdmin)]
    [ProducesResponseType(typeof(StartItemPreparationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartPreparation(Guid itemId, CancellationToken ct)
    {
        if (TenantId == Guid.Empty)
            return BadRequest("Tenant no resuelto.");

        return ToResponse(await Mediator.Send(new StartItemPreparationCommand(
            itemId,
            TenantId,
            StaffId: _currentUser.UserId,
            CallerRoles: RolesDelToken(),
            CallerStationId: CallerStationId), ct));
    }

    // ── POST /api/v1/orders/items/{itemId}/ready ──────────────────────────────
    // SDD-004 v2.0: la estación da el plato por listo y entra en la cola de runners.
    //
    // La política filtra el acceso (KDS o Administración); el handler manda en lo fino: cada
    // plato es de una sola estación (ADR-029) y quien no sea Admin solo da por listo lo suyo
    // —WRONG_STATION, contra CallerStationId—, además del estado de partida (en preparación o
    // devuelto). Ese reparto es el mismo que en /march.
    //
    // PENDIENTE, acotado: el SDD admite además KDS Device Token, y esta política —igual que la
    // StaffJwt que sustituye— solo acepta tokens de personal (el del dispositivo es UserType
    // "device"). Habilitarlo no es renombrar un rol, es abrir un endpoint de escritura a otro
    // tipo de token: la decisión es de quien lleve SDD-004. Lo único que falta es una política
    // que acepte ambos; el resto ya está cableado, porque CallerStationId sale igual del claim
    // del dispositivo que del `stationId` del Staff JWT.

    [HttpPost("{itemId:guid}/ready")]
    [Authorize(Policy = AuthorizationPolicies.StaffKdsOrAdmin)]
    [ProducesResponseType(typeof(MarkOrderItemReadyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Ready(Guid itemId, CancellationToken ct)
    {
        if (TenantId == Guid.Empty)
            return BadRequest("Tenant no resuelto.");

        return ToResponse(await Mediator.Send(new MarkOrderItemReadyCommand(
            itemId,
            TenantId,
            StaffId: _currentUser.UserId,
            CallerRoles: RolesDelToken(),
            CallerStationId: CallerStationId), ct));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Todos los roles del token: la lista completa unida al rol principal, que no siempre
    /// coinciden según cómo se emitiera el JWT. La unión la hace la capa de aplicación
    /// (<see cref="OrderStaffRoles.Merge"/>) para que haya un solo criterio, y aquí se le
    /// añade la traducción del alias: quien traiga <c>Mesero</c> baja también como <c>PDA</c>
    /// (<see cref="StaffRoleNames.ExpandirAlias"/>), que es lo que el handler entiende.
    /// </summary>
    private IReadOnlyList<string> RolesDelToken() =>
        StaffRoleNames.ExpandirAlias(
            OrderStaffRoles.Merge(_currentUser.Roles, _currentUser.Role));
}
