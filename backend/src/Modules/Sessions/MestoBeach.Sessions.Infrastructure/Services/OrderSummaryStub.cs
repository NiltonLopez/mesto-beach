using MestoBeach.Sessions.Application.Common;

namespace MestoBeach.Sessions.Infrastructure.Services;

/// <summary>
/// Stub implementation until the Orders module (SDD-004) is built.
/// Returns zero-valued summary for all sessions.
/// </summary>
public sealed class OrderSummaryStub : IOrderSummaryService
{
    public Task<OrdersSummaryDto> GetSummaryAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new OrdersSummaryDto(0, 0, 0, 0));
}
