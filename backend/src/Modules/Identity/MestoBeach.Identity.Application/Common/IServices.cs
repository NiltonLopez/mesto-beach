namespace MestoBeach.Identity.Application.Common;

/// <summary>Service interfaces used by Application layer handlers.</summary>

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

/// <summary>Generates human-friendly temporary passwords for the direct-access staff onboarding flow.</summary>
public interface IPasswordGenerator
{
    /// <summary>
    /// Returns a random password that satisfies the staff password policy
    /// (>=8 chars, at least one upper, one lower and one digit), avoiding ambiguous characters.
    /// </summary>
    string GenerateTemporary();
}

public interface IJwtService
{
    /// <summary>
    /// Staff JWT — RS256, 8h, claims: sub/tenantId/role(primary)/roles[]/type=staff/jti/iat/exp.
    /// Añade <c>floorId</c> y <c>stationId</c> cuando la membresía tiene esas asignaciones
    /// (SDD-001 v2.0): así la API autoriza "este runner solo reclama platos de su estación" sin
    /// consultar la base en cada petición.
    /// </summary>
    string GenerateStaffToken(
        Guid userId,
        Guid tenantId,
        IReadOnlyList<string> roles,
        Guid? assignedFloorId,
        Guid? assignedStationId,
        out string jti);

    /// <summary>Customer JWT — RS256, 4h, claims: sub/type=customer/jti/iat/exp</summary>
    string GenerateCustomerToken(Guid customerId, out string jti);

    /// <summary>TOTP Challenge Token — RS256, 5min</summary>
    string GenerateTotpChallengeToken(Guid userId, bool setupRequired, out string jti);

    /// <summary>Platform Admin JWT — RS256, 8h, claims: sub/roles/type=platform_admin/jti/iat/exp</summary>
    string GeneratePlatformAdminToken(Guid userId, IEnumerable<string> roles, out string jti);

    /// <summary>
    /// Device Token (KDS) — RS256, rolling 30d.
    /// sub = deviceTokenRecordId (UUID), claims include deviceId (string), tenantId, stationId (optional).
    /// </summary>
    string GenerateDeviceToken(Guid deviceTokenRecordId, Guid tenantId, string deviceId, Guid? stationId, out string jti);

    /// <summary>
    /// Impersonation Token — RS256, 15 min.
    /// sub = opsUserId, claims: role=platform_admin, impersonating=targetTenantId, type=impersonation.
    /// </summary>
    string GenerateImpersonationToken(Guid opsUserId, Guid targetTenantId, string jti);

    /// <summary>Validates a JWT and returns claims dictionary. Throws if invalid.</summary>
    IDictionary<string, string> ValidateToken(string token, string expectedType);

    DateTime GetTokenExpiry(string token);
    string GetJti(string token);
}

public interface IRefreshTokenService
{
    /// <summary>Generates a cryptographically secure opaque refresh token (hex 64 bytes).</summary>
    string Generate();

    Task StoreAsync(string token, Guid userId, Guid tenantId, TimeSpan ttl, CancellationToken ct = default);

    Task<(Guid UserId, Guid TenantId)?> GetAsync(string token, CancellationToken ct = default);

    Task DeleteAsync(string token, CancellationToken ct = default);

    Task AddToUserSessionsAsync(Guid userId, string token, TimeSpan ttl, CancellationToken ct = default);

    Task<IEnumerable<string>> GetUserSessionsAsync(Guid userId, CancellationToken ct = default);

    Task DeleteAllUserSessionsAsync(Guid userId, CancellationToken ct = default);
}

public interface IJwtBlacklistService
{
    Task AddAsync(string jti, TimeSpan ttl, CancellationToken ct = default);
    Task<bool> IsBlacklistedAsync(string jti, CancellationToken ct = default);
}

public interface ILoginAttemptService
{
    Task<int> IncrementAsync(string email, Guid tenantId, CancellationToken ct = default);
    Task<int> GetCountAsync(string email, Guid tenantId, CancellationToken ct = default);
    Task ClearAsync(string email, Guid tenantId, CancellationToken ct = default);
}

public interface IGlobalInvalidationService
{
    Task InvalidateAllAsync(Guid userId, CancellationToken ct = default);
    Task<bool> IsInvalidatedAsync(Guid userId, long issuedAtUnix, CancellationToken ct = default);
}

public interface IActivationTokenService
{
    string Generate();
    Task StoreAsync(string token, Guid userId, Guid tenantId, CancellationToken ct = default);
    Task<(Guid UserId, Guid TenantId)?> GetAsync(string token, CancellationToken ct = default);
    Task DeleteAsync(string token, CancellationToken ct = default);
    Task<string?> GetTokenByUserAsync(Guid userId, CancellationToken ct = default);
    Task StoreUserIndexAsync(Guid userId, string token, CancellationToken ct = default);
    Task DeleteUserIndexAsync(Guid userId, CancellationToken ct = default);
}

public interface IPasswordResetTokenService
{
    string Generate();
    Task StoreStaffAsync(string token, Guid staffId, CancellationToken ct = default);
    Task<Guid?> GetStaffIdAsync(string token, CancellationToken ct = default);
    Task DeleteStaffAsync(string token, CancellationToken ct = default);
    Task StoreCustomerAsync(string token, Guid customerId, CancellationToken ct = default);
    Task<Guid?> GetCustomerIdAsync(string token, CancellationToken ct = default);
    Task DeleteCustomerAsync(string token, CancellationToken ct = default);
}

