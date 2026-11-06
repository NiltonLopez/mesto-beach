using MestoBeach.Catalog.Application.Common;
using StackExchange.Redis;
using System.Text.Json;

namespace MestoBeach.Catalog.Infrastructure.Services;

public sealed class CatalogCacheService : ICatalogCacheService
{
    private readonly IDatabase _db;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(300);

    public CatalogCacheService(IConnectionMultiplexer redis) => _db = redis.GetDatabase();

    private static string Key(Guid tenantId) => $"catalog:{tenantId}";

    public async Task<string?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var val = await _db.StringGetAsync(Key(tenantId));
        return val.HasValue ? val.ToString() : null;
    }

    public async Task SetAsync(Guid tenantId, string json, CancellationToken ct = default) =>
        await _db.StringSetAsync(Key(tenantId), json, Ttl);

    public async Task InvalidateAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.KeyDeleteAsync(Key(tenantId));
}
