using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MestoBeach.Identity.Domain.Entities;
using MestoBeach.Identity.Domain.Enums;

namespace MestoBeach.Identity.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(100).IsRequired();
        b.Property(t => t.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(t => t.Plan).HasColumnName("plan")
            .HasConversion(v => v.ToString().ToLowerInvariant(),
                v => Enum.Parse<TenantPlan>(v, true));
        b.Property(t => t.PlanCode).HasColumnName("plan_code").HasMaxLength(20)
            .HasDefaultValue("pro").IsRequired();
        b.Property(t => t.Status).HasColumnName("status")
            .HasConversion(v => v.ToString().ToLowerInvariant(),
                v => Enum.Parse<TenantStatus>(v, true));
        b.Property(t => t.QrMode).HasColumnName("qr_mode")
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<QrMode>(v, true));
        b.Property(t => t.OperationMode).HasColumnName("operation_mode")
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<OperationMode>(v, true));
        b.Property(t => t.PinRequired).HasColumnName("pin_required");
        b.Property(t => t.Timezone).HasColumnName("timezone").HasMaxLength(50);
        b.Property(t => t.LogoUrl).HasColumnName("logo_url").HasMaxLength(500);
        b.Property(t => t.PrimaryColor).HasColumnName("primary_color").HasMaxLength(7);
        b.Property(t => t.Nit).HasColumnName("nit").HasMaxLength(20);
        b.Property(t => t.NitVerified).HasColumnName("nit_verified");
        b.Property(t => t.CreatedAt).HasColumnName("created_at");
        b.Property(t => t.ActivatedAt).HasColumnName("activated_at");
        b.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        b.Property(t => t.City).HasColumnName("city").HasMaxLength(100);
        b.Property(t => t.OwnerEmail).HasColumnName("owner_email").HasMaxLength(300);
        b.Property(t => t.InternalNotes).HasColumnName("internal_notes");
        b.Property(t => t.CommissionRate).HasColumnName("commission_rate").HasColumnType("decimal(5,2)");
        b.Property(t => t.SubscriptionAmountCents).HasColumnName("subscription_amount_cents");
        b.Property(t => t.TrialEndsAt).HasColumnName("trial_ends_at");
        b.Property(t => t.CreatedBy).HasColumnName("created_by");
        // Modo del local (ADR-036): fuente de verdad; Redis es caché.
        b.Property(t => t.VenueMode).HasColumnName("venue_mode").HasMaxLength(16).IsRequired()
            .HasConversion(v => v.ToString(), v => Enum.Parse<MestoBeach.Shared.Venue.VenueMode>(v, true))
            .HasDefaultValue(MestoBeach.Shared.Venue.VenueMode.RESTAURANT);
        b.Property(t => t.VenueModeChangedAt).HasColumnName("venue_mode_changed_at");

        b.HasIndex(t => t.Slug).IsUnique();
    }
}

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).HasColumnName("id");
        b.Property(u => u.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(u => u.Email).HasColumnName("email").HasMaxLength(300).IsRequired();
        b.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(100);
        b.Property(u => u.Status).HasColumnName("status")
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<StaffStatus>(v, true));
        b.Property(u => u.MustChangePassword).HasColumnName("must_change_password").HasDefaultValue(false);
        b.Property(u => u.CreatedAt).HasColumnName("created_at");
        b.Property(u => u.LastLogin).HasColumnName("last_login");

        // One global identity per email.
        b.HasIndex(u => u.Email).IsUnique();
    }
}

public sealed class UserMembershipConfiguration : IEntityTypeConfiguration<UserMembership>
{
    public void Configure(EntityTypeBuilder<UserMembership> b)
    {
        b.ToTable("user_memberships");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).HasColumnName("id");
        b.Property(m => m.UserId).HasColumnName("user_id");
        b.Property(m => m.TenantId).HasColumnName("tenant_id");
        // Several roles per membership, stored comma-separated (e.g. "Admin,Cajero").
        b.Property(m => m.Roles).HasColumnName("roles")
            .HasConversion(
                v => string.Join(",", v.Select(r => r.ToString())),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Select(s => Enum.Parse<StaffRole>(s, true)).ToList(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<StaffRole>>(
                    (a, c) => a!.SequenceEqual(c!),
                    v => v.Aggregate(0, (h, r) => HashCode.Combine(h, (int)r)),
                    v => v.ToList()))
            .HasMaxLength(200).IsRequired();
        b.Property(m => m.Status).HasColumnName("status")
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<StaffStatus>(v, true));
        b.Property(m => m.CreatedAt).HasColumnName("created_at");

