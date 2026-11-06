namespace MestoBeach.Notifications.Domain.Entities;

/// <summary>
/// Append-only audit record used by the Notifications module.
/// Also consumed by the Platform module for maintenance set operations.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public Guid? PerformedBy { get; private set; }
    public DateTime Timestamp { get; private set; }
    /// <summary>JSON blob with request-specific context.</summary>
    public string Payload { get; private set; } = "{}";

    private AuditLog() { }

    public AuditLog(string action, Guid? performedBy, string payload)
    {
        Id = Guid.NewGuid();
        Action = action;
        PerformedBy = performedBy;
        Timestamp = DateTime.UtcNow;
        Payload = payload;
    }
}
