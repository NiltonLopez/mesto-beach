namespace MestoBeach.Shared.Plans;

/// <summary>
/// The resolved entitlements of a concrete tenant: the catalog values for its
/// <c>planCode</c>, with any Enterprise per-tenant overrides applied.
/// </summary>
public sealed record TenantEntitlements(
    Guid TenantId,
    string PlanCode,
    BillingMode BillingMode,
    int? MaxFloors,
    int? MaxKdsStations,
    int? MaxTables,
    int? MaxStaff,
    bool Loyalty,
    bool Branding,
    string Analytics)
{
    /// <summary>True if <paramref name="current"/> already reaches the limit for an unlimited-aware check.</summary>
    public bool ExceedsFloors(int current) => MaxFloors is int max && current >= max;
    public bool ExceedsKdsStations(int current) => MaxKdsStations is int max && current >= max;
    public bool ExceedsTables(int current) => MaxTables is int max && current >= max;
    public bool ExceedsStaff(int current) => MaxStaff is int max && current >= max;
}

/// <summary>
/// Cross-module reader (interface in Shared; implemented by Identity, which owns the
/// <c>tenants</c> table). Modules that gate resources (Sessions, Catalog, Identity)
/// inject this to enforce plan limits without depending on the Platform module.
/// </summary>
public interface IPlanEntitlementsReader
{
    /// <summary>Resolved entitlements for a tenant, or <c>null</c> if the tenant doesn't exist.</summary>
    Task<TenantEntitlements?> GetAsync(Guid tenantId, CancellationToken ct = default);
}
