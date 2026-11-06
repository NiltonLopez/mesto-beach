namespace MestoBeach.Sessions.Application.Common;

// ─── Redis / Cache ────────────────────────────────────────────────────────────

public interface ISessionCacheService
{
    /// <summary>Returns the active sessionId for a table, or null.</summary>
    Task<Guid?> GetActiveSessionIdAsync(Guid tenantId, string tableCode);

    Task SetActiveSessionAsync(Guid tenantId, string tableCode, Guid sessionId, TimeSpan ttl);

    Task ClearActiveSessionAsync(Guid tenantId, string tableCode);

    /// <summary>Checks and increments rate limiter. Returns true if request is allowed.</summary>
    Task<bool> CheckRateLimitAsync(Guid tenantId, string tableCode);

    Task StoreQrTokenAsync(Guid tenantId, Guid sessionId, string token, TimeSpan ttl);
    Task<string?> GetQrTokenAsync(Guid tenantId, Guid sessionId);

    Task StoreInviteTokenAsync(Guid tenantId, Guid sessionId, string token, TimeSpan ttl);
    Task<string?> GetInviteTokenAsync(Guid tenantId, Guid sessionId);

    Task DeleteSessionTokensAsync(Guid tenantId, Guid sessionId, string tableCode);
}

// ─── PIN ──────────────────────────────────────────────────────────────────────

public interface ISessionPinService
{
    string GeneratePin();
    Task StorePinAsync(Guid tenantId, Guid sessionId, string pin, TimeSpan ttl);
    Task<string?> GetPinAsync(Guid tenantId, Guid sessionId);
    Task UpdatePinKeepTtlAsync(Guid tenantId, Guid sessionId, string newPin);
    Task DeletePinAsync(Guid tenantId, Guid sessionId);

    /// <summary>Increments attempt counter; sets 15-min TTL on first call. Returns new count.</summary>
    Task<int> IncrPinAttemptsAsync(Guid tenantId, string tableCode, string deviceId);

    Task SetPinValidatedAsync(Guid tenantId, Guid sessionId, Guid participantId, TimeSpan ttl);
    Task<bool> IsPinValidatedAsync(Guid tenantId, Guid sessionId, Guid participantId);

    // ─── pins_in_use:{tenantId} (PRD-019 — ADR-040) ──────────────────────────
    /// <summary>HSETNX pins_in_use:{t} {pin} {sessionId}. True si quedó reservado.</summary>
    Task<bool> TryReservePinAsync(Guid tenantId, string pin, Guid sessionId);
    /// <summary>Sesión dueña del PIN en el hash, o null.</summary>
    Task<Guid?> GetPinOwnerAsync(Guid tenantId, string pin);
    /// <summary>HSET: reclama un PIN cuyo dueño ya no está abierto (autorreparación).</summary>
    Task ClaimPinAsync(Guid tenantId, string pin, Guid sessionId);
    /// <summary>Libera por sessionId (HDEL de los campos cuyo valor es esa sesión), sin leer la clave del PIN.</summary>
    Task ReleasePinsAsync(Guid tenantId, Guid sessionId);
}

/// <summary>Tiempos de vida del PIN (ADR-040). Separados de SessionTtl: una noche dura más de 4 h.</summary>
public static class PinTimings
{
    /// <summary>TTL de session:{t}:{s}:pin y pin_validated:{t}:{s}:{p}.</summary>
    public static readonly TimeSpan PinTtl = TimeSpan.FromHours(12);
}

/// <summary>Se agotaron los 20 sorteos contra pins_in_use (log sessions.pin.pool_exhausted).</summary>
public sealed class PinPoolExhaustedException : Exception
{
    public PinPoolExhaustedException(Guid tenantId)
        : base($"No hay PIN libre para el tenant {tenantId} tras 20 intentos.") { }
}

public static class PinValidationCache
{
    /// <summary>
    /// session_participants.pin_validated manda; Redis es caché (ADR-040). Si la columna dice true y
    /// la clave falta, se re-siembra con PinTtl (best-effort). Si Redis dice true, también vale.
    /// </summary>
    public static async Task<bool> ResolveAsync(
        this ISessionPinService pins, Guid tenantId, Guid sessionId,
        Domain.Entities.SessionParticipant participant)
    {
        bool cached = false;
        try { cached = await pins.IsPinValidatedAsync(tenantId, sessionId, participant.Id); }
        catch { /* Redis caído: manda Postgres */ }

        if (cached) return true;
        if (!participant.PinValidated) return false;

        try { await pins.SetPinValidatedAsync(tenantId, sessionId, participant.Id, PinTimings.PinTtl); }
        catch { /* best-effort */ }
        return true;
    }
}

public static class UniquePinAllocator
{
    private const int MaxAttempts = 20;

