using MediatR;
using MestoBeach.API.Middleware;
using MestoBeach.Notifications.Application.Commands;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MestoBeach.API.Controllers.Notifications;

/// <summary>
/// POST /api/v1/notifications/send-email  — internal, X-Service-Token
/// POST /api/v1/notifications/broadcast   — internal, X-Service-Token
/// Both endpoints are blocked from external traffic at the API gateway level.
/// </summary>
[Route("api/v1/notifications")]
[ServiceFilter(typeof(RequireServiceTokenFilter))]
public sealed class NotificationsController : ApiController
{
    public NotificationsController(ISender mediator) : base(mediator) { }

    // ── POST /send-email ──────────────────────────────────────────────────────

    [HttpPost("send-email")]
    public async Task<IActionResult> SendEmail(
        [FromBody] SendEmailRequest req,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new SendEmailCommand(
            req.TemplateId,
            req.Recipient.Email,
            req.Recipient.Name,
            req.Variables ?? new(),
            req.Locale ?? "es"), ct);

        return ToResponse(result, successStatus: 202);
    }

    // ── POST /broadcast ───────────────────────────────────────────────────────

    [HttpPost("broadcast")]
    public async Task<IActionResult> Broadcast(
        [FromBody] BroadcastRequest req,
        CancellationToken ct)
    {
        JsonElement payload;
        if (req.Payload is null)
            return BadRequest(new { error = "payload es requerido." });

        payload = req.Payload.Value;

        return ToResponse(await Mediator.Send(new BroadcastCommand(
            req.Group,
            req.Broadcast ?? false,
            req.Event,
            payload), ct));
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record RecipientDto(string Email, string? Name);

public record SendEmailRequest(
    string TemplateId,
    RecipientDto Recipient,
    Dictionary<string, string>? Variables,
    string? Locale);

public record BroadcastRequest(
    string? Group,
    bool? Broadcast,
    string Event,
    JsonElement? Payload);
