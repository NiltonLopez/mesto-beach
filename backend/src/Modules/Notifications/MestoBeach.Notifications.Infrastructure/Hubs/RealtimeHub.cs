using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace MestoBeach.Notifications.Infrastructure.Hubs;

/// <summary>
/// Unified real-time hub at /hubs/realtime.
/// Requires a valid JWT (validated by ASP.NET Core JWT bearer middleware before the hub runs).
/// On connection, automatically assigns groups based on JWT claim 'type':
///   staff    → tenant:{tenantId}, staff:{userId}
///   customer → session:{tenantId}:{sessionId}
///   device   → station:{tenantId}:{stationId}, tenant:{tenantId}
/// Token may be passed as Authorization header OR as ?access_token= query param
/// (configured in Program.cs JwtBearerEvents.OnMessageReceived).
/// </summary>
[Authorize]
public sealed class RealtimeHub : Hub
{
    private readonly ILogger<RealtimeHub> _logger;

    public RealtimeHub(ILogger<RealtimeHub> logger) => _logger = logger;

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user?.Identity?.IsAuthenticated != true) { Context.Abort(); return; }

        var tokenType   = user.FindFirstValue("type");
        var sub         = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantIdStr = user.FindFirstValue("tenantId");

        if (string.IsNullOrWhiteSpace(tokenType) ||
            !Guid.TryParse(sub, out var userId) ||
            !Guid.TryParse(tenantIdStr, out var tenantId))
        {
            Context.Abort();
            return;
        }

        var groups = new List<string>();

        switch (tokenType)
        {
            case "staff":
                groups.Add($"tenant:{tenantId}");
                groups.Add($"staff:{userId}");
                break;

            case "customer":
            {
                var sessionIdStr = user.FindFirstValue("sessionId");
                if (Guid.TryParse(sessionIdStr, out var sessionId))
                    groups.Add($"session:{tenantId}:{sessionId}");
                break;
            }

            case "device":
            {
                var stationIdStr = user.FindFirstValue("stationId");
                if (Guid.TryParse(stationIdStr, out var stationId))
                {
                    groups.Add($"station:{tenantId}:{stationId}");
                    groups.Add($"tenant:{tenantId}");
                }
                break;
            }
        }

        foreach (var g in groups)
            await Groups.AddToGroupAsync(Context.ConnectionId, g);

        await Clients.Caller.SendAsync("connected", new
        {
            type         = "connected",
            connectionId = Context.ConnectionId,
            groups
        });

        _logger.LogInformation(
            "signalr.connected {ConnectionId} tenantId={TenantId} userId={UserId} " +
            "tokenType={TokenType} groups={Groups}",
            Context.ConnectionId, tenantId, userId, tokenType, string.Join(",", groups));

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "signalr.disconnected {ConnectionId} reason={Reason}",
            Context.ConnectionId, exception?.GetType().Name ?? "normal");

        await base.OnDisconnectedAsync(exception);
    }
}
