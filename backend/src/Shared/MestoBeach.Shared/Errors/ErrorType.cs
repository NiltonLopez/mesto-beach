namespace MestoBeach.Shared.Errors;

public enum ErrorType
{
    None,
    Failure,
    NotFound,
    Validation,
    Conflict,
    Unauthorized,
    Forbidden,
    TooManyRequests,
    Locked,
    Gone,
    /// <summary>422: petición válida que el estado actual no permite procesar (p. ej. NAME_REQUIRED).</summary>
    UnprocessableEntity,
    /// <summary>503: una dependencia no responde (p. ej. VENUE_MODE_UNAVAILABLE).</summary>
    ServiceUnavailable
}
