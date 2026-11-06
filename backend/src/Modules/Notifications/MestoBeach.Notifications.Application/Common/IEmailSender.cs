namespace MestoBeach.Notifications.Application.Common;

public record EmailRecipient(string Email, string? Name);

public interface IEmailSender
{
    /// <summary>
    /// Sends an email with pre-rendered subject and body.
    /// Returns the provider's message ID.
    /// </summary>
    Task<string> SendAsync(
        EmailRecipient recipient,
        string subject,
        string htmlBody,
        CancellationToken ct);
}
