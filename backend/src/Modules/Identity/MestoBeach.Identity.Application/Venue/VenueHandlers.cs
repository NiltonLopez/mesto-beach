using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;

namespace MestoBeach.Identity.Application.Venue;

// ══════════════════════════════════════════════════════════════════════════════
// MODO DEL LOCAL (ADR-036, SDD-001 §7) — consultas y comando.
// En el wire el modo siempre es string en mayúsculas (mode.ToString()); los nulos se omiten
// por WhenWritingNull.
// ══════════════════════════════════════════════════════════════════════════════

// ─── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>
/// GET /venue/{tenantId} (sin <c>stations</c>, que se omite por WhenWritingNull) y GET /venue
/// (con <c>stations</c>, PR3 — ADR-037).
/// </summary>
public sealed record VenueStateDto(
    Guid TenantId, string Mode, DateTime? ChangedAt,
    IReadOnlyList<VenueStationDto>? Stations = null);

/// <summary><c>isOn</c> calculado con el mismo modo que se devuelve (<c>isActive && activeIn ∋ mode</c>).</summary>
public sealed record VenueStationDto(Guid Id, string Name, string FlowProfile, bool IsOn);

public sealed record VenuePreflightDto(
    bool Ready, int OpenSessions, int ActiveItems, int OpenTrips, int OrphanItems,
    IReadOnlyList<string> Tables);

public sealed record ChangeVenueModeResponse(string Mode, string Previous, bool Changed, DateTime? ChangedAt);

public sealed record VenueHistoryItemDto(
    Guid Id, string FromMode, string ToMode, Guid ChangedBy, string? ChangedByName,
    DateTime ChangedAt, JsonElement Preflight);

public sealed record VenueHistoryDto(IReadOnlyList<VenueHistoryItemDto> Data, int Total);

// ─── Métricas ─────────────────────────────────────────────────────────────────

public static class VenueMetrics
{
    public static readonly Meter Meter = new("MestoBeach.Venue");
    /// <summary>tag <c>source = redis | postgres | unavailable</c>.</summary>
    public static readonly Counter<long> ModeReads = Meter.CreateCounter<long>("venue.mode_reads");
    /// <summary>tags <c>from</c>, <c>to</c>.</summary>
    public static readonly Counter<long> ModeChanges = Meter.CreateCounter<long>("venue.mode_changes");
    public static readonly Counter<long> ModeChangeRejected = Meter.CreateCounter<long>("venue.mode_change_rejected");
}

public static class VenueErrors
{
    public const string NotIdleCode = "VENUE_NOT_IDLE";

    public static readonly Error InvalidMode =
        Error.Validation("INVALID_MODE", "El modo debe ser RESTAURANT o NIGHT.");

    public static Error NotIdle(VenuePreflight p) => new(
        NotIdleCode,
        NotIdleDetail(p),
        ErrorType.Conflict,
        new Dictionary<string, object?>
        {
            ["openSessions"] = p.OpenSessions,
            ["activeItems"] = p.ActiveItems,
            ["openTrips"] = p.OpenTrips,
            ["orphanItems"] = p.OrphanItems,
            ["tables"] = p.Tables,
        });

    private static string NotIdleDetail(VenuePreflight p)
    {
        var partes = new List<string>();
        if (p.OpenSessions > 0)
        {
            var mesas = p.Tables.Count > 0 ? $" ({string.Join(", ", p.Tables)})" : string.Empty;
            partes.Add(p.OpenSessions == 1
                ? $"Hay 1 mesa abierta{mesas}. Ciérrala en caja antes de cambiar de modo."
                : $"Hay {p.OpenSessions} mesas abiertas{mesas}. Ciérralas en caja antes de cambiar de modo.");
        }
        if (p.OpenTrips > 0)
            partes.Add(p.OpenTrips == 1
                ? "Hay 1 bandeja en ruta. Ciérrala antes de cambiar de modo."
                : $"Hay {p.OpenTrips} bandejas en ruta. Ciérralas antes de cambiar de modo.");
        if (partes.Count == 0)
            partes.Add("El local no está vacío. Revisa el pre-flight antes de cambiar de modo.");
        return string.Join(" ", partes);
    }

    internal static VenuePreflightDto ToDto(this VenuePreflight p) =>
        new(p.Ready, p.OpenSessions, p.ActiveItems, p.OpenTrips, p.OrphanItems, p.Tables);
}

// ─── GET /venue/{tenantId} (anónimo) y GET /venue (personal / KDS) ───────────

/// <summary>
/// Modo vigente del tenant. Orden fijo: lectura por PK de <c>tenants</c>; sin fila → 404 sin
/// llamar al proveedor. Lanza <see cref="VenueModeUnavailableException"/> si no se puede leer.
/// </summary>
public sealed record GetVenueStateQuery(Guid TenantId, bool IncludeStations = false) : IRequest<Result<VenueStateDto>>;

