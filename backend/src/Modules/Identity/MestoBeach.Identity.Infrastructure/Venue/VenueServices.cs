using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MestoBeach.Identity.Application.Venue;
using MestoBeach.Identity.Domain.Entities;
using MestoBeach.Identity.Infrastructure.Persistence;
using MestoBeach.Shared.Venue;
using StackExchange.Redis;

namespace MestoBeach.Identity.Infrastructure.Venue;

// ══════════════════════════════════════════════════════════════════════════════
// MODO DEL LOCAL (ADR-036, SDD-001 §7) — implementación de infraestructura.
// ══════════════════════════════════════════════════════════════════════════════

internal static class VenueKeys
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    public static string Mode(Guid tenantId) => $"venue:{tenantId}:mode";
}

/// <summary>
/// Única implementación de <see cref="IVenueModeProvider"/>. Scoped: inyecta el DbContext.
/// Redis → (miss) Postgres + SET NX EX 600 (solo si hay fila) → (Redis caído) Postgres sin SET
/// + warning → (ambos caídos) <see cref="VenueModeUnavailableException"/>.
/// </summary>
public sealed class VenueModeProvider : IVenueModeProvider
{
    private readonly IdentityDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<VenueModeProvider> _logger;

    public VenueModeProvider(IdentityDbContext db, IConnectionMultiplexer redis, ILogger<VenueModeProvider> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<VenueMode> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var key = VenueKeys.Mode(tenantId);
        IDatabase? redis = null;
        RedisValue cached;
        try
        {
            redis = _redis.GetDatabase();
            cached = await redis.StringGetAsync(key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Redis caído: Postgres sin SET.
            _logger.LogWarning(ex, "venue_mode.redis_unavailable {TenantId}", tenantId);
            var fallback = await ReadPostgresOrThrowAsync(tenantId, ex, ct);
            VenueMetrics.ModeReads.Add(1, new KeyValuePair<string, object?>("source", "postgres"));
            return fallback ?? VenueMode.RESTAURANT;
        }

        if (cached.HasValue && Enum.TryParse<VenueMode>(cached.ToString(), ignoreCase: false, out var fromCache)
            && Enum.IsDefined(fromCache))
        {
            VenueMetrics.ModeReads.Add(1, new KeyValuePair<string, object?>("source", "redis"));
            return fromCache;
        }

        // Miss: Postgres y re-siembra con NX para no pisar el SET del handler.
        var mode = await ReadPostgresOrThrowAsync(tenantId, null, ct);
        VenueMetrics.ModeReads.Add(1, new KeyValuePair<string, object?>("source", "postgres"));
        if (mode is null) return VenueMode.RESTAURANT; // tenant inexistente: sin clave

        try
        {
            await redis.StringSetAsync(key, mode.Value.ToString(), VenueKeys.Ttl, When.NotExists);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "venue_mode.redis_unavailable {TenantId}", tenantId);
        }
        return mode.Value;
    }

    /// <summary>Null si el tenant no existe. Lanza VenueModeUnavailableException si Postgres falla.</summary>
    private async Task<VenueMode?> ReadPostgresOrThrowAsync(Guid tenantId, Exception? redisError, CancellationToken ct)
    {
        try
        {
            var row = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new { t.VenueMode })
                .FirstOrDefaultAsync(ct);
            return row?.VenueMode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            VenueMetrics.ModeReads.Add(1, new KeyValuePair<string, object?>("source", "unavailable"));
            throw new VenueModeUnavailableException(tenantId,
                redisError is null ? ex : new AggregateException(redisError, ex));
        }
    }
}

public sealed class VenueModeCache : IVenueModeCache
{
    private readonly IConnectionMultiplexer _redis;

    public VenueModeCache(IConnectionMultiplexer redis) => _redis = redis;

    public Task SetAsync(Guid tenantId, VenueMode mode, CancellationToken ct = default) =>
        _redis.GetDatabase().StringSetAsync(VenueKeys.Mode(tenantId), mode.ToString(), VenueKeys.Ttl);

    public Task DeleteAsync(Guid tenantId, CancellationToken ct = default) =>
        _redis.GetDatabase().KeyDeleteAsync(VenueKeys.Mode(tenantId));
}

