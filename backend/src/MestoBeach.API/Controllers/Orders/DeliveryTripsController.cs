using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Orders.Application.Commands;
using MestoBeach.Orders.Application.Queries;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Orders;

/// <summary>
/// Viajes de entrega (ADR-027 / SDD-004 v2.0). El runner arma su bandeja reclamando de forma
/// atómica los platos listos de su estación, reparte por varios puestos y cierra la ruta.
///
/// POST /api/v1/delivery-trips                   - Reclama los platos y abre el viaje
/// GET  /api/v1/delivery-trips/mine              - Manifiesto del viaje abierto (204 si no hay)
/// POST /api/v1/delivery-trips/{tripId}/complete - Cierra la ruta (entregado por defecto)
/// POST /api/v1/delivery-trips/{tripId}/release  - Libera un viaje abandonado
/// </summary>
[Route("api/v1/delivery-trips")]
public sealed class DeliveryTripsController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public DeliveryTripsController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    private Guid TenantId => (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

    // ── POST /api/v1/delivery-trips ───────────────────────────────────────────
    // El reclamo es todo o nada: si algún plato ya no está READY, se revierte entero (409).
    // La estación sale de la adscripción del runner, NUNCA del cuerpo de la petición (ADR-027),
    // y esa adscripción ya existe: es el claim `stationId` del Staff JWT (SDD-001 v2.0). Se
    // manda al handler, que rechaza con WRONG_STATION cualquier plato de otra estación. Un
    // Admin no tiene estación adscrita: baja en null y entonces se deduce de los propios platos.
    //
    // DÓNDE VIVE LA DECISIÓN: la política es la puerta (Runner o Admin, 403 antes del handler);
    // el handler conserva la misma comprobación de rol porque el comando se puede invocar sin
    // pasar por HTTP, y es el dueño de lo fino — un solo viaje abierto por runner, todos los
    // platos de la misma estación y de la SUYA, el tope de 30 y el reclamo atómico.

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.StaffRunnerOrAdmin)]
    [ProducesResponseType(typeof(DeliveryTripDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDeliveryTripRequest req, CancellationToken ct)
    {
        if (TenantId == Guid.Empty)
            return BadRequest("Tenant no resuelto.");

        // El tope se comprueba aquí —y solo aquí— para poder devolver el 422 que pide el SDD:
        // la taxonomía compartida (ErrorType) no tiene 422, así que el mismo caso resuelto en el
        // handler sale como 400. El número lo sigue poniendo el handler; no se duplica.
        // El resto de validaciones de forma (requerido, vacío, repetidos) las hace su validador.
        if (req.ItemIds is { Count: > CreateDeliveryTripHandler.MaxItemsPerTrip })
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "TOO_MANY_ITEMS",
                Detail = $"Un viaje admite como mucho {CreateDeliveryTripHandler.MaxItemsPerTrip} platos.",
                Status = StatusCodes.Status422UnprocessableEntity
            });

        return ToResponse(await Mediator.Send(new CreateDeliveryTripCommand(
            req.ItemIds ?? [],
            CallerTenantId: TenantId,
            RunnerId: _currentUser.UserId,
            CallerRole: _currentUser.Role,
            RunnerStationId: _currentUser.AssignedStationId,
            CallerRoles: RolesDelToken()), ct),
            successStatus: 201);
    }

    // ── GET /api/v1/delivery-trips/mine ───────────────────────────────────────
    // Fuente de verdad de la ruta: la app no guarda el manifiesto solo en memoria (ADR-024).
    // Sin viaje abierto responde 204, no 404: no llevar bandeja no es un error.
    // La política filtra por rol; el viaje que se devuelve es siempre el del propio llamador
    // (RunnerId sale del token, no de la petición), así que no hay regla fina que delegar.

    [HttpGet("mine")]
    [Authorize(Policy = AuthorizationPolicies.StaffRunnerOrAdmin)]
    [ProducesResponseType(typeof(DeliveryTripDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        if (TenantId == Guid.Empty)
            return BadRequest("Tenant no resuelto.");

        var result = await Mediator.Send(new GetMyDeliveryTripQuery(
            TenantId, RunnerId: _currentUser.UserId), ct);

        if (result.IsFailure) return ToResponse(result);

        return result.Value is null ? NoContent() : Ok(result.Value);
    }

    // ── POST /api/v1/delivery-trips/{tripId}/complete ─────────────────────────
    // "Todo marcado, desmarca lo que falló": los platos del viaje se dan por entregados y solo
    // se enumeran las excepciones, que vuelven a la cola de su estación como devueltas.
    //
    // La política dice quién puede cerrar viajes; de QUIÉN es este viaje decide el handler:
    // el runner solo cierra el suyo (si no, 404, que no filtra la existencia del ajeno) y
    // Administración cierra el de cualquiera.

    [HttpPost("{tripId:guid}/complete")]
    [Authorize(Policy = AuthorizationPolicies.StaffRunnerOrAdmin)]
    [ProducesResponseType(typeof(CompleteDeliveryTripDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(
        Guid tripId,
        [FromBody] CompleteDeliveryTripRequest? req,
        CancellationToken ct)
    {
        if (TenantId == Guid.Empty)
            return BadRequest("Tenant no resuelto.");

        // Cuerpo opcional a propósito: cerrar sin desmarcar nada entrega todo el viaje.
        return ToResponse(await Mediator.Send(new CompleteDeliveryTripCommand(
            tripId,
            req?.ReturnedItemIds ?? [],
            CallerTenantId: TenantId,
            RequesterId: _currentUser.UserId,
            CallerRole: _currentUser.Role,
            CallerRoles: RolesDelToken()), ct));
    }

    // ── POST /api/v1/delivery-trips/{tripId}/release ──────────────────────────
    // Runner que acaba el turno —o pierde el móvil— con la bandeja reclamada: los platos
    // vuelven a READY y el viaje queda RELEASED. Solo Administración o el propio runner: la
    // política pone el rol, y el "propio runner" —la parte fina— la sigue poniendo el handler.

    [HttpPost("{tripId:guid}/release")]
    [Authorize(Policy = AuthorizationPolicies.StaffRunnerOrAdmin)]
    [ProducesResponseType(typeof(ReleaseDeliveryTripDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Release(Guid tripId, CancellationToken ct)
    {
        if (TenantId == Guid.Empty)
            return BadRequest("Tenant no resuelto.");

        return ToResponse(await Mediator.Send(new ReleaseDeliveryTripCommand(
            tripId,
            CallerTenantId: TenantId,
            RequesterId: _currentUser.UserId,
            CallerRole: _currentUser.Role,
            CallerRoles: RolesDelToken()), ct));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Todos los roles del token: la lista completa unida al rol principal, que no siempre
    /// coinciden según cómo se emitiera el JWT. La unión la hace la capa de aplicación
    /// (<see cref="OrderStaffRoles.Merge"/>) para que haya un solo criterio.
    /// </summary>
    private IReadOnlyList<string> RolesDelToken() =>
        OrderStaffRoles.Merge(_currentUser.Roles, _currentUser.Role);
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record CreateDeliveryTripRequest(List<Guid>? ItemIds);

public record CompleteDeliveryTripRequest(List<Guid>? ReturnedItemIds);