        // Asignaciones de Mesto Beach (SDD-001 v2.0): sector del PDA y estación del Runner.
        // Sin FK a propósito: floors es de Sessions y kds_stations de Orders, y cada módulo
        // mantiene sus propias migraciones. La existencia se valida en el caso de uso.
        b.Property(m => m.AssignedFloorId).HasColumnName("assigned_floor_id");
        b.Property(m => m.AssignedStationId).HasColumnName("assigned_station_id");

        // One membership per (user, tenant).
        b.HasIndex(m => new { m.UserId, m.TenantId }).IsUnique();
        b.HasIndex(m => m.TenantId);

        // La cola del runner y el mapa del PDA se resuelven por asignación dentro del tenant.
        b.HasIndex(m => new { m.TenantId, m.AssignedStationId });
        b.HasIndex(m => new { m.TenantId, m.AssignedFloorId });
    }
}

public sealed class CustomerAccountConfiguration : IEntityTypeConfiguration<CustomerAccount>
{
    public void Configure(EntityTypeBuilder<CustomerAccount> b)
    {
        b.ToTable("customer_accounts");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.Email).HasColumnName("email").HasMaxLength(300).IsRequired();
        b.Property(c => c.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(20);
        b.Property(c => c.BirthDate).HasColumnName("birth_date");
        b.Property(c => c.PasswordHash).HasColumnName("password_hash").HasMaxLength(100);
        b.Property(c => c.GoogleId).HasColumnName("google_id").HasMaxLength(100);
        b.Property(c => c.LoyaltyPoints).HasColumnName("loyalty_points");
        b.Property(c => c.CreatedAt).HasColumnName("created_at");
        b.Property(c => c.LastLogin).HasColumnName("last_login");

        b.HasIndex(c => c.Email).IsUnique();
        b.HasIndex(c => c.GoogleId);
    }
}

public sealed class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> b)
    {
        b.ToTable("platform_users");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.Email).HasColumnName("email").HasMaxLength(300).IsRequired();
        b.Property(p => p.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(p => p.PasswordHash).HasColumnName("password_hash").HasMaxLength(100).IsRequired();
        b.Property(p => p.TotpSecret).HasColumnName("totp_secret").HasMaxLength(500);
        b.Property(p => p.EmergencyCodeHash).HasColumnName("emergency_code_hash").HasMaxLength(100);
        b.Property(p => p.Status).HasColumnName("status")
            .HasConversion(v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<PlatformUserStatus>(v, true));
        b.Property(p => p.CreatedAt).HasColumnName("created_at");
        b.Property(p => p.LastLogin).HasColumnName("last_login");
        b.Property(p => p.CreatedBy).HasColumnName("created_by");
        b.Property(p => p.DeactivatedAt).HasColumnName("deactivated_at");
        b.Property(p => p.DeactivatedBy).HasColumnName("deactivated_by");

        b.HasIndex(p => p.Email).IsUnique();
    }
}

public sealed class PlatformUserRoleConfiguration : IEntityTypeConfiguration<PlatformUserRole>
{
    public void Configure(EntityTypeBuilder<PlatformUserRole> b)
    {
        b.ToTable("platform_user_roles");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.UserId).HasColumnName("user_id");
        b.Property(r => r.Role).HasColumnName("role").HasMaxLength(50).IsRequired();
        b.Property(r => r.AssignedBy).HasColumnName("assigned_by");
        b.Property(r => r.AssignedAt).HasColumnName("assigned_at");
    }
}

public sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> b)
    {
        b.ToTable("device_tokens");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).HasColumnName("id");
        b.Property(d => d.TenantId).HasColumnName("tenant_id");
        b.Property(d => d.StaffId).HasColumnName("staff_id");
        b.Property(d => d.DeviceId).HasColumnName("device_id").HasMaxLength(100).IsRequired();
        b.Property(d => d.DeviceName).HasColumnName("device_name").HasMaxLength(200).IsRequired();
        b.Property(d => d.StationId).HasColumnName("station_id");
        b.Property(d => d.RevokedAt).HasColumnName("revoked_at");
        b.Property(d => d.CreatedAt).HasColumnName("created_at");
        b.Property(d => d.LastSeenAt).HasColumnName("last_seen_at");

        // Uniqueness: one active device per deviceId per tenant
        b.HasIndex(d => new { d.TenantId, d.DeviceId }).IsUnique()
            .HasFilter("revoked_at IS NULL");
    }
}
