using MestoBeach.Notifications.Application.Common;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace MestoBeach.Notifications.Infrastructure.Services;

public sealed class MaintenanceService : IMaintenanceService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MaintenanceService> _logger;

    private static readonly HashSet<string> AllApps = new() { "menu", "waiter", "kds", "admin", "ops" };

    public MaintenanceService(IConnectionMultiplexer redis, ILogger<MaintenanceService> logger)
    {
        _redis  = redis;
        _logger = logger;
    }

    // ── GET ───────────────────────────────────────────────────────────────────

    public async Task<MaintenanceStatusDto> GetAsync(string? app, CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        if (app is not null)
        {
            var appDto = await ReadKeyAsync(db, $"maintenance:{app}");
            if (appDto is not null)
            {
                _logger.LogInformation("maintenance.checked app={App} active={Active} source=app_specific", app, appDto.Active);
                return appDto;
            }
        }

        // Fallback to global
        var globalDto = await ReadKeyAsync(db, "maintenance:global");
        if (globalDto is not null)
        {
            _logger.LogInformation("maintenance.checked app={App} active={Active} source=global", app, globalDto.Active);
            return globalDto;
        }

        _logger.LogInformation("maintenance.checked app={App} active=false source=none", app);
        return new MaintenanceStatusDto(false, null, Array.Empty<string>(), null, null);
    }

    // ── SET ───────────────────────────────────────────────────────────────────

    public async Task SetAsync(
        bool active,
        string? message,
        string[] apps,
        DateTime? activeFrom,
        DateTime? activeUntil,
        Guid activatedBy,
        CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        // Determine if it's global (empty array or all 5 apps)
        var isGlobal = apps.Length == 0 || apps.Length >= 5;
        var targetKeys = isGlobal
            ? new[] { "maintenance:global" }
            : apps.Select(a => $"maintenance:{a}").ToArray();

        if (active)
        {
            var payload = new MaintenanceRedisPayload(
                message!, apps, activeFrom!.Value, activeUntil!.Value, activatedBy);
            var json = JsonSerializer.Serialize(payload);

            // TTL = (activeUntil - now) + 60 seconds buffer
            var ttl = (activeUntil!.Value - DateTime.UtcNow) + TimeSpan.FromSeconds(60);
            if (ttl < TimeSpan.FromSeconds(60)) ttl = TimeSpan.FromSeconds(60);

            foreach (var key in targetKeys)
            {
                await db.StringSetAsync(key, json, ttl);
                _logger.LogInformation("maintenance.activated key={Key} activeUntil={Until}", key, activeUntil);
            }
        }
        else
        {
            foreach (var key in targetKeys)
            {
                await db.KeyDeleteAsync(key);
                _logger.LogInformation("maintenance.deactivated key={Key}", key);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<MaintenanceStatusDto?> ReadKeyAsync(IDatabase db, string key)
    {
        var raw = await db.StringGetAsync(key);
        if (!raw.HasValue) return null;

        MaintenanceRedisPayload? payload;
        try { payload = JsonSerializer.Deserialize<MaintenanceRedisPayload>(raw!); }
        catch { return null; }

        if (payload is null) return null;

        // Defend against stale keys (activeUntil passed)
        if (payload.ActiveUntil < DateTime.UtcNow)
            return null;

        return new MaintenanceStatusDto(
            true, payload.Message, payload.Apps,
            payload.ActiveFrom, payload.ActiveUntil, payload.ActivatedBy);
    }
}

internal sealed record MaintenanceRedisPayload(
    string   Message,
    string[] Apps,
    DateTime ActiveFrom,
    DateTime ActiveUntil,
    Guid     ActivatedBy);
