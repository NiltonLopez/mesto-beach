using MestoBeach.Notifications.Domain.Entities;

namespace MestoBeach.Notifications.Application.Common;

public interface IEmailTemplateRepository
{
    /// <summary>
    /// Returns the template for the requested locale; falls back to "es" if not found.
    /// Returns null if the template doesn't exist in any locale.
    /// </summary>
    Task<EmailTemplate?> GetAsync(string templateId, string locale, CancellationToken ct);

    Task AddAuditAsync(AuditLog entry, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
