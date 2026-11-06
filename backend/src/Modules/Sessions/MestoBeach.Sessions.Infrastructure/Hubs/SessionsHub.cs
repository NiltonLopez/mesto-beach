using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MestoBeach.Sessions.Infrastructure.Hubs;

/// <summary>
/// SignalR hub for real-time session events (PIN regenerated, session closed, waiter reassigned,
/// avisos de mesa). Clients subscribe to group "waiter-{tenantId}".
/// <para>
/// <b>[Authorize]</b> (PRD-019 — ADR-040): era la única puerta SignalR anónima; cualquier WebSocket
/// podía unirse a <c>waiter-{tenantId}</c> y escuchar los avisos de todas las mesas del local. El token
/// viaja por <c>?access_token=</c> como en los otros hubs y el tenant del grupo debe ser el del llamante.
/// </para>
/// </summary>
[Authorize]
public sealed class SessionsHub : Hub
{
    /// <summary>El tenant del llamante según su token ya validado (no el parámetro).</summary>
    private string? CallerTenant => Context.User?.FindFirst("tenantId")?.Value;

    public async Task JoinWaiterGroup(string tenantId)
    {
        var caller = CallerTenant;
        if (caller is null || !string.Equals(caller, tenantId, StringComparison.OrdinalIgnoreCase)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"waiter-{caller}");
    }

    public async Task LeaveWaiterGroup(string tenantId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"waiter-{tenantId}");
}
