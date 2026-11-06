using MestoBeach.Payments.Application.Common;
using MestoBeach.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MestoBeach.Payments.Infrastructure.Services;

public sealed class SessionOrderReaderService : ISessionOrderReader
{
    private readonly PaymentsDbContext _db;
    public SessionOrderReaderService(PaymentsDbContext db) => _db = db;

    public async Task<SessionOrdersTotal> GetTotalAsync(
        Guid sessionId, CancellationToken ct = default)
    {
        // Join orders (non-cancelled) with order_items to compute total
        var total = await (
            from o in _db.Orders.AsNoTracking()
            join i in _db.OrderItems.AsNoTracking() on o.Id equals i.OrderId
            where o.SessionId == sessionId && o.Status != "CANCELLED"
            select i.ItemPriceSnapshotCents * i.Quantity
        ).SumAsync(ct);

        int orderCount = await _db.Orders.AsNoTracking()
            .CountAsync(o => o.SessionId == sessionId && o.Status != "CANCELLED", ct);

        return new SessionOrdersTotal(total, orderCount);
    }
}
