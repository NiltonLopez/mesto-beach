using FluentValidation;
using MediatR;
using MestoBeach.Notifications.Application.Common;
using MestoBeach.Notifications.Domain.Entities;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using System.Text.RegularExpressions;

namespace MestoBeach.Notifications.Application.Commands;

// ── Send Email ────────────────────────────────────────────────────────────────

public record SendEmailCommand(
    string TemplateId,
    string RecipientEmail,
    string? RecipientName,
    Dictionary<string, string> Variables,
    string Locale) : IRequest<Result<SendEmailResult>>;

public record SendEmailResult(
    string MessageId,
    string TemplateId,
    string Recipient,
    string Status,
    DateTime EnqueuedAt);

public sealed class SendEmailValidator : AbstractValidator<SendEmailCommand>
{
    public SendEmailValidator()
    {
        RuleFor(c => c.TemplateId).NotEmpty();
        RuleFor(c => c.RecipientEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);
        RuleFor(c => c.RecipientName).MaximumLength(100).When(c => c.RecipientName is not null);
        RuleFor(c => c.Locale)
            .Must(l => l is "es" or "en")
            .WithMessage("locale debe ser 'es' o 'en'.")
            .When(c => c.Locale is not null);
    }
}

public sealed class SendEmailHandler : IRequestHandler<SendEmailCommand, Result<SendEmailResult>>
{
    private readonly IEmailTemplateRepository _repo;
    private readonly IEmailSender _sender;

    public SendEmailHandler(IEmailTemplateRepository repo, IEmailSender sender)
    {
        _repo = repo;
        _sender = sender;
    }

    public async Task<Result<SendEmailResult>> Handle(SendEmailCommand cmd, CancellationToken ct)
    {
        var locale = cmd.Locale ?? "es";
        var template = await _repo.GetAsync(cmd.TemplateId, locale, ct);
        if (template is null)
            return Result.Failure<SendEmailResult>(Error.NotFound("Template de email no encontrado."));

        // Verify required variables are present
        var missing = template.RequiredVariables
            .Where(v => !cmd.Variables.ContainsKey(v))
            .ToList();
        if (missing.Count > 0)
            return Result.Failure<SendEmailResult>(
                Error.Validation("MISSING_VARIABLES",
                    $"Variables requeridas ausentes: {string.Join(", ", missing)}"));

        var subject = Interpolate(template.Subject, cmd.Variables);
        var body    = Interpolate(template.Body, cmd.Variables);

        var messageId = await _sender.SendAsync(
            new EmailRecipient(cmd.RecipientEmail, cmd.RecipientName),
            subject, body, ct);

        // Audit
        var audit = new AuditLog(
            "email.queued",
            null,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                templateId = cmd.TemplateId,
                recipientEmail = cmd.RecipientEmail,
                locale,
                messageId
            }));
        await _repo.AddAuditAsync(audit, ct);
        await _repo.SaveChangesAsync(ct);

        return Result.Success(new SendEmailResult(
            messageId,
            cmd.TemplateId,
            cmd.RecipientEmail,
            "QUEUED",
            DateTime.UtcNow));
    }

    private static string Interpolate(string template, Dictionary<string, string> vars)
    {
        foreach (var (key, value) in vars)
            template = template.Replace("{{" + key + "}}", value);
        return template;
    }
}
