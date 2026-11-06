using MediatR;
using Microsoft.Extensions.Logging;
using MestoBeach.Catalog.Application.Common;
using MestoBeach.Shared.Venue;

namespace MestoBeach.Catalog.Application.Venue;

/// <summary>
/// Invalida <c>catalog:{t}</c> al cambiar el modo del local (SDD-002, ADR-036/041). Defensivo:
/// la caché se guarda sin filtrar por modo, pero así ningún <c>visibleIn</c> editado en el modo
/// anterior sobrevive al cambio. Vive en Application porque CatalogModule solo registra este
/// ensamblado en MediatR. Best-effort.
/// </summary>
public sealed class VenueModeChangedCacheHandler : INotificationHandler<VenueModeChangedNotification>
{
    private readonly ICatalogCacheService _cache;
    private readonly ILogger<VenueModeChangedCacheHandler> _logger;

    public VenueModeChangedCacheHandler(ICatalogCacheService cache, ILogger<VenueModeChangedCacheHandler> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task Handle(VenueModeChangedNotification n, CancellationToken ct)
    {
        try
        {
            await _cache.InvalidateAsync(n.TenantId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "venue_mode.notify_failed {TenantId} {Handler}",
                n.TenantId, nameof(VenueModeChangedCacheHandler));
        }
    }
}
