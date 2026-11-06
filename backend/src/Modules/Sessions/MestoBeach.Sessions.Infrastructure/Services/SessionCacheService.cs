using MestoBeach.Sessions.Application.Common;
using StackExchange.Redis;

namespace MestoBeach.Sessions.Infrastructure.Services;

public sealed class SessionCacheService : ISessionCacheService
{
    private const int RateLimitMax = 10;
    private const int RateLimitWindowSeconds = 60;

    private readonly IDatabase _db;

    public SessionCacheService(IConnectionMultiplexer redis) =>
        _db = redis.GetDatabase();

    private static string ActiveKey(Guid tenantId, string tableCode) =>
        $"table:{tenantId}:{tableCode}:active";

    private static string QrTokenKey(Guid tenantId, Guid sessionId) =>
        $"qr:{tenantId}:{sessionId}:token";

    private static string InviteTokenKey(Guid tenantId, Guid sessionId) =>
        $"invite:{tenantId}:{sessionId}:token";

    private static string RateLimitKey(Guid tenantId, string tableCode) =>
        $"ratelimit:sessions:{tenantId}:{tableCode}";

    // ─── Active session ───────────────────────────────────────────────────────

    public async Task<Guid?> GetActiveSessionIdAsync(Guid tenantId, string tableCode)
    {
        var val = await _db.StringGetAsync(ActiveKey(tenantId, tableCode));
        return val.HasValue && Guid.TryParse(val, out var id) ? id : null;
    }

    public Task SetActiveSessionAsync(Guid tenantId, string tableCode, Guid sessionId, TimeSpan ttl) =>
        _db.StringSetAsync(ActiveKey(tenantId, tableCode), sessionId.ToString(), ttl);

    public Task ClearActiveSessionAsync(Guid tenantId, string tableCode) =>
        _db.KeyDeleteAsync(ActiveKey(tenantId, tableCode));

    // ─── Rate limit ───────────────────────────────────────────────────────────

    public async Task<bool> CheckRateLimitAsync(Guid tenantId, string tableCode)
    {
        var key = RateLimitKey(tenantId, tableCode);
        long count = await _db.StringIncrementAsync(key);
        if (count == 1)
            await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(RateLimitWindowSeconds));
        return count <= RateLimitMax;
    }

    // ─── QR token ─────────────────────────────────────────────────────────────

    public Task StoreQrTokenAsync(Guid tenantId, Guid sessionId, string token, TimeSpan ttl) =>
        _db.StringSetAsync(QrTokenKey(tenantId, sessionId), token, ttl);

    public async Task<string?> GetQrTokenAsync(Guid tenantId, Guid sessionId)
    {
        var val = await _db.StringGetAsync(QrTokenKey(tenantId, sessionId));
        return val.HasValue ? (string?)val : null;
    }

    // ─── Invite token ─────────────────────────────────────────────────────────

    public Task StoreInviteTokenAsync(Guid tenantId, Guid sessionId, string token, TimeSpan ttl) =>
        _db.StringSetAsync(InviteTokenKey(tenantId, sessionId), token, ttl);

    public async Task<string?> GetInviteTokenAsync(Guid tenantId, Guid sessionId)
    {
        var val = await _db.StringGetAsync(InviteTokenKey(tenantId, sessionId));
        return val.HasValue ? (string?)val : null;
    }

    // ─── Cleanup on session close ─────────────────────────────────────────────

    public Task DeleteSessionTokensAsync(Guid tenantId, Guid sessionId, string tableCode)
    {
        var keys = new RedisKey[]
        {
            ActiveKey(tenantId, tableCode),
            QrTokenKey(tenantId, sessionId),
            InviteTokenKey(tenantId, sessionId)
        };
        return _db.KeyDeleteAsync(keys);
    }
}
