using MestoBeach.Shared.Errors;

namespace MestoBeach.Shared.Plans;

/// <summary>
/// Builds the bloqueante <c>plan_limit_exceeded</c> error (HTTP 403) raised when a tenant
/// tries to create a resource beyond its plan's entitlements (PRD-017 §4 / SDD-009).
/// </summary>
public static class PlanLimits
{
    public static Error Exceeded(string resource, int limit, int current, string currentPlan)
    {
        var suggested = currentPlan == PlanCatalog.Basic ? PlanCatalog.Pro : PlanCatalog.Enterprise;
        var message =
            $"Alcanzaste el límite de tu plan ({resource}: {current}/{limit}). " +
            $"Sube a {suggested} para tener más.";

        return Error.ForbiddenWith("plan_limit_exceeded", message, new Dictionary<string, object?>
        {
            ["resource"] = resource,
            ["limit"] = limit,
            ["current"] = current,
            ["suggestedPlan"] = suggested,
        });
    }
}
