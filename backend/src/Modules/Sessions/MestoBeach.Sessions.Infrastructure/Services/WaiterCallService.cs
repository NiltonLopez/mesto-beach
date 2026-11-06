using System.Text.Json;
using MestoBeach.Sessions.Application.Common;
using StackExchange.Redis;

namespace MestoBeach.Sessions.Infrastructure.Services;

public sealed class WaiterCallService : IWaiterCallService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(4);

    public WaiterCallService(IConnectionMultiplexer redis)
    {
        _redis = redis;
        _db = redis.GetDatabase();
    }

    private static string Key(Guid tenantId, Guid sessionId) => $"waiter_call:{tenantId}:{sessionId}";

    // Precedencia de motivos: a mayor número, más urgente es el aviso para el camarero.
    // Un motivo nuevo solo ASCIENDE un aviso pendiente si su precedencia es MAYOR que la del actual;
    // en caso contrario se respeta el aviso previo (nunca se degrada).
    //   PIN    (3) → el comensal no puede ni pedir hasta que le dicten el PIN: lo más bloqueante.
    //   BILL   (2) → pide la cuenta: petición concreta y accionable, y la mesa no rota hasta
    //                cumplirla. Pesa más que una llamada genérica, cuyo motivo ni se conoce.
    //   WAITER (1) → llamada genérica: quiere atención presencial, motivo desconocido.
    //   ETA    (0) → "¿cuándo sale?": la consulta menos urgente. No debe pisar los anteriores,
    //                porque el camarero perdería el motivo real por el que le llamaron.
    //                Si no hay aviso previo, el ETA se crea con normalidad.
    private static int Precedencia(string reason) => reason switch
    {
        "PIN" => 3,
        "BILL" => 2,
        "WAITER" => 1,
        _ => 0, // "ETA" y cualquier motivo futuro sin urgencia propia
    };

    public async Task<WaiterCallInfo> CreateOrGetAsync(Guid tenantId, Guid sessionId, string tableCode, string reason = "WAITER", CancellationToken ct = default)
    {
        var key = Key(tenantId, sessionId);
        var existing = await _db.StringGetAsync(key);
        if (existing.HasValue)
        {
            var prev = JsonSerializer.Deserialize<WaiterCallInfo>(existing!)!;
            // Solo se asciende el motivo si el nuevo es más urgente que el pendiente (ver Precedencia).
            // Se conserva el CreatedAt original para no perder la antigüedad del aviso en la cola.
            if (Precedencia(reason) > Precedencia(prev.Reason))
            {
                var ascendido = prev with { Reason = reason };
                await _db.StringSetAsync(key, JsonSerializer.Serialize(ascendido), Ttl);
                return ascendido;
            }
            return prev;
        }

        var info = new WaiterCallInfo(tenantId, sessionId, tableCode, DateTime.UtcNow, reason);
        await _db.StringSetAsync(key, JsonSerializer.Serialize(info), Ttl);
        return info;
    }

    public async Task<List<WaiterCallInfo>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var pattern = $"waiter_call:{tenantId}:*";
        var result = new List<WaiterCallInfo>();

        foreach (var key in server.Keys(pattern: pattern))
        {
            var raw = await _db.StringGetAsync(key);
            if (!raw.HasValue) continue;

            var info = JsonSerializer.Deserialize<WaiterCallInfo>(raw!);
            if (info is not null)
                result.Add(info);
        }

        return result.OrderBy(c => c.CreatedAt).ToList();
    }

    public Task<bool> AcknowledgeAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default) =>
        _db.KeyDeleteAsync(Key(tenantId, sessionId));
}