public sealed class GetVenueStateHandler : IRequestHandler<GetVenueStateQuery, Result<VenueStateDto>>
{
    private readonly IVenueStore _store;
    private readonly IVenueModeProvider _provider;

    public GetVenueStateHandler(IVenueStore store, IVenueModeProvider provider)
    {
        _store = store;
        _provider = provider;
    }

    public async Task<Result<VenueStateDto>> Handle(GetVenueStateQuery q, CancellationToken ct)
    {
        TenantVenueRow? row;
        try
        {
            row = await _store.GetTenantAsync(q.TenantId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin Postgres no se puede aplicar «404 primero»; se trata como modo inaccesible.
            VenueMetrics.ModeReads.Add(1, new KeyValuePair<string, object?>("source", "unavailable"));
            throw new VenueModeUnavailableException(q.TenantId, ex);
        }

        if (row is null)
            return Result.Failure<VenueStateDto>(Error.NotFound("Restaurante no encontrado."));

        var mode = await _provider.GetAsync(q.TenantId, ct);
        if (!q.IncludeStations)
            return Result.Success(new VenueStateDto(q.TenantId, mode.ToString(), row.ChangedAt));

        // Todas, encendidas o no: el KDS necesita saber que la suya está «Cerrada en Night».
        var modeName = mode.ToString();
        var stations = (await _store.GetStationsAsync(q.TenantId, ct))
            .Select(s => new VenueStationDto(s.Id, s.Name, s.FlowProfile, s.IsActive && s.ActiveIn.Contains(modeName)))
            .ToList();
        return Result.Success(new VenueStateDto(q.TenantId, mode.ToString(), row.ChangedAt, stations));
    }
}

// ─── GET /venue/preflight ─────────────────────────────────────────────────────

public sealed record GetVenuePreflightQuery(Guid TenantId) : IRequest<Result<VenuePreflightDto>>;

public sealed class GetVenuePreflightHandler : IRequestHandler<GetVenuePreflightQuery, Result<VenuePreflightDto>>
{
    private readonly IVenueStore _store;
    private readonly ILogger<GetVenuePreflightHandler> _logger;

    public GetVenuePreflightHandler(IVenueStore store, ILogger<GetVenuePreflightHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<Result<VenuePreflightDto>> Handle(GetVenuePreflightQuery q, CancellationToken ct)
    {
        var p = await _store.GetPreflightAsync(q.TenantId, ct);
        _logger.LogInformation(
            "venue_mode.preflight {TenantId} {Ready} {OpenSessions} {ActiveItems} {OpenTrips} {OrphanItems}",
            q.TenantId, p.Ready, p.OpenSessions, p.ActiveItems, p.OpenTrips, p.OrphanItems);
        return Result.Success(p.ToDto());
    }
}

// ─── POST /venue/mode ─────────────────────────────────────────────────────────

/// <summary>
/// <c>Mode</c> llega como string y se valida aquí (sin validador FluentValidation a propósito:
/// <c>ValidationBehavior</c> taparía <c>INVALID_MODE</c>).
/// </summary>
public sealed record ChangeVenueModeCommand(Guid TenantId, Guid AdminId, string? Mode)
    : IRequest<Result<ChangeVenueModeResponse>>;

public sealed class ChangeVenueModeHandler : IRequestHandler<ChangeVenueModeCommand, Result<ChangeVenueModeResponse>>
{
    private readonly IVenueStore _store;
    private readonly IVenueModeCache _cache;
    private readonly IPublisher _publisher;
    private readonly IAuditLogService _audit;
    private readonly ILogger<ChangeVenueModeHandler> _logger;

