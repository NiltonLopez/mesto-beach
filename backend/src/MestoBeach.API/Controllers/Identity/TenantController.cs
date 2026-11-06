using MediatR;
using MestoBeach.Identity.Application.Auth.Staff.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Identity;

/// <summary>
/// Tenant-level actions the restaurant owner performs on their own restaurant.
/// Base path: /api/v1/identity/tenant
/// </summary>
[Route("api/v1/identity/tenant")]
public sealed class TenantController : ApiController
{
    public TenantController(ISender mediator) : base(mediator) { }

    /// <summary>
    /// Launches the owner's restaurant (Pending → live) so the rest of the staff can sign in.
    /// Admin only; always acts on the caller's active tenant (from the JWT), never another.
    /// </summary>
    [HttpPost("activate")]
    [Authorize(Policy = "StaffAdmin")]
    [ProducesResponseType(typeof(TenantActivationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Activate(CancellationToken ct)
    {
        var tenantId = (Guid)HttpContext.Items["tenant_id"]!;
        return ToResponse(await Mediator.Send(new ActivateTenantCommand(tenantId), ct));
    }
}
