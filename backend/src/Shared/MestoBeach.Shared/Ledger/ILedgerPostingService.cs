namespace MestoBeach.Shared.Ledger;

/// <summary>Chart-of-accounts codes used by Mesto Beach's SaaS ledger (PRD-017 / SDD-010).</summary>
public static class LedgerAccounts
{
    public const string Banks = "1110";                 // Bancos / Caja (Activo)
    public const string AccountsReceivable = "1305";    // Cuentas por cobrar — tenants (Activo)
    public const string AccountsPayable = "2335";       // Cuentas por pagar (Pasivo)
    public const string VatPayable = "2408";            // IVA por pagar (Pasivo)
    public const string IncomeSubscription = "4170";    // Ingresos por suscripción (Ingreso)
    public const string IncomeCommission = "4175";      // Ingresos por comisión (Ingreso)
    public const string Discounts = "5299";             // Descuentos y cortesías (Gasto)
    public const string LoyaltyExpense = "5305";        // Gasto por fidelización — settlement (Gasto)
}

/// <summary>Kinds of business event a journal entry records (idempotency key with <c>Reference</c>).</summary>
public static class LedgerEntryTypes
{
    public const string InvoiceIssued = "INVOICE_ISSUED";
    public const string PaymentReceived = "PAYMENT_RECEIVED";
    public const string DiscountApplied = "DISCOUNT_APPLIED";
    public const string InvoiceVoided = "INVOICE_VOIDED";
    public const string LoyaltySettlement = "LOYALTY_SETTLEMENT";
    public const string SettlementPaid = "SETTLEMENT_PAID";
}

/// <summary>One leg of an entry: a debit XOR a credit (the other is 0) against an account.</summary>
public sealed record LedgerLine(string AccountCode, long DebitCents, long CreditCents)
{
    public static LedgerLine Debit(string accountCode, long cents) => new(accountCode, cents, 0);
    public static LedgerLine Credit(string accountCode, long cents) => new(accountCode, 0, cents);
}

/// <summary>A balanced journal entry to post. Σ debits must equal Σ credits.</summary>
public sealed record LedgerEntry(
    Guid? TenantId,
    string EntryType,
    string Reference,       // idempotency: invoice/payment/discount/settlement id
    string? Period,         // 'YYYY-MM'
    string? Description,
    long AmountCents,         // principal amount (for listings; detail is in Lines)
    IReadOnlyList<LedgerLine> Lines,
    Guid? CreatedBy = null);

/// <summary>
/// Posts balanced double-entry journal entries (SDD-010). Implemented by the Ledger
/// module; the interface lives in Shared so Platform/Loyalty can post without
/// depending on Ledger. Idempotent by (EntryType, Reference): posting the same
/// business event twice is a no-op.
/// </summary>
public interface ILedgerPostingService
{
    /// <summary>Posts an entry. Throws if it doesn't balance (Σ debits != Σ credits) or has &lt; 2 lines.</summary>
    Task PostAsync(LedgerEntry entry, CancellationToken ct = default);
}
