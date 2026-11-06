using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Sessions.Application.Commands;
using MestoBeach.Sessions.Application.Queries;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;

namespace MestoBeach.API.Controllers.Sessions;

[Route("api/v1/tables")]
public sealed class TablesController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public TablesController(ISender mediator, ICurrentUser currentUser)
        : base(mediator) => _currentUser = currentUser;

    private bool CanAccessTenant(Guid tenantId)
    {
        if (_currentUser.UserType == "platform_admin") return true;
        if (_currentUser.UserType == "staff")
        {
            var staffTenantId = HttpContext.Items["tenant_id"] as Guid?;
            return staffTenantId == tenantId;
        }
        return false;
    }

    // GET /api/v1/tables?tenantId=&active=&zone=
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.StaffJwt)]
    public async Task<IActionResult> GetTables(
        [FromQuery] Guid tenantId,
        [FromQuery] bool? active,
        [FromQuery] string? zone,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            return BadRequest("tenantId es requerido.");
        if (!CanAccessTenant(tenantId))
            return StatusCode(403, new ProblemDetails { Title = "FORBIDDEN", Status = 403 });

        return ToResponse(await Mediator.Send(
            new GetTablesQuery(tenantId, active, zone, _currentUser.UserId), ct));
    }

    // POST /api/v1/tables
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> CreateTable(
        [FromBody] CreateTableRequest req, CancellationToken ct)
    {
        if (!CanAccessTenant(req.TenantId))
            return StatusCode(403, new ProblemDetails { Title = "FORBIDDEN", Status = 403 });

        return ToResponse(await Mediator.Send(
            new CreateTableCommand(req.TenantId, req.TableCode, req.Zone, req.Active ?? true,
                _currentUser.UserId, req.FloorId, req.PosX, req.PosY, req.Seats, req.Shape,
                req.QrMode, req.RequiresPin ?? true, req.Mode, req.Kind),
            ct), successStatus: 201);
    }

    // PATCH /api/v1/tables/layout — distribución de UN modo (PRD-019):
    // { mode, tables:[{tableId, floorId?, posX?, posY?, active?, stationId?}] }. Se lee como JSON
    // crudo para distinguir un campo ausente (conserva el valor) de uno enviado a null.
    [HttpPatch("layout")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> UpdateLayout(
        [FromBody] JsonObject body, CancellationToken ct)
    {
        Guid tenantId = (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;
        if (body is null) return BadRequest("Body vacío.");

        string? mode = body["mode"] is JsonValue mv && mv.TryGetValue<string>(out var ms) ? ms : null;
        if (body["tables"] is not JsonArray arr)
            return StatusCode(400, new ProblemDetails
            {
                Title = "Validation", Status = 400,
                Detail = "tables es obligatorio (el cuerpo {placements} ya no se acepta)."
            });

        var inputs = new List<TableLayoutInput>(arr.Count);
        try
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject o) continue;
                Guid tableId = o["tableId"] is JsonValue tv && tv.TryGetValue<string>(out var ts)
                    && Guid.TryParse(ts, out var tid) ? tid : Guid.Empty;
                bool hasFloor = o.ContainsKey("floorId");
                Guid? floorId = hasFloor && o["floorId"] is not null ? Guid.Parse(o["floorId"]!.GetValue<string>()) : null;
                bool hasStation = o.ContainsKey("stationId");
                Guid? stationId = hasStation && o["stationId"] is not null ? Guid.Parse(o["stationId"]!.GetValue<string>()) : null;
                double? posX = o["posX"]?.GetValue<double>();
                double? posY = o["posY"]?.GetValue<double>();
                bool? active = o["active"]?.GetValue<bool>();
                inputs.Add(new TableLayoutInput(tableId, hasFloor, floorId, posX, posY, active, hasStation, stationId));
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return StatusCode(400, new ProblemDetails
            {
                Title = "Validation", Status = 400, Detail = "Formato inválido en tables[]."
            });
        }

        return ToResponse(await Mediator.Send(
            new UpdateTablesLayoutCommand(tenantId, mode, inputs, _currentUser.UserId), ct));
    }

    // PATCH /api/v1/tables/{tableId}
    [HttpPatch("{tableId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> UpdateTable(
        Guid tableId,
        [FromBody] JsonObject body,
        CancellationToken ct)
    {
        if (body is null || body.Count == 0)
            return BadRequest("Body vacío.");

        // Extract tenantId from staff JWT
        Guid tenantId = (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;
        if (_currentUser.UserType == "platform_admin")
        {
            // platform admin must pass tenantId in body or we infer from the table itself
            // For simplicity: resolve from DB — pass Guid.Empty and let handler infer from table
            tenantId = Guid.Empty;
        }

        bool hasCode = body.ContainsKey("tableCode");
        bool hasZone = body.ContainsKey("zone");
        bool hasActive = body.ContainsKey("active");
        // PRD-019: la posición ya no se edita aquí, sino por modo con PATCH /tables/layout.
        if (body.ContainsKey("floorId") || body.ContainsKey("posX") || body.ContainsKey("posY"))
            return StatusCode(400, new ProblemDetails
            {
                Title = "Validation", Status = 400,
                Detail = "La posición se edita por modo con PATCH /tables/layout."
            });

        bool hasKind = body.ContainsKey("kind");
        bool hasSeats = body.ContainsKey("seats");
        bool hasShape = body.ContainsKey("shape");
        bool hasQr = body.ContainsKey("qrMode");
        bool hasRequiresPin = body.ContainsKey("requiresPin");

        string? newCode = hasCode ? body["tableCode"]?.GetValue<string>() : null;
        string? newZone = hasZone && body["zone"] is not null
            ? body["zone"]!.GetValue<string>()
            : null;
        bool clearZone = hasZone && body["zone"] is null;
        bool? active = hasActive ? body["active"]?.GetValue<bool>() : null;

        string? kind = hasKind ? body["kind"]?.GetValue<string>() : null;
        int? seats = hasSeats ? body["seats"]?.GetValue<int>() : null;
        string? shape = hasShape ? body["shape"]?.GetValue<string>() : null;
        string? qrMode = hasQr ? body["qrMode"]?.GetValue<string>() : null;
        bool? requiresPin = hasRequiresPin ? body["requiresPin"]?.GetValue<bool>() : null;

        return ToResponse(await Mediator.Send(
            new UpdateTableCommand(tenantId, tableId, newCode, newZone, clearZone, active,
                _currentUser.UserId, seats, shape, qrMode, requiresPin, kind), ct));
    }

    // DELETE /api/v1/tables/{tableId}
    [HttpDelete("{tableId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> DeleteTable(Guid tableId, CancellationToken ct)
    {
        Guid tenantId = (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;
        return ToResponse(await Mediator.Send(
            new DeleteTableCommand(tenantId, tableId, _currentUser.UserId), ct));
    }

    // GET /api/v1/tables/{tableId}/qr
    [HttpGet("{tableId:guid}/qr")]
    [Authorize(Policy = AuthorizationPolicies.StaffAdmin)]
    public async Task<IActionResult> GetQr(Guid tableId, CancellationToken ct)
    {
        Guid tenantId = (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

        var result = await Mediator.Send(
            new GenerateTableQrCommand(tenantId, tableId, _currentUser.UserId), ct);

        if (result.IsFailure) return Problem(result.Error);

        var dto = result.Value;
        Response.Headers["Content-Disposition"] = $"attachment; filename=\"qr-{dto.TableCode}.png\"";
        Response.Headers["Cache-Control"] = "no-store";
        return File(dto.PngBytes, "image/png");
    }

    // ─── helpers ──────────────────────────────────────────────────────────────
    private ObjectResult Problem(MestoBeach.Shared.Errors.Error error)
    {
        int status = error.Type switch
        {
            MestoBeach.Shared.Errors.ErrorType.NotFound => 404,
            MestoBeach.Shared.Errors.ErrorType.Validation => 400,
            MestoBeach.Shared.Errors.ErrorType.Conflict => 409,
            _ => 500
        };
        return StatusCode(status, new ProblemDetails
        {
            Title = error.Code,
            Detail = error.Message,
            Status = status
        });
    }
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public sealed record CreateTableRequest(
    Guid TenantId,
    string TableCode,
    string? Zone,
    bool? Active,
    Guid? FloorId = null,
    double? PosX = null,
    double? PosY = null,
    int? Seats = null,
    string? Shape = null,
    string? QrMode = null,
    bool? RequiresPin = null,
    string? Mode = null,     // PRD-019: RESTAURANT | NIGHT; null = modo activo
    string? Kind = null);    // PRD-019: TABLE | HIGH_TABLE | RESERVADO