public interface ITotpService
{
    string GenerateSecret();
    string EncryptSecret(string base32Secret);
    string DecryptSecret(string encryptedSecret);
    bool ValidateCode(string encryptedSecret, string code);
    string BuildOtpAuthUrl(string email, string base32Secret);
    string GenerateEmergencyCode();
    string HashEmergencyCode(string code);
    bool VerifyEmergencyCode(string code, string hash);
}

public interface IGoogleAuthService
{
    /// <summary>Validates a Google id_token and returns email/name/googleId. Throws on failure.</summary>
    Task<(string Email, string Name, string GoogleId)> ValidateAsync(string idToken, CancellationToken ct = default);
}

public interface IEmailNotificationService
{
    Task SendStaffActivationEmailAsync(string toEmail, string name, string activationUrl, CancellationToken ct = default);
    Task SendStaffPasswordResetEmailAsync(string toEmail, string name, string resetUrl, CancellationToken ct = default);
    Task SendCustomerPasswordResetEmailAsync(string toEmail, string name, string resetUrl, CancellationToken ct = default);
}

/// <summary>
/// Redis rolling-TTL store for KDS device sessions.
/// Key: device:{tenantId}:{deviceId}  TTL: 30d (renewed on each refresh).
/// </summary>
public interface IDeviceSessionService
{
    Task SetAsync(Guid tenantId, string deviceId, Guid? stationId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid tenantId, string deviceId, CancellationToken ct = default);
    Task DeleteAsync(Guid tenantId, string deviceId, CancellationToken ct = default);
}

/// <summary>
/// Redis store for active impersonation sessions.
/// Key: impersonation:{opsUserId}:{tenantId}  TTL: 15 min (renewed on renew).
/// Value: {"jti":"...","reason":"..."}
/// </summary>
public interface IImpersonationSessionService
{
    Task SetAsync(Guid opsUserId, Guid tenantId, string jti, string reason, CancellationToken ct = default);
    Task<(string Jti, string Reason)?> GetAsync(Guid opsUserId, Guid tenantId, CancellationToken ct = default);
    Task DeleteAsync(Guid opsUserId, Guid tenantId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid opsUserId, Guid tenantId, CancellationToken ct = default);
}

/// <summary>In-process audit log sink (writes to structured logger; dedicated table is post-MVP).</summary>
public interface IAuditLogService
{
    Task RecordAsync(string action, Guid actorId, string? targetId, Guid? tenantId,
        IDictionary<string, object?>? metadata = null, CancellationToken ct = default);
}

/// <summary>Resolves front-end URLs for deep links in tokens and emails.</summary>
public interface IFrontendUrlService
{
    string AdminImpersonateUrl(string token);
    string StaffActivationUrl(string activationToken);
    string StaffPasswordResetUrl(string resetToken);
    string CustomerPasswordResetUrl(string resetToken);
}

// ─── Lectura cruzada para las asignaciones (SDD-001 v2.0) ─────────────────────
//
// Identity no es dueño ni de los sectores ni de las estaciones ni de los viajes, pero sin
// consultarlos no puede validar una asignación. Se resuelve como ya lo hace el repositorio en
// sentido contrario (ISessionReadService en Orders): una interfaz aquí, una proyección de solo
// lectura en la infraestructura de este módulo, y ninguna referencia a los otros proyectos.

/// <summary>Sectores del local (<c>floors</c>, módulo Sessions).</summary>
public interface IFloorReadService
{
    /// <summary>True si el sector existe y pertenece a ese restaurante.</summary>
    Task<bool> ExistsAsync(Guid tenantId, Guid floorId, CancellationToken ct = default);
}

/// <summary>Estaciones de preparación (<c>kds_stations</c>, módulo Orders).</summary>
public interface IStationReadService
{
    /// <summary>True si la estación existe y pertenece a ese restaurante.</summary>
    Task<bool> ExistsAsync(Guid tenantId, Guid stationId, CancellationToken ct = default);
}

/// <summary>Viajes de entrega de los runners (<c>delivery_trips</c>, módulo Orders, ADR-027).</summary>
public interface IDeliveryTripReadService
{
    /// <summary>
    /// True si el runner tiene una ruta sin cerrar. Mientras la tenga no puede cambiar de
    /// estación: sus platos reclamados salieron de la anterior.
    /// </summary>
    Task<bool> HasOpenTripAsync(Guid tenantId, Guid runnerId, CancellationToken ct = default);
}

// ─── Estación del runner leída de la base (SDD-001 v2.1 §7, ADR-038, PRD-019) ─────────

/// <summary>
/// Resultado de <see cref="IStaffStationResolver.ResolveAsync"/>. <c>Resolved = false</c> solo
/// cuando no se pudo consultar ni Redis ni Postgres: el llamador conserva entonces la claim
/// <c>stationId</c> del token. Con <c>Resolved = true</c>, <c>StationId = null</c> es «sin
/// estación» (centinela <c>-</c>) y obliga a vaciar la que traiga el token.
/// </summary>
public readonly record struct StaffStationResolution(bool Resolved, Guid? StationId)
{
    public static StaffStationResolution Unresolved => new(false, null);
}

/// <summary>
/// Resuelve en cada petición de staff la estación vigente del runner
/// (<c>user_memberships.assigned_station_id</c>, solo membresías ACTIVE) con caché Redis
/// <c>staff_station:{tenantId}:{userId}</c> de 60 s (uuid o centinela <c>-</c>). Nunca lanza.
/// </summary>
public interface IStaffStationResolver
{
    Task<StaffStationResolution> ResolveAsync(Guid userId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// <c>DEL staff_station:{tenantId}:{userId}</c>. Se llama siempre tras el commit (asignación,
    /// roles, baja). Nunca lanza: si Redis falla, la clave caduca sola en ≤ 60 s.
    /// </summary>
    Task InvalidateAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
}
