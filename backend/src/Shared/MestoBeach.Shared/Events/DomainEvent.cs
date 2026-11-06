using MediatR;

namespace MestoBeach.Shared.Events;

/// <summary>
/// Marker interface for domain events. Implement MediatR's INotification
/// so events can be dispatched via the mediator pipeline.
/// </summary>
public interface IDomainEvent : INotification
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
}

/// <summary>
/// Base record for domain events. Provides EventId and timestamp automatically.
/// </summary>
public abstract record DomainEvent(Guid EventId, DateTime OccurredAt) : IDomainEvent
{
    protected DomainEvent() : this(Guid.NewGuid(), DateTime.UtcNow) { }
}