    public ChangeVenueModeHandler(
        IVenueStore store,
        IVenueModeCache cache,
        IPublisher publisher,
        IAuditLogService audit,
        ILogger<ChangeVenueModeHandler> logger)
    {
        _store = store;
        _cache = cache;
        _publisher = publisher;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<ChangeVenueModeResponse>> Handle(ChangeVenueModeCommand cmd, CancellationToken ct)
    {
        // Enum.GetNames + OrdinalIgnoreCase, no Enum.TryParse (aceptaría "1" o "5").
        var name = Enum.GetNames<VenueMode>()
            .FirstOrDefault(n => string.Equals(n, cmd.Mode?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return Result.Failure<ChangeVenueModeResponse>(VenueErrors.InvalidMode);
        var mode = Enum.Parse<VenueMode>(name);

        var sw = Stopwatch.StartNew();
        var now = DateTime.UtcNow;

        // Pasos 1–3: idempotencia con bloqueo de fila, pre-flight y escritura en una transacción.
        var outcome = await _store.ChangeModeAsync(cmd.TenantId, mode, cmd.AdminId, now, ct);

        switch (outcome)
        {
            case VenueChangeOutcome.TenantNotFound:
                return Result.Failure<ChangeVenueModeResponse>(Error.NotFound("Restaurante no encontrado."));

            case VenueChangeOutcome.AlreadyInMode same:
                // Reintentar el POST repara una clave vieja; sin evento ni auditoría.
                await TrySetCacheAsync(cmd.TenantId, same.Mode, deleteOnFailure: false, ct);
                return Result.Success(new ChangeVenueModeResponse(
                    same.Mode.ToString(), same.Mode.ToString(), false, same.ChangedAt));

            case VenueChangeOutcome.NotIdle notIdle:
                var p = notIdle.Preflight;
                _logger.LogInformation(
                    "venue_mode.rejected_not_idle {TenantId} {OpenSessions} {ActiveItems} {OpenTrips} {OrphanItems}",
                    cmd.TenantId, p.OpenSessions, p.ActiveItems, p.OpenTrips, p.OrphanItems);
                VenueMetrics.ModeChangeRejected.Add(1);
                return Result.Failure<ChangeVenueModeResponse>(VenueErrors.NotIdle(p));

            case VenueChangeOutcome.Changed changed:
                // 4. Redis: SET incondicional; si falla, DEL best-effort (el TTL acota el resto).
                await TrySetCacheAsync(cmd.TenantId, mode, deleteOnFailure: true, ct);

                // 5. Notificación best-effort: Orders difunde VENUE_MODE_CHANGED, Catalog invalida caché.
                try
                {
                    await _publisher.Publish(new VenueModeChangedNotification(
                        cmd.TenantId, mode, changed.Previous, changed.ChangedAt, cmd.AdminId), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "venue_mode.notify_failed {TenantId} {Handler}",
                        cmd.TenantId, nameof(VenueModeChangedNotification));
                }

                // 6. Auditoría y log.
                await _audit.RecordAsync("venue_mode_changed", cmd.AdminId, cmd.TenantId.ToString(), cmd.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["from"] = changed.Previous.ToString(),
                        ["to"] = mode.ToString(),
                        ["preflight"] = changed.Preflight.ToDto(),
                    }, ct);

                sw.Stop();
                _logger.LogInformation(
                    "venue_mode.changed {TenantId} {From} {To} {ChangedBy} {DurationMs}",
                    cmd.TenantId, changed.Previous, mode, cmd.AdminId, sw.ElapsedMilliseconds);
                VenueMetrics.ModeChanges.Add(1,
                    new KeyValuePair<string, object?>("from", changed.Previous.ToString()),
                    new KeyValuePair<string, object?>("to", mode.ToString()));

                return Result.Success(new ChangeVenueModeResponse(
                    mode.ToString(), changed.Previous.ToString(), true, changed.ChangedAt));

            default:
                throw new InvalidOperationException($"Resultado de cambio de modo no contemplado: {outcome}");
        }
    }

    private async Task TrySetCacheAsync(Guid tenantId, VenueMode mode, bool deleteOnFailure, CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(tenantId, mode, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "venue_mode.redis_set_failed {TenantId}", tenantId);
            if (!deleteOnFailure) return;
            try { await _cache.DeleteAsync(tenantId, ct); }
            catch (Exception delEx) when (delEx is not OperationCanceledException)
            {
                _logger.LogError(delEx, "venue_mode.redis_del_failed {TenantId}", tenantId);
            }
        }
    }
}

// ─── GET /venue/history ───────────────────────────────────────────────────────

public sealed record GetVenueHistoryQuery(Guid TenantId, int Limit) : IRequest<Result<VenueHistoryDto>>;

public sealed class GetVenueHistoryHandler : IRequestHandler<GetVenueHistoryQuery, Result<VenueHistoryDto>>
{
    private readonly IVenueStore _store;

    public GetVenueHistoryHandler(IVenueStore store) => _store = store;

    public async Task<Result<VenueHistoryDto>> Handle(GetVenueHistoryQuery q, CancellationToken ct)
    {
        if (q.Limit is < 1 or > 100)
            return Result.Failure<VenueHistoryDto>(
                Error.Validation("INVALID_LIMIT", "limit debe estar entre 1 y 100."));

        var (rows, total) = await _store.GetHistoryAsync(q.TenantId, q.Limit, ct);
        var data = rows.Select(r => new VenueHistoryItemDto(
                r.Id, r.FromMode.ToString(), r.ToMode.ToString(), r.ChangedBy, r.ChangedByName,
                r.ChangedAt, ParsePreflight(r.PreflightJson)))
            .ToList();
        return Result.Success(new VenueHistoryDto(data, total));
    }

    private static JsonElement ParsePreflight(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }
}
