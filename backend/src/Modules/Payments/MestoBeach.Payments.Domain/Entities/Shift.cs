using MestoBeach.Payments.Domain.Enums;

namespace MestoBeach.Payments.Domain.Entities;

/// <summary>A cashier shift (cash drawer) for a tenant. Payments made while open are stamped with its id.</summary>
public sealed class Shift
{
    private Shift() { } // EF Core

    public Shift(Guid id, Guid tenantId, Guid openedBy, int openingFloatCents)
    {
        Id = id;
        TenantId = tenantId;
        OpenedBy = openedBy;
        OpeningFloatCents = openingFloatCents;
        Status = ShiftStatus.Open;
        OpenedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public ShiftStatus Status { get; private set; }
    public int OpeningFloatCents { get; private set; }
    public Guid OpenedBy { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public string? Note { get; private set; }

    public bool IsOpen => Status == ShiftStatus.Open;

    public void Close(Guid closedBy, string? note)
    {
        if (Status != ShiftStatus.Open)
            throw new InvalidOperationException("El turno ya está cerrado.");
        Status = ShiftStatus.Closed;
        ClosedBy = closedBy;
        ClosedAt = DateTime.UtcNow;
        Note = note;
    }
}
