namespace MestoBeach.Notifications.Domain.Entities;

/// <summary>
/// Email template with locale-specific subject and body.
/// Composite primary key: (Id, Locale).
/// Body supports {{variableName}} interpolation.
/// </summary>
public sealed class EmailTemplate
{
    public string Id { get; private set; } = string.Empty;
    public string Locale { get; private set; } = "es";
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    /// <summary>JSON array of variable names required in the body/subject, e.g. ["activationLink","restaurantName"].</summary>
    public List<string> RequiredVariables { get; private set; } = new();
    public DateTime CreatedAt { get; private set; }

    private EmailTemplate() { }

    public EmailTemplate(
        string id,
        string locale,
        string subject,
        string body,
        List<string>? requiredVariables = null)
    {
        Id = id;
        Locale = locale;
        Subject = subject;
        Body = body;
        RequiredVariables = requiredVariables ?? new();
        CreatedAt = DateTime.UtcNow;
    }
}
