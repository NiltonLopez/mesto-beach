using MestoBeach.Identity.Domain.Entities;
using MestoBeach.Identity.Domain.Enums;

namespace MestoBeach.Identity.Domain.Repositories;

/// <summary>Global staff identities (one row per person, shared across tenants).</summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
}

/// <summary>Per-tenant memberships linking a user to a tenant with one or more roles.</summary>
public interface IUserMembershipRepository
{
    Task<UserMembership?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserMembership?> GetByUserAndTenantAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    /// <summary>All memberships for a user (every tenant they belong to).</summary>
    Task<List<UserMembership>> GetByUserAsync(Guid userId, CancellationToken ct = default);
    /// <summary>All memberships within a tenant (the tenant's staff list).</summary>
    Task<List<UserMembership>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(UserMembership membership, CancellationToken ct = default);
    Task UpdateAsync(UserMembership membership, CancellationToken ct = default);
    Task RemoveAsync(UserMembership membership, CancellationToken ct = default);
}

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<bool> ExistsBySlugAsync(string slug, CancellationToken ct = default);
    Task AddAsync(Tenant tenant, CancellationToken ct = default);
    Task UpdateAsync(Tenant tenant, CancellationToken ct = default);
}

public interface ICustomerAccountRepository
{
    Task<CustomerAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CustomerAccount?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<CustomerAccount?> GetByGoogleIdAsync(string googleId, CancellationToken ct = default);
    Task AddAsync(CustomerAccount account, CancellationToken ct = default);
    Task UpdateAsync(CustomerAccount account, CancellationToken ct = default);
}

public interface IPlatformUserRepository
{
    Task<PlatformUser?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PlatformUser?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<List<string>> GetRolesAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(PlatformUser user, CancellationToken ct = default);
    Task UpdateAsync(PlatformUser user, CancellationToken ct = default);
    Task AddRoleAsync(PlatformUserRole role, CancellationToken ct = default);
    Task RemoveRolesAsync(Guid userId, CancellationToken ct = default);
}

public interface IDeviceTokenRepository
{
    Task<DeviceToken?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns active (non-revoked) device by human-readable deviceId within a tenant.</summary>
    Task<DeviceToken?> GetActiveByDeviceIdAndTenantAsync(string deviceId, Guid tenantId, CancellationToken ct = default);

    Task<List<DeviceToken>> GetActiveByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(DeviceToken token, CancellationToken ct = default);
    Task UpdateAsync(DeviceToken token, CancellationToken ct = default);
}
