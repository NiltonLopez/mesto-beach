using MestoBeach.Sessions.Domain.Enums;
using MestoBeach.Shared.Venue;

namespace MestoBeach.Sessions.Domain.Entities;

/// <summary>An active or closed dining session for a table.</summary>
public sealed class Session
{
    private Session() { } // EF Core

    public Session(
        Guid id,
        Guid tenantId,
        Guid tableId,
        string tableCode,
        QrMode qrMode,
        ServiceMode serviceMode,
        bool pinRequired,
        Guid? waiterId,
        Guid? openedBy,
        Guid? shiftId,
        int? guestCount = null,
        VenueMode venueMode = VenueMode.RESTAURANT)
    {
        Id = id;
        TenantId = tenantId;
        TableId = tableId;
        TableCode = tableCode;
        Status = SessionStatus.Open;
        QrMode = qrMode;
        ServiceMode = serviceMode;
        PinRequired = pinRequired;
        WaiterId = waiterId;
        OpenedBy = openedBy;
        ShiftId = shiftId;
        GuestCount = guestCount;
        VenueMode = venueMode;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TableId { get; private set; }
    public string TableCode { get; private set; } = string.Empty;
    public SessionStatus Status { get; private set; }
    public QrMode QrMode { get; private set; }
    public ServiceMode ServiceMode { get; private set; }
    public bool PinRequired { get; private set; }
    public Guid? WaiterId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? OpenedBy { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }
    public Guid? ShiftId { get; private set; }

    /// <summary>
    /// Comensales que la Host registró al sentar la mesa (RF-1840). Alimenta la ocupación por
    /// sector (RF-1841). Nullable: en autoservicio no lo pone nadie, y la ocupación cae entonces
    /// a "mesa ocupada" sin número de personas.
    /// </summary>
    public int? GuestCount { get; private set; }

    /// <summary>Modo del local con el que se abrió la sesión (snapshot, ADR-040). Nunca cambia.</summary>
    public VenueMode VenueMode { get; private set; }

    public bool IsOpen => Status == SessionStatus.Open;

    public void Close(Guid closedBy)
    {
        Status = SessionStatus.Closed;
        ClosedAt = DateTime.UtcNow;
        ClosedBy = closedBy;
    }

    public void AssignWaiter(Guid? waiterId) => WaiterId = waiterId;

    /// <summary>La Host corrige el número de comensales de una mesa ya sentada (RF-1840).</summary>
    public void SetGuestCount(int? guestCount) => GuestCount = guestCount;
}
