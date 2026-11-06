namespace MestoBeach.Payments.Domain.Entities;

/// <summary>Per-tenant payment configuration. 1:1 with tenant.</summary>
public sealed class TenantPaymentConfig
{
    private TenantPaymentConfig() { } // EF Core

    /// <summary>Creates a new config with defaults.</summary>
    public static TenantPaymentConfig CreateDefault(Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            DigitalPaymentsEnabled = false,
            GatewayType = null,
            GatewayPublicKey = null,
            GatewayPrivateKeyRef = null,
            TipEnabled = true,
            TipOptions = [5, 10, 15],
            CommissionPct = 3.5m,
            UpdatedAt = DateTime.UtcNow
        };

    public Guid TenantId { get; private set; }
    public bool DigitalPaymentsEnabled { get; private set; }
    public string? GatewayType { get; private set; }
    public string? GatewayPublicKey { get; private set; }
    /// <summary>Name of the Azure Key Vault secret holding the gateway private key.</summary>
    public string? GatewayPrivateKeyRef { get; private set; }
    public bool TipEnabled { get; private set; }
    public List<int> TipOptions { get; private set; } = [];
    /// <summary>Mesto Beach commission percentage on AmountCents (not TipCents).</summary>
    public decimal CommissionPct { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void UpdateTip(bool? tipEnabled, List<int>? tipOptions)
    {
        if (tipEnabled.HasValue) TipEnabled = tipEnabled.Value;
        if (tipOptions is not null) TipOptions = tipOptions;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Only callable by Platform Admin.</summary>
    public void UpdateCommission(decimal commissionPct)
    {
        if (commissionPct < 0 || commissionPct > 100)
            throw new ArgumentOutOfRangeException(nameof(commissionPct));
        CommissionPct = commissionPct;
        UpdatedAt = DateTime.UtcNow;
    }
}
