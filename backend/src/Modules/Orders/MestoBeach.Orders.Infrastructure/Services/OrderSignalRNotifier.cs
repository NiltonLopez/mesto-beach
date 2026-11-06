using MestoBeach.Orders.Application.Common;
using MestoBeach.Orders.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Orders.Infrastructure.Services;

public sealed class OrderSignalRNotifier : IOrderSignalRNotifier
{
    private readonly IHubContext<OrdersHub> _hub;
    private readonly ILogger<OrderSignalRNotifier> _logger;

    public OrderSignalRNotifier(
        IHubContext<OrdersHub> hub,
        ILogger<OrderSignalRNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifySessionAsync(
        Guid tenantId, Guid sessionId, object payload, CancellationToken ct = default)
    {
        var group = $"session:{tenantId}:{sessionId}";
        await _hub.Clients.Group(group).SendAsync("OrderEvent", payload, ct);
        _logger.LogDebug("order.signalr.session {TenantId} {SessionId}", tenantId, sessionId);
    }

    public async Task NotifyTenantAsync(
        Guid tenantId, object payload, CancellationToken ct = default)
    {
        var group = $"tenant:{tenantId}";
        await _hub.Clients.Group(group).SendAsync("OrderEvent", payload, ct);
        _logger.LogDebug("order.signalr.tenant {TenantId}", tenantId);
    }

    public async Task NotifyStationAsync(
        Guid tenantId, Guid stationId, object payload, CancellationToken ct = default)
    {
        var group = $"station:{tenantId}:{stationId}";
        await _hub.Clients.Group(group).SendAsync("OrderEvent", payload, ct);
        _logger.LogDebug("order.signalr.station {TenantId} {StationId}", tenantId, stationId);
    }
}
