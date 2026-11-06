using MestoBeach.Payments.Domain.Entities;
using MestoBeach.Payments.Domain.Repositories;
using MestoBeach.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MestoBeach.Payments.Infrastructure.Persistence.Repositories;

public sealed class PaymentConfigRepository : IPaymentConfigRepository
{
    private readonly PaymentsDbContext _db;
    public PaymentConfigRepository(PaymentsDbContext db) => _db = db;

    public Task<TenantPaymentConfig?> GetByTenantAsync(Guid tenantId, CancellationToken ct) =>
        _db.PaymentConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    public async Task UpsertAsync(TenantPaymentConfig config, CancellationToken ct)
    {
        var exists = await _db.PaymentConfigs.AnyAsync(c => c.TenantId == config.TenantId, ct);
        if (exists)
            _db.PaymentConfigs.Update(config);
        else
            await _db.PaymentConfigs.AddAsync(config, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}
