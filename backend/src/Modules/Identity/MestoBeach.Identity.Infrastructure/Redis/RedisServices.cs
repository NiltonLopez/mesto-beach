using System.Text.Json;
using MestoBeach.Identity.Application.Common;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MestoBeach.Identity.Infrastructure.Redis;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly IDatabase _db;

    public RefreshTokenService(IConnectionMultiplexer redis) => _db = redis.GetDatabase();

    public string Generate()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(64);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task StoreAsync(string token, Guid userId, Guid tenantId, TimeSpan ttl, CancellationToken ct = default)
    {
        var value = JsonSerializer.Serialize(new { userId, tenantId });
        await _db.StringSetAsync($"refresh:{token}", value, ttl);
    }

    public async Task<(Guid UserId, Guid TenantId)?> GetAsync(string token, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync($"refresh:{token}");
        if (!value.HasValue) return null;

        var doc = JsonSerializer.Deserialize<JsonElement>(value!);
        return (
            Guid.Parse(doc.GetProperty("userId").GetString()!),
            Guid.Parse(doc.GetProperty("tenantId").GetString()!)
        );
    }

    public async Task DeleteAsync(string token, CancellationToken ct = default) =>
        await _db.KeyDeleteAsync($"refresh:{token}");

    public async Task AddToUserSessionsAsync(Guid userId, string token, TimeSpan ttl, CancellationToken ct = default)
    {
        if (ttl <= TimeSpan.Zero)
        {
            await _db.SetRemoveAsync($"user_sessions:{userId}", token);
            return;
        }
        await _db.SetAddAsync($"user_sessions:{userId}", token);
        await _db.KeyExpireAsync($"user_sessions:{userId}", ttl);
    }

    public async Task<IEnumerable<string>> GetUserSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        var members = await _db.SetMembersAsync($"user_sessions:{userId}");
        return members.Select(m => m.ToString());
    }

    public async Task DeleteAllUserSessionsAsync(Guid userId, CancellationToken ct = default) =>
        await _db.KeyDeleteAsync($"user_sessions:{userId}");
}

public sealed class JwtBlacklistService : IJwtBlacklistService
{
    private readonly IDatabase _db;
    public JwtBlacklistService(IConnectionMultiplexer redis) => _db = redis.GetDatabase();

    public async Task AddAsync(string jti, TimeSpan ttl, CancellationToken ct = default) =>
        await _db.StringSetAsync($"blacklist:{jti}", "1", ttl);

    public async Task<bool> IsBlacklistedAsync(string jti, CancellationToken ct = default) =>
        await _db.KeyExistsAsync($"blacklist:{jti}");
}

public sealed class LoginAttemptService : ILoginAttemptService
{
    private readonly IDatabase _db;
    public LoginAttemptService(IConnectionMultiplexer redis) => _db = redis.GetDatabase();

    private static string Key(string email, Guid tenantId) =>
        $"login_attempts:{email}:{tenantId}";

    public async Task<int> IncrementAsync(string email, Guid tenantId, CancellationToken ct = default)
    {
        var key = Key(email, tenantId);
        var count = await _db.StringIncrementAsync(key);
        await _db.KeyExpireAsync(key, TimeSpan.FromMinutes(15));
        return (int)count;
    }

    public async Task<int> GetCountAsync(string email, Guid tenantId, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync(Key(email, tenantId));
        return value.HasValue ? (int)value : 0;
    }

    public async Task ClearAsync(string email, Guid tenantId, CancellationToken ct = default) =>
        await _db.KeyDeleteAsync(Key(email, tenantId));
}

public sealed class GlobalInvalidationService : IGlobalInvalidationService
{
    private readonly IDatabase _db;
    public GlobalInvalidationService(IConnectionMultiplexer redis) => _db = redis.GetDatabase();

    public async Task InvalidateAllAsync(Guid userId, CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        await _db.StringSetAsync(
            $"auth_all_invalidated:{userId}",
            timestamp,
            TimeSpan.FromHours(8));
    }

    public async Task<bool> IsInvalidatedAsync(Guid userId, long issuedAtUnix, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync($"auth_all_invalidated:{userId}");
        if (!value.HasValue) return false;

        var invalidatedAt = long.Parse(value!);
        return issuedAtUnix < invalidatedAt;
    }
}

public sealed class ActivationTokenService : IActivationTokenService
{
    private readonly IDatabase _db;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    public ActivationTokenService(IConnectionMultiplexer redis) => _db = redis.GetDatabase();

    public string Generate()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(64);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task StoreAsync(string token, Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var value = JsonSerializer.Serialize(new { userId, tenantId });
        await _db.StringSetAsync($"activation:{token}", value, Ttl);
    }

    public async Task<(Guid UserId, Guid TenantId)?> GetAsync(string token, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync($"activation:{token}");
        if (!value.HasValue) return null;

        var doc = JsonSerializer.Deserialize<JsonElement>(value!);
        return (
            Guid.Parse(doc.GetProperty("userId").GetString()!),
            Guid.Parse(doc.GetProperty("tenantId").GetString()!)
        );
    }

