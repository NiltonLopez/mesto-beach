using MestoBeach.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace MestoBeach.Notifications.Infrastructure.Persistence.Configurations;

public sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    private static readonly JsonSerializerOptions JsonOpts = new();

    public void Configure(EntityTypeBuilder<EmailTemplate> b)
    {
        b.ToTable("email_templates");
        b.HasKey(t => new { t.Id, t.Locale });
        b.Property(t => t.Id).HasColumnName("id").HasMaxLength(50);
        b.Property(t => t.Locale).HasColumnName("locale").HasMaxLength(5);
        b.Property(t => t.Subject).HasColumnName("subject").HasMaxLength(300).IsRequired();
        b.Property(t => t.Body).HasColumnName("body").IsRequired();
        b.Property(t => t.RequiredVariables)
            .HasColumnName("required_variables")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOpts) ?? new())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, b2) => a != null && b2 != null && a.SequenceEqual(b2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        b.Property(t => t.CreatedAt).HasColumnName("created_at");
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_log");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).HasColumnName("id");
        b.Property(a => a.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        b.Property(a => a.PerformedBy).HasColumnName("performed_by");
        b.Property(a => a.Timestamp).HasColumnName("timestamp");
        b.Property(a => a.Payload).HasColumnName("payload").HasColumnType("jsonb");

        b.HasIndex(a => a.Action);
        b.HasIndex(a => a.Timestamp);
    }
}
