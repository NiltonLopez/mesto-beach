using MestoBeach.Identity.Application.Common;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Identity.Infrastructure.Services;

/// <summary>
/// Stub email service that logs messages to the console.
/// Replace with a real SMTP/SendGrid implementation before going to production.
/// </summary>
public sealed class EmailNotificationService : IEmailNotificationService
{
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(ILogger<EmailNotificationService> logger)
        => _logger = logger;

    public Task SendStaffActivationEmailAsync(
        string toEmail, string name, string activationUrl, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[EMAIL] Staff activation → {Email} | Name: {Name} | URL: {Url}",
            toEmail, name, activationUrl);
        return Task.CompletedTask;
    }

    public Task SendStaffPasswordResetEmailAsync(
        string toEmail, string name, string resetUrl, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[EMAIL] Staff password reset → {Email} | Name: {Name} | URL: {Url}",
            toEmail, name, resetUrl);
        return Task.CompletedTask;
    }

    public Task SendCustomerPasswordResetEmailAsync(
        string toEmail, string name, string resetUrl, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[EMAIL] Customer password reset → {Email} | Name: {Name} | URL: {Url}",
            toEmail, name, resetUrl);
        return Task.CompletedTask;
    }
}
