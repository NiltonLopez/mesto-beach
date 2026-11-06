using Microsoft.Extensions.Logging;
using MestoBeach.Orders.Application.Common;
using StackExchange.Redis;

namespace MestoBeach.Orders.Infrastructure.Services;

/// <summary>
/// Gate del PIN al pedir (SDD-004, ADR-040). La clave Redis <c>pin_validated:{t}:{s}:{p}</c> es caché;
/// <c>session_participants.pin_validated</c> es la verdad. Si la clave falta (o Redis falla) y la
/// columna dice true, se re-siembra con 12 h (best-effort) y el pedido pasa.
/// </summary>
public sealed class PinValidationService : IPinValidationService
{
    /// <summary>TTL fijo de la re-siembra: Postgres no guarda caducidad; el cierre lo controla sessions.status.</summary>
    private static readonly TimeSpan ReseedTtl = TimeSpan.FromHours(12);

    private readonly IDatabase _redis;
    private readonly ILogger<PinValidationService> _logger;

    // Key pattern matches Sessions module: pin_validated:{tenantId}:{sessionId}:{participantId}
    private static string PinValidatedKey(Guid tenantId, Guid sessionId, Guid participantId) =>
        $"pin_validated:{tenantId}:{sessionId}:{participantId}";

    public PinValidationService(
        IConnectionMultiplexer redis,
        ILogger<PinValidationService> logger)
    {
        _redis = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<bool> IsPinValidatedAsync(
        Guid tenantId, Guid sessionId, Guid participantId, bool dbPinValidated, CancellationToken ct = default)
    {
        var key = PinValidatedKey(tenantId, sessionId, participantId);

        bool exists = false;
        try
        {
            exists = await _redis.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "pin_validation.redis_failed {TenantId} {SessionId} {ParticipantId}",
                tenantId, sessionId, participantId);
        }

        _logger.LogDebug(
            "pin_validation.check {TenantId} {SessionId} {ParticipantId} {Result}",
            tenantId, sessionId, participantId, exists);

        if (exists) return true;
        if (!dbPinValidated) return false;

        // Postgres manda: re-siembra best-effort; si Redis falla, no se bloquea el pedido.
        try
        {
            await _redis.StringSetAsync(key, "1", ReseedTtl);
            _logger.LogInformation(
                "order.pin_reseeded {TenantId} {SessionId} {ParticipantId}",
                tenantId, sessionId, participantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "order.pin_reseed_failed {TenantId} {SessionId} {ParticipantId}",
                tenantId, sessionId, participantId);
        }
        return true;
    }
}
