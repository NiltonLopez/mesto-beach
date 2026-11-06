using MestoBeach.Identity.Infrastructure.Persistence;
using MestoBeach.Shared.Plans;
using Microsoft.EntityFrameworkCore;

namespace MestoBeach.Identity.Infrastructure.Services;

/// <summary>
/// Resolves a tenant's plan entitlements from its <c>plan_code</c> + the catalog.
/// Implementation lives in Identity (owner of the <c>tenants</c> table); the
/// <see cref="IPlanEntitlementsReader"/> interface lives in Shared so any module
/// (Sessions, Catalog, …) can enforce limits without depending on Platform.
/// </summary>
public sealed class PlanEntitlementsReader : IPlanEntitlementsReader
{
    private readonly IdentityDbContext _db;
    public PlanEntitlementsReader(IdentityDbContext db) => _db = db;

    public async Task<TenantEntitlements?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.PlanCode })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        var plan = PlanCatalog.For(row.PlanCode);
        return new TenantEntitlements(
            tenantId,
            plan.PlanCode,
            plan.BillingMode,
            plan.MaxFloors,
            plan.MaxKdsStations,
            plan.MaxTables,
            plan.MaxStaff,
            plan.Loyalty,
            plan.Branding,
            plan.Analytics);
    }
}
