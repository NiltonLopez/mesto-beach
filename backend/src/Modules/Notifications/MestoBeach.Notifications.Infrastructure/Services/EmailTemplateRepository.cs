using MestoBeach.Notifications.Application.Common;
using MestoBeach.Notifications.Domain.Entities;
using MestoBeach.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MestoBeach.Notifications.Infrastructure.Services;

public sealed class EmailTemplateRepository : IEmailTemplateRepository
{
    private readonly NotificationsDbContext _db;

    public EmailTemplateRepository(NotificationsDbContext db) => _db = db;

    public async Task<EmailTemplate?> GetAsync(string templateId, string locale, CancellationToken ct)
    {
        // Try exact locale match first
        var template = await _db.EmailTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.Locale == locale, ct);

        // Fallback to "es" if the requested locale doesn't exist
        if (template is null && locale != "es")
            template = await _db.EmailTemplates
                .FirstOrDefaultAsync(t => t.Id == templateId && t.Locale == "es", ct);

        return template;
    }

    public async Task AddAuditAsync(AuditLog entry, CancellationToken ct) =>
        await _db.AuditLogs.AddAsync(entry, ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}
