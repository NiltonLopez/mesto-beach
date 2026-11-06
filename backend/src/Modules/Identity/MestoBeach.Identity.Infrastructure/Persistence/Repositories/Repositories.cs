using Microsoft.EntityFrameworkCore;
using MestoBeach.Identity.Domain.Entities;
using MestoBeach.Identity.Domain.Repositories;

namespace MestoBeach.Identity.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly IdentityDbContext _db;
    public UserRepository(IdentityDbContext db) => _db = db;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task AddAsync(User user, CancellationToken ct)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync(ct);
    }
}

public sealed class UserMembershipRepository : IUserMembershipRepository
{
    private readonly IdentityDbContext _db;
    public UserMembershipRepository(IdentityDbContext db) => _db = db;

    public Task<UserMembership?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Memberships.FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<UserMembership?> GetByUserAndTenantAsync(Guid userId, Guid tenantId, CancellationToken ct) =>
        _db.Memberships.FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);

    public Task<List<UserMembership>> GetByUserAsync(Guid userId, CancellationToken ct) =>
        _db.Memberships.Where(m => m.UserId == userId).ToListAsync(ct);

    public Task<List<UserMembership>> GetByTenantAsync(Guid tenantId, CancellationToken ct) =>
        _db.Memberships.Where(m => m.TenantId == tenantId).ToListAsync(ct);

    public async Task AddAsync(UserMembership membership, CancellationToken ct)
    {
        _db.Memberships.Add(membership);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(UserMembership membership, CancellationToken ct)
    {
        _db.Memberships.Update(membership);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(UserMembership membership, CancellationToken ct)
    {
        _db.Memberships.Remove(membership);
        await _db.SaveChangesAsync(ct);
    }
}

public sealed class TenantRepository : ITenantRepository
{
    private readonly IdentityDbContext _db;
    public TenantRepository(IdentityDbContext db) => _db = db;

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken ct) =>
        _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);

    public Task<bool> ExistsBySlugAsync(string slug, CancellationToken ct) =>
        _db.Tenants.AnyAsync(t => t.Slug == slug, ct);

    public async Task AddAsync(Tenant tenant, CancellationToken ct)
    {
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Tenant tenant, CancellationToken ct)
    {
        _db.Tenants.Update(tenant);
        await _db.SaveChangesAsync(ct);
    }
}

public sealed class CustomerAccountRepository : ICustomerAccountRepository
{
    private readonly IdentityDbContext _db;
    public CustomerAccountRepository(IdentityDbContext db) => _db = db;

    public Task<CustomerAccount?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.CustomerAccounts.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<CustomerAccount?> GetByEmailAsync(string email, CancellationToken ct) =>
        _db.CustomerAccounts.FirstOrDefaultAsync(c => c.Email == email, ct);

    public Task<CustomerAccount?> GetByGoogleIdAsync(string googleId, CancellationToken ct) =>
        _db.CustomerAccounts.FirstOrDefaultAsync(c => c.GoogleId == googleId, ct);

    public async Task AddAsync(CustomerAccount account, CancellationToken ct)
    {
        _db.CustomerAccounts.Add(account);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(CustomerAccount account, CancellationToken ct)
    {
        _db.CustomerAccounts.Update(account);
        await _db.SaveChangesAsync(ct);
    }
}

public sealed class PlatformUserRepository : IPlatformUserRepository
{
    private readonly IdentityDbContext _db;
    public PlatformUserRepository(IdentityDbContext db) => _db = db;

    public Task<PlatformUser?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.PlatformUsers.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<PlatformUser?> GetByEmailAsync(string email, CancellationToken ct) =>
        _db.PlatformUsers.FirstOrDefaultAsync(p => p.Email == email, ct);

    public Task<List<string>> GetRolesAsync(Guid userId, CancellationToken ct) =>
        _db.PlatformUserRoles
            .Where(r => r.UserId == userId)
            .Select(r => r.Role)
            .ToListAsync(ct);

    public async Task AddAsync(PlatformUser user, CancellationToken ct)
    {
        _db.PlatformUsers.Add(user);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PlatformUser user, CancellationToken ct)
    {
        _db.PlatformUsers.Update(user);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddRoleAsync(PlatformUserRole role, CancellationToken ct)
    {
        _db.PlatformUserRoles.Add(role);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveRolesAsync(Guid userId, CancellationToken ct)
    {
        var roles = await _db.PlatformUserRoles.Where(r => r.UserId == userId).ToListAsync(ct);
        _db.PlatformUserRoles.RemoveRange(roles);
        await _db.SaveChangesAsync(ct);
    }
}

public sealed class DeviceTokenRepository : IDeviceTokenRepository
{
    private readonly IdentityDbContext _db;
    public DeviceTokenRepository(IdentityDbContext db) => _db = db;

    public Task<DeviceToken?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.DeviceTokens.FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<DeviceToken?> GetActiveByDeviceIdAndTenantAsync(string deviceId, Guid tenantId, CancellationToken ct) =>
        _db.DeviceTokens.FirstOrDefaultAsync(
            d => d.DeviceId == deviceId && d.TenantId == tenantId && d.RevokedAt == null, ct);

    public Task<List<DeviceToken>> GetActiveByTenantAsync(Guid tenantId, CancellationToken ct) =>
        _db.DeviceTokens.Where(d => d.TenantId == tenantId && d.RevokedAt == null).ToListAsync(ct);

    public async Task AddAsync(DeviceToken token, CancellationToken ct)
    {
        _db.DeviceTokens.Add(token);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(DeviceToken token, CancellationToken ct)
    {
        _db.DeviceTokens.Update(token);
        await _db.SaveChangesAsync(ct);
    }
}