public sealed class VenueStore : IVenueStore
{
    // Literales tal y como los persisten Sessions y Orders.
    private const string SessionOpen = "OPEN";
    private const string TripOpen = "OPEN";
    private static readonly string[] ItemTerminal = ["DELIVERED", "CANCELLED"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IdentityDbContext _db;

    public VenueStore(IdentityDbContext db) => _db = db;

    public async Task<TenantVenueRow?> GetTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.VenueMode, t.VenueModeChangedAt })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : new TenantVenueRow(row.VenueMode, row.VenueModeChangedAt);
    }

    public async Task<IReadOnlyList<VenueStationRow>> GetStationsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await _db.KdsStations.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(s => new VenueStationRow(s.Id, s.Name, s.FlowProfile, s.IsActive, s.ActiveIn)).ToList();
    }

    public async Task<VenuePreflight> GetPreflightAsync(Guid tenantId, CancellationToken ct = default)
    {
        var openSessions = await _db.Sessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Status == SessionOpen)
            .Select(s => s.TableCode)
            .ToListAsync(ct);

        // Platos vivos con su sesión (left join: un plato sin sesión cuenta como huérfano).
        var live =
            from i in _db.OrderItems.AsNoTracking()
            join o in _db.Orders.AsNoTracking() on i.OrderId equals o.Id
            join s in _db.Sessions.AsNoTracking() on o.SessionId equals s.Id into ss
            from s in ss.DefaultIfEmpty()
            where i.TenantId == tenantId && !ItemTerminal.Contains(i.Status)
            select new { SessionOpen = s != null && s.Status == SessionOpen };

        var activeItems = await live.CountAsync(x => x.SessionOpen, ct);
        var orphanItems = await live.CountAsync(x => !x.SessionOpen, ct);

        var openTrips = await _db.DeliveryTrips.AsNoTracking()
            .CountAsync(t => t.TenantId == tenantId && t.Status == TripOpen, ct);

        var tables = openSessions.Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
        return new VenuePreflight(openSessions.Count, activeItems, openTrips, orphanItems, tables);
    }

    public async Task<VenueChangeOutcome> ChangeModeAsync(Guid tenantId, VenueMode mode, Guid changedBy,
        DateTime now, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // 1. Bloqueo de fila: dos cambios simultáneos se serializan aquí.
        var tenant = await _db.Tenants
            .FromSqlInterpolated($"SELECT * FROM tenants WHERE id = {tenantId} FOR UPDATE")
            .FirstOrDefaultAsync(ct);

        if (tenant is null)
        {
            await tx.RollbackAsync(ct);
            return new VenueChangeOutcome.TenantNotFound();
        }

        if (tenant.VenueMode == mode)
        {
            await tx.CommitAsync(ct); // transacción vacía
            return new VenueChangeOutcome.AlreadyInMode(tenant.VenueMode, tenant.VenueModeChangedAt);
        }

        // 2. Pre-flight dentro de la misma transacción.
        var preflight = await GetPreflightAsync(tenantId, ct);
        if (!preflight.Ready)
        {
            await tx.RollbackAsync(ct);
            return new VenueChangeOutcome.NotIdle(preflight);
        }

        // 3. Escritura.
        var previous = tenant.VenueMode;
        tenant.ChangeVenueMode(mode, now);
        var preflightJson = JsonSerializer.Serialize(new
        {
            preflight.OpenSessions,
            preflight.ActiveItems,
            preflight.OpenTrips,
            preflight.OrphanItems,
            preflight.Tables,
        }, Json);
        _db.VenueModeChanges.Add(new VenueModeChange(tenantId, previous, mode, changedBy, now, preflightJson));
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new VenueChangeOutcome.Changed(previous, now, preflight);
    }

    public async Task<(IReadOnlyList<VenueHistoryRow> Rows, int Total)> GetHistoryAsync(
        Guid tenantId, int limit, CancellationToken ct = default)
    {
        var total = await _db.VenueModeChanges.AsNoTracking().CountAsync(c => c.TenantId == tenantId, ct);

        var rows = await (
                from c in _db.VenueModeChanges.AsNoTracking()
                join u in _db.Users.AsNoTracking() on c.ChangedBy equals u.Id into us
                from u in us.DefaultIfEmpty()
                where c.TenantId == tenantId
                orderby c.ChangedAt descending
                select new { c.Id, c.FromMode, c.ToMode, c.ChangedBy, Name = u != null ? u.Name : null, c.ChangedAt, c.Preflight })
            .Take(limit)
            .ToListAsync(ct);

        return (rows.Select(r => new VenueHistoryRow(
            r.Id, r.FromMode, r.ToMode, r.ChangedBy, r.Name, r.ChangedAt, r.Preflight)).ToList(), total);
    }
}
