namespace MestoBeach.Shared.Primitives;

/// <summary>
/// Base class for all domain entities. Uses strongly-typed Guid IDs.
/// </summary>
public abstract class Entity
{
    protected Entity(Guid id) => Id = id;

    public Guid Id { get; private set; }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other) return false;
        if (obj.GetType() != GetType()) return false;
        return Id == other.Id;
    }

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Entity? left, Entity? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(Entity? left, Entity? right) => !(left == right);
}