    /// <summary>
    /// Sortea un PIN único entre las sesiones abiertas del tenant (SDD-003 §Redis Keys, ADR-040).
    /// Si HSETNX falla y la sesión dueña ya no está OPEN en Postgres, reclama el PIN con HSET.
    /// Lanza <see cref="PinPoolExhaustedException"/> tras 20 intentos.
    /// </summary>
    public static async Task<string> ReserveUniquePinAsync(
        this ISessionPinService pins,
        Domain.Repositories.ISessionRepository sessions,
        Guid tenantId, Guid sessionId, CancellationToken ct)
    {
        for (int i = 0; i < MaxAttempts; i++)
        {
            var pin = pins.GeneratePin();
            if (await pins.TryReservePinAsync(tenantId, pin, sessionId)) return pin;

            var owner = await pins.GetPinOwnerAsync(tenantId, pin);
            if (owner == sessionId) return pin;
            if (owner is null)
            {
                if (await pins.TryReservePinAsync(tenantId, pin, sessionId)) return pin;
                continue;
            }

            var ownerSession = await sessions.GetByIdAsync(owner.Value, ct);
            if (ownerSession is null || !ownerSession.IsOpen || ownerSession.TenantId != tenantId)
            {
                await pins.ClaimPinAsync(tenantId, pin, sessionId);
                return pin;
            }
        }
        throw new PinPoolExhaustedException(tenantId);
    }
}

// ─── Session Token (QR / Invite JWTs) ────────────────────────────────────────

public interface ISessionTokenService
{
    string GenerateJoinToken(Guid sessionId, Guid tenantId, string type, TimeSpan ttl);
    bool ValidateJoinToken(string token, string expectedType, out Guid sessionId, out Guid tenantId);
}

// ─── Staff JWT Validator (for OpenSession body waiterJwt) ────────────────────

public record StaffClaims(Guid StaffId, Guid TenantId, string Role);

public interface IStaffJwtValidator
{
    /// <summary>Validates a staff JWT string from the request body. Returns null if invalid.</summary>
    StaffClaims? Validate(string jwt);
}

// ─── Tenant Settings ──────────────────────────────────────────────────────────

public record TenantSettings(Guid Id, string Slug, bool PinRequired);

public interface ITenantSettingsService
{
    Task<TenantSettings?> GetAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>Cross-module read of the cashier shift state (shifts table owned by Payments).</summary>
public interface IShiftReader
{
    Task<bool> HasOpenShiftAsync(Guid tenantId, CancellationToken ct = default);
}

// ─── URLs del frontend ────────────────────────────────────────────────────────

/// <summary>
/// Base pública de la app del comensal, para los enlaces que se imprimen en un QR.
/// No puede ser <c>localhost</c> si el QR lo va a escanear un móvil: ahí `localhost`
/// es el propio teléfono. Sale de la configuración <c>Frontend:BaseUrl</c>.
/// </summary>
public interface IMenuUrlProvider
{
    string MenuBaseUrl { get; }
}

// ─── QR Code ──────────────────────────────────────────────────────────────────

public interface IQrCodeService
{
    /// <summary>Generates a 1000×1000 PNG QR code for the given URL, optionally overlaying a logo.</summary>
    Task<byte[]> GenerateAsync(string url, Stream? logoStream, CancellationToken ct = default);
}

// ─── Orders Summary (stub until Orders module is built) ───────────────────────

public record OrdersSummaryDto(int TotalItems, int TotalCents, int PendingItems, int DeliveredItems);

public interface IOrderSummaryService
{
    Task<OrdersSummaryDto> GetSummaryAsync(Guid sessionId, CancellationToken ct = default);
}

// ─── SignalR Notifier ─────────────────────────────────────────────────────────

public interface ISignalRNotifier
{
    Task NotifyWaitersAsync(Guid tenantId, object payload, CancellationToken ct = default);
}

// ─── Blob Logo (for QR endpoint) ─────────────────────────────────────────────

public interface IBlobLogoService
{
    /// <summary>Downloads the tenant logo stream, or returns null if it doesn't exist.</summary>
    Task<Stream?> GetLogoStreamAsync(Guid tenantId, CancellationToken ct = default);
}

// ─── Waiter calls ──────────────────────────────────────────────────────────────

public record WaiterCallInfo(
    Guid TenantId,
    Guid SessionId,
    string TableCode,
    DateTime CreatedAt,
    // Motivos del aviso:
    //   "WAITER" = el comensal pulsó "llamar al camarero".
    //   "PIN"    = el comensal necesita el PIN de la mesa para poder pedir.
    //   "BILL"   = el comensal pide la cuenta.
    //   "ETA"    = el comensal pregunta "¿cuándo sale?" por su pedido.
    string Reason = "WAITER");

public interface IWaiterCallService
{
    Task<WaiterCallInfo> CreateOrGetAsync(Guid tenantId, Guid sessionId, string tableCode, string reason = "WAITER", CancellationToken ct = default);
    Task<List<WaiterCallInfo>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> AcknowledgeAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default);
}
