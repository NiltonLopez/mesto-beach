namespace MestoBeach.Shared.Plans;

/// <summary>How Mesto Beach charges a tenant for a plan.</summary>
public enum BillingMode
{
    None,
    Subscription,
    Commission,
    Hybrid
}

/// <summary>
/// The capabilities/limits a plan grants. <c>null</c> on a limit means unlimited.
/// This is the catalog definition (per <c>planCode</c>), independent of any tenant.
/// </summary>
public sealed record PlanEntitlements(
    string PlanCode,        // "basic" | "pro" | "enterprise"
    string Name,
    BillingMode BillingMode,
    int? PriceCents,          // null = a medida (Enterprise)
    int? MaxFloors,         // null = ilimitado
    int? MaxKdsStations,
    int? MaxTables,
    int? MaxStaff,
    bool Loyalty,
    bool Branding,
    string Analytics);      // "basic" | "advanced"

/// <summary>
/// Code-defined plan catalog (PRD-017). The source of truth for what each plan
/// includes. Trial tenants get <c>pro</c> entitlements; Enterprise is unlimited
/// and may carry per-tenant price/commission overrides on the tenant row.
/// </summary>
public static class PlanCatalog
{
    public const string Basic = "basic";
    public const string Pro = "pro";
    public const string Enterprise = "enterprise";

    private static readonly IReadOnlyDictionary<string, PlanEntitlements> Plans =
        new Dictionary<string, PlanEntitlements>(StringComparer.OrdinalIgnoreCase)
        {
            [Basic] = new(Basic, "Básico", BillingMode.Subscription, 59_000,
                MaxFloors: 1, MaxKdsStations: 2, MaxTables: 20, MaxStaff: 5,
                Loyalty: true, Branding: false, Analytics: "basic"),

            [Pro] = new(Pro, "Pro", BillingMode.Subscription, 119_000,
                MaxFloors: null, MaxKdsStations: null, MaxTables: null, MaxStaff: null,
                Loyalty: true, Branding: true, Analytics: "advanced"),

            [Enterprise] = new(Enterprise, "Enterprise", BillingMode.Hybrid, PriceCents: null,
                MaxFloors: null, MaxKdsStations: null, MaxTables: null, MaxStaff: null,
                Loyalty: true, Branding: true, Analytics: "advanced"),
        };

    /// <summary>The default plan a new tenant starts on (trial gives Pro entitlements).</summary>
    public const string TrialPlanCode = Pro;

    public static IReadOnlyCollection<PlanEntitlements> All => (IReadOnlyCollection<PlanEntitlements>)Plans.Values;

    public static bool IsValid(string? code) => code is not null && Plans.ContainsKey(code);

    /// <summary>Entitlements for a plan code; falls back to Básico (most restrictive) if unknown.</summary>
    public static PlanEntitlements For(string? code) =>
        code is not null && Plans.TryGetValue(code, out var p) ? p : Plans[Basic];
}
