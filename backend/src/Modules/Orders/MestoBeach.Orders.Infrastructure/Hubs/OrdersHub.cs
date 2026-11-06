using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MestoBeach.Orders.Infrastructure.Hubs;

/// <summary>
/// Minimal SignalR hub for Orders real-time events.
/// Clients join groups via the Sessions hub; this hub is used server-side only
/// to push events via IHubContext.
/// Groups:
///   tenant:{tenantId}               - all staff for a tenant (new orders)
///   session:{tenantId}:{sessionId}  - session participants (status updates)
///   station:{tenantId}:{stationId}  - KDS device (orders assigned to station)
/// <para>
/// <b>[Authorize]</b>: sin él, un WebSocket anónimo podía conectarse y llamar
/// <c>JoinTenantGroup(tenantId)</c> para recibir TODOS los eventos del ciclo por ítem
/// (ITEM_MARCHED, ITEM_READY, ITEMS_CLAIMED…) con nombres de plato, mesa y posición. Ahora el
/// handshake exige un token válido, y el tenant del grupo se toma de las claims del llamante, no
/// del parámetro: nadie se suscribe a los eventos de un tenant que no es el suyo.
/// </para>
/// </summary>
[Authorize]
public sealed class OrdersHub : Hub
{
    /// <summary>El tenant del que es el llamante, según su token ya validado (no el parámetro).</summary>
    private string? CallerTenant => Context.User?.FindFirst("tenantId")?.Value;

    public async Task JoinSessionGroup(string tenantId, string sessionId)
    {
        var caller = CallerTenant;
        if (caller is null || caller != tenantId) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{caller}:{sessionId}");
    }

    public async Task JoinTenantGroup(string tenantId)
    {
        var caller = CallerTenant;
        if (caller is null || caller != tenantId) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{caller}");
    }

    public async Task JoinStationGroup(string tenantId, string stationId)
    {
        var caller = CallerTenant;
        if (caller is null || caller != tenantId) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"station:{caller}:{stationId}");
    }
}
