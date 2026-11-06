using MestoBeach.Sessions.Application.Common;
using StackExchange.Redis;
using System.Security.Cryptography;

namespace MestoBeach.Sessions.Infrastructure.Services;

public sealed class SessionPinService : ISessionPinService
{
    private const int PinAttemptTtlSeconds = 900; // 15 minutes

    private readonly IDatabase _db;

    public SessionPinService(IConnectionMultiplexer redis) =>
        _db = redis.GetDatabase();

    private static string PinKey(Guid tenantId, Guid sessionId) =>
        $"session:{tenantId}:{sessionId}:pin";

    private static string AttemptsKey(Guid tenantId, string tableCode, string deviceId) =>
        $"pin_attempts:{tenantId}:{tableCode}:{deviceId}";

    private static string ValidatedKey(Guid tenantId, Guid sessionId, Guid participantId) =>
        $"pin_validated:{tenantId}:{sessionId}:{participantId}";

    // ─── PIN generation ───────────────────────────────────────────────────────

    public string GeneratePin()
    {
        // Cryptographically secure 4-digit PIN
        var bytes = new byte[4];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        int value = (int)(BitConverter.ToUInt32(bytes, 0) % 10000);
        return value.ToString("D4");
    }

    // ─── PIN storage ──────────────────────────────────────────────────────────

    public Task StorePinAsync(Guid tenantId, Guid sessionId, string pin, TimeSpan ttl) =>
        _db.StringSetAsync(PinKey(tenantId, sessionId), pin, ttl);

    public async Task<string?> GetPinAsync(Guid tenantId, Guid sessionId)
    {
        var val = await _db.StringGetAsync(PinKey(tenantId, sessionId));
        return val.HasValue ? (string?)val : null;
    }

    public async Task UpdatePinKeepTtlAsync(Guid tenantId, Guid sessionId, string newPin)
    {
        var key = PinKey(tenantId, sessionId);
        var ttl = await _db.KeyTimeToLiveAsync(key);
        if (ttl.HasValue && ttl.Value > TimeSpan.Zero)
            await _db.StringSetAsync(key, newPin, ttl.Value);
        else
            await _db.StringSetAsync(key, newPin, PinTimings.PinTtl);
    }

    // ─── pins_in_use:{tenantId} (ADR-040) ─────────────────────────────────────

    private static string PinsInUseKey(Guid tenantId) => $"pins_in_use:{tenantId}";

    // HDEL de todos los campos cuyo valor es la sesión (atómico en Redis).
    private const string ReleaseScript = @"
local all = redis.call('HGETALL', KEYS[1])
local n = 0
for i = 1, #all, 2 do
  if all[i + 1] == ARGV[1] then
    redis.call('HDEL', KEYS[1], all[i])
    n = n + 1
  end
end
return n";

    public Task<bool> TryReservePinAsync(Guid tenantId, string pin, Guid sessionId) =>
        _db.HashSetAsync(PinsInUseKey(tenantId), pin, sessionId.ToString(), When.NotExists);

    public async Task<Guid?> GetPinOwnerAsync(Guid tenantId, string pin)
    {
        var val = await _db.HashGetAsync(PinsInUseKey(tenantId), pin);
        return val.HasValue && Guid.TryParse(val.ToString(), out var id) ? id : null;
    }

    public Task ClaimPinAsync(Guid tenantId, string pin, Guid sessionId) =>
        _db.HashSetAsync(PinsInUseKey(tenantId), pin, sessionId.ToString());

    public Task ReleasePinsAsync(Guid tenantId, Guid sessionId) =>
        _db.ScriptEvaluateAsync(ReleaseScript,
            new RedisKey[] { PinsInUseKey(tenantId) },
            new RedisValue[] { sessionId.ToString() });

    public Task DeletePinAsync(Guid tenantId, Guid sessionId) =>
        _db.KeyDeleteAsync(PinKey(tenantId, sessionId));

    // ─── Attempt counter ──────────────────────────────────────────────────────

    public async Task<int> IncrPinAttemptsAsync(Guid tenantId, string tableCode, string deviceId)
    {
        var key = AttemptsKey(tenantId, tableCode, deviceId);
        long count = await _db.StringIncrementAsync(key);
        if (count == 1)
            await _db.KeyExpireAsync(key, TimeSpan.FromSeconds(PinAttemptTtlSeconds));
        return (int)count;
    }

    // ─── Validated flag ───────────────────────────────────────────────────────

    public Task SetPinValidatedAsync(Guid tenantId, Guid sessionId, Guid participantId, TimeSpan ttl) =>
        _db.StringSetAsync(ValidatedKey(tenantId, sessionId, participantId), "1", ttl);

    public async Task<bool> IsPinValidatedAsync(Guid tenantId, Guid sessionId, Guid participantId)
    {
        var val = await _db.StringGetAsync(ValidatedKey(tenantId, sessionId, participantId));
        return val.HasValue && val == "1";
    }
}
