using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MestoBeach.API.Middleware;
using MestoBeach.Identity.Application.Venue;
using MestoBeach.Shared.Context;
using MestoBeach.Shared.Venue;

namespace MestoBeach.API.Controllers.Identity;

/// <summary>
/// Modo del local (ADR-036, SDD-001 §7). Base path <c>/api/v1/venue</c>, fuera de
/// <c>/api/v1/identity</c> a propósito: lo sondean el menú anónimo, el personal y el panel.
/// </summary>
[Route("api/v1/venue")]
public sealed class VenueController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public VenueController(ISender mediator, ICurrentUser currentUser) : base(mediator)
        => _currentUser = currentUser;

    /// <summary>GET /api/v1/venue/{tenantId} — anónimo, no-store. Lo sondea el menú cada 30 s.</summary>
    [HttpGet("{tenantId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(VenueStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> GetPublic(Guid tenantId, CancellationToken ct) => GetStateAsync(tenantId, false, ct);

    /// <summary>
    /// GET /api/v1/venue — personal o KDS: {tenantId, mode, changedAt} más <c>stations</c> con su
    /// <c>isOn</c> calculado con el mismo modo (PR3, ADR-037).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.StaffOrDeviceJwt)]
    [ProducesResponseType(typeof(VenueStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> GetForStaff(CancellationToken ct)
    {
        if (HttpContext.Items["tenant_id"] is not Guid tenantId)
            return Task.FromResult<IActionResult>(Unauthorized());
        return GetStateAsync(tenantId, true, ct);
    }

    /// <summary>GET /api/v1/venue/preflight — vista previa del pre-flight (Admin).</summary>
    [HttpGet("preflight")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    [ProducesResponseType(typeof(VenuePreflightDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Preflight(CancellationToken ct)
    {
        var tenantId = (Guid)HttpContext.Items["tenant_id"]!;
        NoStore();
        return ToResponse(await Mediator.Send(new GetVenuePreflightQuery(tenantId), ct));
    }

    /// <summary>
    /// POST /api/v1/venue/mode — cambia el modo de todo el local si está vacío (Admin).
    /// 409 VENUE_NOT_IDLE con los contadores y las mesas en la raíz del ProblemDetails.
    /// </summary>
    [HttpPost("mode")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    [ProducesResponseType(typeof(ChangeVenueModeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeMode([FromBody] ChangeVenueModeRequest? req, CancellationToken ct)
    {
        var tenantId = (Guid)HttpContext.Items["tenant_id"]!;
        return ToResponse(await Mediator.Send(
            new ChangeVenueModeCommand(tenantId, _currentUser.UserId, req?.Mode), ct));
    }

    /// <summary>GET /api/v1/venue/history?limit= — últimos cambios de modo (Admin).</summary>
    [HttpGet("history")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    [ProducesResponseType(typeof(VenueHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> History([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var tenantId = (Guid)HttpContext.Items["tenant_id"]!;
        return ToResponse(await Mediator.Send(new GetVenueHistoryQuery(tenantId, limit), ct));
    }

    private async Task<IActionResult> GetStateAsync(Guid tenantId, bool includeStations, CancellationToken ct)
    {
        NoStore();
        try
        {
            return ToResponse(await Mediator.Send(new GetVenueStateQuery(tenantId, includeStations), ct));
        }
        catch (VenueModeUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = VenueModeUnavailableException.Code,
                Detail = ex.Message,
                Status = StatusCodes.Status503ServiceUnavailable,
            });
        }
    }

    /// <summary>Ni el navegador ni un proxy pueden servir un modo viejo.</summary>
    private void NoStore()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
    }
}

/// <summary><c>mode</c> como string: se valida en el handler (INVALID_MODE).</summary>
public sealed record ChangeVenueModeRequest(string? Mode);
