using MestoBeach.Orders.Domain.Repositories;
using MestoBeach.Sessions.Application.Common;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Orders.Infrastructure.Services;

/// <summary>
/// Real implementation of the Sessions module's IOrderSummaryService.
/// Registered in OrdersModule, overriding the stub in SessionsModule.
/// </summary>
public sealed class OrderSummaryService : IOrderSummaryService
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<OrderSummaryService> _logger;

    public OrderSummaryService(
        IOrderRepository orders,
        ILogger<OrderSummaryService> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<OrdersSummaryDto> GetSummaryAsync(Guid sessionId, CancellationToken ct = default)
    {
        var (totalItems, totalCents, pendingItems, deliveredItems) =
            await _orders.GetSessionSummaryAsync(sessionId, ct);

        _logger.LogDebug(
            "order_summary {SessionId} TotalItems={TotalItems} TotalCents={TotalCents}",
            sessionId, totalItems, totalCents);

        return new OrdersSummaryDto(totalItems, totalCents, pendingItems, deliveredItems);
    }
}
