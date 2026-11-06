using MestoBeach.Payments.Domain.Entities;
using MestoBeach.Payments.Domain.Enums;
using MestoBeach.Payments.Domain.Repositories;
using MestoBeach.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MestoBeach.Payments.Infrastructure.Persistence.Repositories;

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly PaymentsDbContext _db;
    public PaymentRepository(PaymentsDbContext db) => _db = db;

    public Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken ct) =>
        _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);

    public Task<List<Payment>> GetBySessionAsync(Guid sessionId, CancellationToken ct) =>
        _db.Payments
            .Where(p => p.SessionId == sessionId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

    public Task<Payment?> GetSplitPaymentAsync(Guid sessionId, int splitIndex, CancellationToken ct) =>
        _db.Payments.FirstOrDefaultAsync(
            p => p.SessionId == sessionId && p.SplitIndex == splitIndex, ct);

    public Task<int> CountCompletedSplitsAsync(Guid sessionId, CancellationToken ct) =>
        _db.Payments.CountAsync(
            p => p.SessionId == sessionId &&
                 p.SplitIndex != null &&
                 p.Status == PaymentStatus.Completed, ct);

    public Task<List<Payment>> GetByShiftAsync(Guid shiftId, CancellationToken ct) =>
        _db.Payments
            .Where(p => p.ShiftId == shiftId && p.Status == PaymentStatus.Completed)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

    public Task<List<Payment>> GetByTenantAsync(
        Guid tenantId, DateTime fromUtc, DateTime toExclusive, int limit, CancellationToken ct) =>
        _db.Payments
            .Where(p => p.TenantId == tenantId && p.CreatedAt >= fromUtc && p.CreatedAt < toExclusive)
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task AddAsync(Payment payment, CancellationToken ct) =>
        await _db.Payments.AddAsync(payment, ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}

public sealed class ShiftRepository : IShiftRepository
{
    private readonly PaymentsDbContext _db;
    public ShiftRepository(PaymentsDbContext db) => _db = db;

    public Task<Shift?> GetByIdAsync(Guid shiftId, CancellationToken ct) =>
        _db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct);

    public Task<Shift?> GetOpenByTenantAsync(Guid tenantId, CancellationToken ct) =>
        _db.Shifts.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Status == ShiftStatus.Open, ct);

    public async Task AddAsync(Shift shift, CancellationToken ct) =>
        await _db.Shifts.AddAsync(shift, ct);

    public Task UpdateAsync(Shift shift, CancellationToken ct)
    {
        _db.Shifts.Update(shift);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}
