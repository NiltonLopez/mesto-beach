namespace MestoBeach.Shared.Errors;

public record Error(
    string Code,
    string Message,
    ErrorType Type = ErrorType.Failure,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>Forbidden with a machine-readable code + structured extensions (e.g. plan_limit_exceeded).</summary>
    public static Error ForbiddenWith(string code, string message, IReadOnlyDictionary<string, object?> extensions) =>
        new(code, message, ErrorType.Forbidden, extensions);

    public static Error NotFound(string message) =>
        new("NOT_FOUND", message, ErrorType.NotFound);

    public static Error Validation(string code, string message) =>
        new(code, message, ErrorType.Validation);

    public static Error Conflict(string message) =>
        new("CONFLICT", message, ErrorType.Conflict);

    public static Error Unauthorized(string message) =>
        new("UNAUTHORIZED", message, ErrorType.Unauthorized);

    public static Error Forbidden(string message) =>
        new("FORBIDDEN", message, ErrorType.Forbidden);

    public static Error TooManyRequests(string message) =>
        new("TOO_MANY_REQUESTS", message, ErrorType.TooManyRequests);

    public static Error Locked(string message) =>
        new("LOCKED", message, ErrorType.Locked);

    public static Error Gone(string message) =>
        new("GONE", message, ErrorType.Gone);

    public static Error UnprocessableEntity(string code, string message) =>
        new(code, message, ErrorType.UnprocessableEntity);

    public static Error ServiceUnavailable(string code, string message) =>
        new(code, message, ErrorType.ServiceUnavailable);

    public static Error Failure(string code, string message) =>
        new(code, message, ErrorType.Failure);
}
