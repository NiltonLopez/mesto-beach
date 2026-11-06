using MestoBeach.Sessions.Application.Common;
using Microsoft.EntityFrameworkCore;
using MestoBeach.Sessions.Infrastructure.Persistence;

namespace MestoBeach.Sessions.Infrastructure.Services;

public sealed class TenantSettingsService : ITenantSettingsService
{
    private readonly SessionsDbContext _db;

    public TenantSettingsService(SessionsDbContext db) => _db = db;

    public async Task<TenantSettings?> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        return t is null ? null : new TenantSettings(t.Id, t.Slug, t.PinRequired);
    }
}
