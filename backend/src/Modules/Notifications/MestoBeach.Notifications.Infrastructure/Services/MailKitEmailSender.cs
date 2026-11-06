using MestoBeach.Notifications.Application.Common;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MestoBeach.Notifications.Infrastructure.Services;

public sealed class MailKitOptions
{
    public string Host        { get; set; } = "localhost";
    public int    Port        { get; set; } = 1025;
    public string FromAddress { get; set; } = "noreply@mestobeach.local";
    public string FromName    { get; set; } = "Mesto Beach";
    public string? Username   { get; set; }
    public string? Password   { get; set; }
    public bool   UseSsl      { get; set; } = false;
}

public sealed class MailKitEmailSender : IEmailSender
{
    private readonly MailKitOptions _opts;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(IOptions<MailKitOptions> opts, ILogger<MailKitEmailSender> logger)
    {
        _opts   = opts.Value;
        _logger = logger;
    }

    public async Task<string> SendAsync(
        EmailRecipient recipient,
        string subject,
        string htmlBody,
        CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
        msg.To.Add(new MailboxAddress(recipient.Name ?? recipient.Email, recipient.Email));
        msg.Subject = subject;
        msg.Body    = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();

        var socketOpts = _opts.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.None;

        await client.ConnectAsync(_opts.Host, _opts.Port, socketOpts, ct);

        if (!string.IsNullOrWhiteSpace(_opts.Username))
            await client.AuthenticateAsync(_opts.Username, _opts.Password, ct);

        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);

        var messageId = msg.MessageId;
        _logger.LogInformation(
            "email.queued templateId=unknown recipient={Recipient} messageId={MessageId} provider=mailkit",
            recipient.Email, messageId);

        return messageId;
    }
}
