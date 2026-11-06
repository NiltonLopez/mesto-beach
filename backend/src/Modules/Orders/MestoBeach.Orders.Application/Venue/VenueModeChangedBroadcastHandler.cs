using MediatR;
using Microsoft.Extensions.Logging;
using MestoBeach.Orders.Application.Common;
using MestoBeach.Shared.Venue;

namespace MestoBeach.Orders.Application.Venue;

/// <summary>
/// Difunde <c>VENUE_MODE_CHANGED</c> por OrdersHub al grupo <c>tenant:{t}</c> (SDD-004 §Eventos
/// SignalR, ADR-036). Vive en Application porque OrdersModule solo registra este ensamblado en
/// MediatR. Best-effort: atrapa y registra su propio fallo.
/// </summary>
public sealed class VenueModeChangedBroadcastHandler : INotificationHandler<VenueModeChangedNotification>
{
    private readonly IOrderSignalRNotifier _notifier;
    private readonly ILogger<VenueModeChangedBroadcastHandler> _logger;

    public VenueModeChangedBroadcastHandler(IOrderSignalRNotifier notifier, ILogger<VenueModeChangedBroadcastHandler> logger)
    {
        _notifier = notifier;
        _logger = logger;
    }

    public async Task Handle(VenueModeChangedNotification n, CancellationToken ct)
    {
        try
        {
            await _notifier.NotifyTenantAsync(n.TenantId, new
            {
                type = "VENUE_MODE_CHANGED",
                mode = n.Mode.ToString(),
                previous = n.Previous.ToString(),
                changedAt = n.ChangedAt,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "venue_mode.notify_failed {TenantId} {Handler}",
                n.TenantId, nameof(VenueModeChangedBroadcastHandler));
        }
    }
}
