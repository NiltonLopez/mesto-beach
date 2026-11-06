using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Enums;
using MestoBeach.Identity.Infrastructure.Persistence;
using StackExchange.Redis;

namespace MestoBeach.Identity.Infrastructure.Services;

/// <summary>
/// Implementación de <see cref="IStaffStationResolver"/> (SDD-001 v2.1 §7, ADR-038).
/// Redis <c>staff_station:{tenantId}:{userId}</c> (60 s, uuid o centinela <c>-</c>); en miss,
/// <c>SELECT assigned_station_id FROM user_memberships WHERE user_id/tenant_id AND status = 'ACTIVE'</c>
/// (sin fila = centinela). Redis caído → Postgres + warning. Postgres caído →
/// <see cref="StaffStationResolution.Unresolved"/> + error (el middleware conserva la claim del
/// token). Nunca lanza.
/// </summary>
public sealed class StaffStationResolver : IStaffStationResolver
{
    private const string SinEstacion = "-";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private static readonly Meter Meter = new("MestoBeach.Identity");
    private static readonly Counter<long> Lookups =
        Meter.CreateCounter<long>("identity.staff_station_lookups");

    private readonly IConnectionMultiplexer _redis;
    private readonly IdentityDbContext _db;
    private readonly ILogger<StaffStationResolver> _log;

    public StaffStationResolver(
        IConnectionMultiplexer redis, IdentityDbContext db, ILogger<StaffStationResolver> log)
    {
        _redis = redis;
        _db = db;
        _log = log;
    }

    private static string Key(Guid tenantId, Guid userId) => $"staff_station:{tenantId}:{userId}";

    private static void Count(string source) =>
        Lookups.Add(1, new KeyValuePair<string, object?>("source", source));

    public async Task<StaffStationResolution> ResolveAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var key = Key(tenantId, userId);
        var redisOk = true;

        try
        {
            var cached = await _redis.GetDatabase().StringGetAsync(key);
            if (cached.HasValue)
            {
                var raw = cached.ToString();
                if (raw == SinEstacion)
                {
                    Count("redis");
                    return new StaffStationResolution(true, null);
                }
                if (Guid.TryParse(raw, out var cachedId))
                {
                    Count("redis");
                    return new StaffStationResolution(true, cachedId);
                }
                // Valor irreconocible: se trata como miss y se reescribe abajo.
            }
        }
        catch (Exception ex)
        {
            redisOk = false;
            _log.LogWarning(ex, "staff_station.redis_unavailable {UserId}", userId);
        }

        _log.LogDebug("staff_station.cache_miss {UserId} {TenantId}", userId, tenantId);

        Guid? stationId;
        try
        {
            stationId = await _db.Memberships.AsNoTracking()
                .Where(m => m.UserId == userId
                         && m.TenantId == tenantId
                         && m.Status == StaffStatus.Active)
                .Select(m => m.AssignedStationId)
                .FirstOrDefaultAsync(ct);
            Count("postgres");
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "staff_station.postgres_unavailable {UserId} {TenantId}: se conserva la estación del token",
                userId, tenantId);
            return StaffStationResolution.Unresolved;
        }

        // Con Redis caído no se intenta escribir: el warning ya se registró una vez en esta petición.
        if (redisOk)
        {
            try
            {
                await _redis.GetDatabase().StringSetAsync(
                    key, stationId?.ToString() ?? SinEstacion, Ttl);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "staff_station.redis_unavailable {UserId}", userId);
            }
        }

        return new StaffStationResolution(true, stationId);
    }

    public async Task InvalidateAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(Key(tenantId, userId));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "staff_station.invalidate_failed {UserId} {TenantId}: caduca sola en 60 s",
                userId, tenantId);
        }
    }
}
