namespace MestoBeach.Shared.Venue;

/// <summary>
/// Modo del local (ADR-036, SDD-001 §7). Vive en Shared porque lo leen Catalog, Sessions, Orders y
/// Analytics, que no pueden depender de Identity. En el wire siempre viaja como string en
/// mayúsculas (<c>mode.ToString()</c>); no hay <c>JsonStringEnumConverter</c> global.
/// </summary>
public enum VenueMode
{
    RESTAURANT,
    NIGHT
}
