using MestoBeach.Payments.Domain.Enums;

namespace MestoBeach.Payments.Domain.Entities;

/// <summary>Represents a single payment record for a session.</summary>
public sealed class Payment
{
    private Payment() { } // EF Core

    public Payment(
        Guid id,
        Guid tenantId,
        Guid sessionId,
        PaymentMethod method,
        int amountCents,
        int tipCents,
        int commissionCents,
        string? gatewayReference,
        string? gatewayType,
        int? splitIndex,
        int? splitTotal,
        Guid processedBy,
        string? payerLabel = null)
    {
        Id = id;
        TenantId = tenantId;
        SessionId = sessionId;
        Method = method;
        AmountCents = amountCents;
        TipCents = tipCents;
        TotalCents = amountCents + tipCents;
        CommissionCents = commissionCents;
        GatewayReference = gatewayReference;
        GatewayType = gatewayType;
        Status = method == PaymentMethod.Cash ? PaymentStatus.Completed : PaymentStatus.Pending;
        SplitIndex = splitIndex;
        SplitTotal = splitTotal;
        ProcessedBy = processedBy;
        PayerLabel = string.IsNullOrWhiteSpace(payerLabel) ? null : payerLabel.Trim();
        CreatedAt = DateTime.UtcNow;
        if (method == PaymentMethod.Cash) CompletedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SessionId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public int AmountCents { get; private set; }
    public int TipCents { get; private set; }
    public int TotalCents { get; private set; }
    public int CommissionCents { get; private set; }
    public string? GatewayReference { get; private set; }
    public string? GatewayType { get; private set; }
    public PaymentStatus Status { get; private set; }
    public int? SplitIndex { get; private set; }
    public int? SplitTotal { get; private set; }
    /// <summary>Who this payment is for (e.g. the person's name in a per-person charge). Display only.</summary>
    public string? PayerLabel { get; private set; }
    public Guid ProcessedBy { get; private set; }
    public Guid? ShiftId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public bool IsFullPayment => SplitIndex is null;

    /// <summary>Associates this payment with the cashier shift open at the time of payment.</summary>
    public void AssignShift(Guid shiftId) => ShiftId = shiftId;

    public void Complete()
    {
        if (Status == PaymentStatus.Completed) return;
        Status = PaymentStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void Fail()
    {
        if (Status == PaymentStatus.Completed)
            throw new InvalidOperationException("No se puede fallar un pago ya completado.");
        Status = PaymentStatus.Failed;
    }

    public void Refund()
    {
        if (Status != PaymentStatus.Completed)
            throw new InvalidOperationException("Solo se pueden reembolsar pagos completados.");
        Status = PaymentStatus.Refunded;
    }
}
