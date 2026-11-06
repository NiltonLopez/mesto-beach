using MestoBeach.Payments.Domain.Entities;
using MestoBeach.Payments.Domain.Enums;

namespace MestoBeach.Payments.Domain.Repositories;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken ct = default);

    Task<List<Payment>> GetBySessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Returns existing split payment for (sessionId, splitIndex), or null.</summary>
    Task<Payment?> GetSplitPaymentAsync(Guid sessionId, int splitIndex, CancellationToken ct = default);

    /// <summary>
    /// Counts completed payments for the session with split tracking.
    /// Returns the count of completed splits.
    /// </summary>
    Task<int> CountCompletedSplitsAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Completed payments stamped with the given shift (for the cash report).</summary>
    Task<List<Payment>> GetByShiftAsync(Guid shiftId, CancellationToken ct = default);

    /// <summary>Recent payments for a tenant within a UTC date range (newest first), for the sales list.</summary>
    Task<List<Payment>> GetByTenantAsync(Guid tenantId, DateTime fromUtc, DateTime toExclusive, int limit, CancellationToken ct = default);

    Task AddAsync(Payment payment, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IShiftRepository
{
    Task<Shift?> GetByIdAsync(Guid shiftId, CancellationToken ct = default);

    /// <summary>The currently open shift for the tenant, or null.</summary>
    Task<Shift?> GetOpenByTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(Shift shift, CancellationToken ct = default);
    Task UpdateAsync(Shift shift, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IPaymentConfigRepository
{
    Task<TenantPaymentConfig?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Upserts the config for a tenant.</summary>
    Task UpsertAsync(TenantPaymentConfig config, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
