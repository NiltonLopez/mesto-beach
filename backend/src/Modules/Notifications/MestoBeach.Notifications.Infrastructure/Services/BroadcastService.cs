using MestoBeach.Notifications.Application.Common;
using MestoBeach.Notifications.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Notifications.Infrastructure.Services;

public sealed class BroadcastService : IBroadcastService
{
    private readonly IHubContext<RealtimeHub> _hub;
    private readonly ILogger<BroadcastService> _logger;

    public BroadcastService(IHubContext<RealtimeHub> hub, ILogger<BroadcastService> logger)
    {
        _hub    = hub;
        _logger = logger;
    }

    public async Task<int> BroadcastToGroupAsync(
        string group, string eventName, object payload, CancellationToken ct)
    {
        await _hub.Clients.Group(group).SendAsync(eventName, payload, ct);

        _logger.LogInformation(
            "signalr.broadcast event={Event} group={Group}",
            eventName, group);

        // ASP.NET Core SignalR in-process does not expose group member counts.
        // Return 0 — caller must treat 0 as valid (fire-and-forget semantics).
        return 0;
    }

    public async Task<int> BroadcastToAllAsync(
        string eventName, object payload, CancellationToken ct)
    {
        await _hub.Clients.All.SendAsync(eventName, payload, ct);

        _logger.LogInformation(
            "signalr.broadcast event={Event} broadcast=all",
            eventName);

        return 0;
    }
}