    public async Task DeleteAsync(string token, CancellationToken ct = default) =>
        await _db.KeyDeleteAsync($"activation:{token}");

    public async Task<string?> GetTokenByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync($"activation:user:{userId}");
        return value.HasValue ? value.ToString() : null;
    }

    public async Task StoreUserIndexAsync(Guid userId, string token, CancellationToken ct = default) =>
        await _db.StringSetAsync($"activation:user:{userId}", token, Ttl);

    public async Task DeleteUserIndexAsync(Guid userId, CancellationToken ct = default) =>
        await _db.KeyDeleteAsync($"activation:user:{userId}");
}

public sealed class PasswordResetTokenService : IPasswordResetTokenService
{
    private readonly IDatabase _db;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    public PasswordResetTokenService(IConnectionMultiplexer redis) => _db = redis.GetDatabase();

    public string Generate()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(64);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task StoreStaffAsync(string token, Guid staffId, CancellationToken ct = default) =>
        await _db.StringSetAsync($"reset:staff:{token}", staffId.ToString(), Ttl);

    public async Task<Guid?> GetStaffIdAsync(string token, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync($"reset:staff:{token}");
        return value.HasValue ? Guid.Parse(value!) : null;
    }

    public async Task DeleteStaffAsync(string token, CancellationToken ct = default) =>
        await _db.KeyDeleteAsync($"reset:staff:{token}");

    public async Task StoreCustomerAsync(string token, Guid customerId, CancellationToken ct = default) =>
        await _db.StringSetAsync($"reset:customer:{token}", customerId.ToString(), Ttl);

    public async Task<Guid?> GetCustomerIdAsync(string token, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync($"reset:customer:{token}");
        return value.HasValue ? Guid.Parse(value!) : null;
    }

    public async Task DeleteCustomerAsync(string token, CancellationToken ct = default) =>
        await _db.KeyDeleteAsync($"reset:customer:{token}");
}

public sealed class DeviceSessionService : IDeviceSessionService
{
    private readonly IDatabase _db;
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    public DeviceSessionService(IConnectionMultiplexer redis) => _db = redis.GetDatabase();

    private static string Key(Guid tenantId, string deviceId) => $"device:{tenantId}:{deviceId}";

    public async Task SetAsync(Guid tenantId, string deviceId, Guid? stationId, CancellationToken ct = default)
    {
        var value = JsonSerializer.Serialize(new
        {
            stationId = stationId?.ToString(),
            lastSeen = DateTime.UtcNow.ToString("O")
        });
        await _db.StringSetAsync(Key(tenantId, deviceId), value, Ttl);
    }

    public async Task<bool> ExistsAsync(Guid tenantId, string deviceId, CancellationToken ct = default) =>
        await _db.KeyExistsAsync(Key(tenantId, deviceId));

    public async Task DeleteAsync(Guid tenantId, string deviceId, CancellationToken ct = default) =>
        await _db.KeyDeleteAsync(Key(tenantId, deviceId));
}

public sealed class ImpersonationSessionService : IImpersonationSessionService
{
    private readonly IDatabase _db;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    public ImpersonationSessionService(IConnectionMultiplexer redis) => _db = redis.GetDatabase();

    private static string Key(Guid opsUserId, Guid tenantId) =>
        $"impersonation:{opsUserId}:{tenantId}";

    public async Task SetAsync(Guid opsUserId, Guid tenantId, string jti, string reason, CancellationToken ct = default)
    {
        var value = JsonSerializer.Serialize(new { jti, reason });
        await _db.StringSetAsync(Key(opsUserId, tenantId), value, Ttl);
    }

    public async Task<(string Jti, string Reason)?> GetAsync(Guid opsUserId, Guid tenantId, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync(Key(opsUserId, tenantId));
        if (!value.HasValue) return null;

        var doc = JsonSerializer.Deserialize<JsonElement>(value!);
        return (
            doc.GetProperty("jti").GetString()!,
            doc.GetProperty("reason").GetString()!
        );
    }

    public async Task DeleteAsync(Guid opsUserId, Guid tenantId, CancellationToken ct = default) =>
        await _db.KeyDeleteAsync(Key(opsUserId, tenantId));

    public async Task<bool> ExistsAsync(Guid opsUserId, Guid tenantId, CancellationToken ct = default) =>
        await _db.KeyExistsAsync(Key(opsUserId, tenantId));
}

/// <summary>
/// Structured-log audit sink. A dedicated DB table can be wired in post-MVP
/// by replacing this implementation without touching callers.
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly Microsoft.Extensions.Logging.ILogger<AuditLogService> _logger;

    public AuditLogService(Microsoft.Extensions.Logging.ILogger<AuditLogService> logger) =>
        _logger = logger;

    public Task RecordAsync(
        string action,
        Guid actorId,
        string? targetId,
        Guid? tenantId,
        IDictionary<string, object?>? metadata = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "AUDIT {Action} actor={ActorId} target={TargetId} tenant={TenantId} meta={@Metadata}",
            action, actorId, targetId, tenantId, metadata);
        return Task.CompletedTask;
    }
}
