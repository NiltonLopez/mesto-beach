namespace MestoBeach.Shared.Venue;

/// <summary>Redis y Postgres fallaron a la vez. Los consumidores la traducen a 503 VENUE_MODE_UNAVAILABLE.</summary>
public sealed class VenueModeUnavailableException : Exception
{
    public const string Code = "VENUE_MODE_UNAVAILABLE";

    public VenueModeUnavailableException(Guid tenantId, Exception inner)
        : base($"No se pudo leer el modo del tenant {tenantId}: Redis y Postgres fallaron.", inner) { }
}
