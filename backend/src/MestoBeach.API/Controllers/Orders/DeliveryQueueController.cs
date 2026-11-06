using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Orders.Application.Queries;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Orders;

/// <summary>
/// Cola del pase: los platos <c>READY</c> de una estación que ningún runner ha reclamado
/// todavía (RF-1830 / ADR-027). Es lo que el runner mira antes de armar la bandeja con
/// POST /api/v1/delivery-trips.
///
/// GET /api/v1/delivery-queue[?stationId=]
///
/// Auth: Runner o Admin. El runner lee la estación de su adscripción; <c>stationId</c> quedó en
/// opcional y es para Administración, que no tiene estación propia.
///
/// AVISO: este endpoint <b>no tiene contrato propio en SDD-004 v2.0</b> —el SDD describe la
/// cola de runners como consecuencia de <c>ITEM_READY</c>, pero no la especifica—. La forma de
/// aquí es la mínima que sostiene el encargo; hay que escribirla en el SDD, incluido el paso
/// de <c>stationId</c> de obligatorio a opcional que trae este cambio.
/// </summary>
[Route("api/v1/delivery-queue")]
public sealed class DeliveryQueueController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public DeliveryQueueController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    private Guid TenantId => (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

    // ── GET /api/v1/delivery-queue ────────────────────────────────────────────
    // La cola que mira un runner es la de SU estación. Esa adscripción ya existe —claim
    // `stationId` del Staff JWT, SDD-001 v2.0—, así que manda el token y no la query: pedir
    // otra estación con un token de runner es 403. El parámetro sobrevive porque
    // Administración no tiene estación adscrita y necesita decir de qué pase lee.
    //
    // La política filtra el acceso por rol (Runner o Admin); esta comprobación de estación es
    // la regla fina y vive aquí, no en la consulta, porque nace del token: GetDeliveryQueueQuery
    // es una lectura por estación y no sabe —ni tiene por qué— quién la pide.

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.StaffRunnerOrAdmin)]
    [ProducesResponseType(typeof(DeliveryQueueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Queue(
        [FromQuery] string? stationId,
        CancellationToken ct)
    {
        if (TenantId == Guid.Empty)
            return BadRequest("Tenant no resuelto.");

        Guid? pedida = null;
        if (!string.IsNullOrEmpty(stationId))
        {
            if (!Guid.TryParse(stationId, out var parsedStationId))
                return BadRequest("stationId debe ser un UUID válido.");
            pedida = parsedStationId;
        }

        var adscrita = _currentUser.AssignedStationId;
        var esAdmin = _currentUser.HasRole(StaffRoleNames.Admin);

        if (adscrita.HasValue && !esAdmin && pedida.HasValue && pedida != adscrita)
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "WRONG_STATION",
                Detail = "Esa cola es de otra estación.",
                Status = StatusCodes.Status403Forbidden
            });

        // Administración puede mirar cualquier pase; el runner adscrito, solo el suyo. Un token
        // sin estación —Admin, o un runner al que aún no se la han asignado— sigue teniendo que
        // decirla por query.
        var estacion = esAdmin ? pedida ?? adscrita : adscrita ?? pedida;

        if (!estacion.HasValue)
            return BadRequest(
                "stationId es requerido: este token no trae estación adscrita, " +
                "así que hay que indicar de qué pase se lee.");

        return ToResponse(await Mediator.Send(
            new GetDeliveryQueueQuery(TenantId, estacion.Value), ct));
    }
}
