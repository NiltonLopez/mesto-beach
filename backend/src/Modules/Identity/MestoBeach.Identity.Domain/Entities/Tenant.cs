using MestoBeach.Identity.Domain.Enums;
using MestoBeach.Shared.Plans;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;

namespace MestoBeach.Identity.Domain.Entities;

/// <summary>
/// Represents a restaurant on the Mesto Beach platform.
/// </summary>
public sealed class Tenant : AggregateRoot
{
    private Tenant() : base(Guid.Empty) { } // EF Core

    public Tenant(
        Guid id,
        string slug,
        string name,
        TenantPlan plan,
        string timezone = "America/Bogota") : base(id)
    {
        Slug = slug.ToLowerInvariant();
        Name = name;
        Plan = plan;
        PlanCode = PlanCatalog.TrialPlanCode; // trial → Pro entitlements (PRD-017)
        Status = TenantStatus.Trial;
        QrMode = QrMode.Fixed;
        OperationMode = OperationMode.Staffed;
        PinRequired = true;
        Timezone = timezone;
        CreatedAt = DateTime.UtcNow;
    }

    public string Slug { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public TenantPlan Plan { get; private set; }                 // billing mode (None/Subscription/Commission/Hybrid)
    public string PlanCode { get; private set; } = PlanCatalog.TrialPlanCode; // plan tier (basic/pro/enterprise) — PRD-017
    public TenantStatus Status { get; private set; }
    public QrMode QrMode { get; private set; }
    public OperationMode OperationMode { get; private set; }
    public bool PinRequired { get; private set; }
    public string Timezone { get; private set; } = string.Empty;
    public string? LogoUrl { get; private set; }
    public string? PrimaryColor { get; private set; }
    public string? Nit { get; private set; }
    public bool NitVerified { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ActivatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // ── Platform admin columns ─────────────────────────────────────────────────
    public string? City { get; private set; }
    public string? OwnerEmail { get; private set; }
    public string? InternalNotes { get; private set; }
    public decimal? CommissionRate { get; private set; }
    public int? SubscriptionAmountCents { get; private set; }
    public DateTime? TrialEndsAt { get; private set; }
    public Guid? CreatedBy { get; private set; }

    // ── Modo del local (ADR-036, SDD-001 §7) ───────────────────────────────────
    /// <summary>Fuente de verdad del modo; Redis (<c>venue:{tenantId}:mode</c>) es solo caché.</summary>
    public VenueMode VenueMode { get; private set; } = VenueMode.RESTAURANT;
    /// <summary>Último cambio efectivo. Null = nunca ha salido de RESTAURANT.</summary>
    public DateTime? VenueModeChangedAt { get; private set; }

    public bool IsActive => Status == TenantStatus.Active || Status == TenantStatus.Trial;

    public void Activate()
    {
        Status = TenantStatus.Active;
        ActivatedAt = DateTime.UtcNow;
    }

    public void Suspend() => Status = TenantStatus.Suspended;

    public void SetOperationConfig(QrMode qrMode, OperationMode operationMode, bool pinRequired)
    {
        // DYNAMIC + SELF_SERVICE is invalid (ADR)
        if (qrMode == QrMode.Dynamic && operationMode == OperationMode.SelfService)
            throw new InvalidOperationException("DYNAMIC QR mode is incompatible with SELF_SERVICE operation mode.");

        QrMode = qrMode;
        OperationMode = operationMode;
        PinRequired = pinRequired;
    }

    /// <summary>Sets the plan tier (Ops via ChangePlan). Validated against the catalog by the caller.</summary>
    public void SetPlanCode(string planCode)
    {
        PlanCode = planCode;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Cambio efectivo de modo. El pre-flight y la auditoría son del handler.</summary>
    public void ChangeVenueMode(VenueMode mode, DateTime now)
    {
        VenueMode = mode;
        VenueModeChangedAt = now;
        UpdatedAt = now;
    }

    public void SetNit(string nit) => Nit = nit;

    public void VerifyNit() => NitVerified = true;

    public void UpdateBranding(string? logoUrl, string? primaryColor)
    {
        LogoUrl = logoUrl;
        PrimaryColor = primaryColor;
    }

    public void SetPlatformDetails(
        string? city,
        string? ownerEmail,
        string? internalNotes,
        decimal? commissionRate,
        int? subscriptionAmountCents,
        DateTime? trialEndsAt)
    {
        if (city is not null) City = city;
        if (ownerEmail is not null) OwnerEmail = ownerEmail;
        if (internalNotes is not null) InternalNotes = internalNotes;
        if (commissionRate.HasValue) CommissionRate = commissionRate;
        if (subscriptionAmountCents.HasValue) SubscriptionAmountCents = subscriptionAmountCents;
        if (trialEndsAt.HasValue) TrialEndsAt = trialEndsAt;
        UpdatedAt = DateTime.UtcNow;
    }
}
