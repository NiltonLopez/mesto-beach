using FluentValidation;
using MediatR;
using MestoBeach.Notifications.Application.Common;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MestoBeach.Notifications.Application.Commands;

// ── Allowed events and their required top-level payload fields ─────────────────

file static class EventCatalog
{
    public static readonly HashSet<string> KnownEvents = new()
    {
        "order.created", "order.status_changed", "order.cancelled",
        "session.opened", "session.participant_joined", "session.closed",
        "pin.regenerated", "maintenance.updated",
        "loyalty.points_earned", "loyalty.points_redeemed"
    };

    public static readonly Dictionary<string, string[]> RequiredPayloadFields = new()
    {
        ["order.created"]             = ["orderId", "sessionId", "tableCode", "participantId", "items", "stationId"],
        ["order.status_changed"]      = ["orderId", "status", "tableCode"],
        ["order.cancelled"]           = ["orderId", "reason"],
        ["session.opened"]            = ["sessionId", "tableCode", "waiterId"],
        ["session.participant_joined"]= ["sessionId", "participantCount"],
        ["session.closed"]            = ["sessionId"],
        ["pin.regenerated"]           = ["sessionId", "tableCode", "newPin"],
        ["maintenance.updated"]       = ["message", "apps", "activeFrom", "activeUntil"],
        ["loyalty.points_earned"]     = ["customerId", "points", "balanceAfter", "tenantName"],
        ["loyalty.points_redeemed"]   = ["customerId", "points", "itemName", "tenantName"]
    };

    // Group pattern: tenant:{uuid} | session:{uuid}:{uuid} | station:{uuid}:{uuid} | staff:{uuid}
    private static readonly Regex GroupPattern = new(
        @"^(tenant:[0-9a-f-]{36}|session:[0-9a-f-]{36}:[0-9a-f-]{36}|station:[0-9a-f-]{36}:[0-9a-f-]{36}|staff:[0-9a-f-]{36})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsValidGroup(string group) => GroupPattern.IsMatch(group);
}

// ── Command ───────────────────────────────────────────────────────────────────

public record BroadcastCommand(
    string? Group,
    bool Broadcast,
    string EventName,
    JsonElement Payload) : IRequest<Result<BroadcastResult>>;

public record BroadcastResult(
    string EventName,
    string? Group,
    DateTime DeliveredAt,
    int RecipientCount);

public sealed class BroadcastValidator : AbstractValidator<BroadcastCommand>
{
    public BroadcastValidator()
    {
        RuleFor(c => c.EventName)
            .Must(EventCatalog.KnownEvents.Contains)
            .WithMessage("Evento desconocido. Debe ser uno de los eventos del catálogo.");

        // Either group or broadcast must be present
        RuleFor(c => c)
            .Must(c => c.Broadcast || !string.IsNullOrWhiteSpace(c.Group))
            .WithMessage("Se requiere 'group' o 'broadcast: true'.");

        // broadcast:true only for maintenance.updated
        RuleFor(c => c.EventName)
            .Must((cmd, evt) => evt == "maintenance.updated")
            .When(c => c.Broadcast)
            .WithMessage("broadcast:true solo está permitido para el evento 'maintenance.updated'.");

        // group format validation
        RuleFor(c => c.Group)
            .Must(g => g is null || EventCatalog.IsValidGroup(g))
            .WithMessage("Formato de grupo inválido.")
            .When(c => !string.IsNullOrWhiteSpace(c.Group));

        // payload must contain required top-level fields
        RuleFor(c => c)
            .Must(c =>
            {
                if (!EventCatalog.RequiredPayloadFields.TryGetValue(c.EventName, out var required))
                    return true;
                if (c.Payload.ValueKind != JsonValueKind.Object) return false;
                return required.All(f => c.Payload.TryGetProperty(f, out _));
            })
            .WithMessage(c =>
            {
                if (!EventCatalog.RequiredPayloadFields.TryGetValue(c.EventName, out var required))
                    return "Payload inválido.";
                var missing = c.Payload.ValueKind == JsonValueKind.Object
                    ? required.Where(f => !c.Payload.TryGetProperty(f, out _))
                    : required;
                return $"Payload falta campos requeridos: {string.Join(", ", missing)}";
            });
    }
}

public sealed class BroadcastHandler : IRequestHandler<BroadcastCommand, Result<BroadcastResult>>
{
    private readonly IBroadcastService _broadcast;

    public BroadcastHandler(IBroadcastService broadcast) => _broadcast = broadcast;

    public async Task<Result<BroadcastResult>> Handle(BroadcastCommand cmd, CancellationToken ct)
    {
        var payloadObj = (object)cmd.Payload;
        int count;

        if (cmd.Broadcast)
            count = await _broadcast.BroadcastToAllAsync(cmd.EventName, payloadObj, ct);
        else
            count = await _broadcast.BroadcastToGroupAsync(cmd.Group!, cmd.EventName, payloadObj, ct);

        return Result.Success(new BroadcastResult(
            cmd.EventName,
            cmd.Group,
            DateTime.UtcNow,
            count));
    }
}
