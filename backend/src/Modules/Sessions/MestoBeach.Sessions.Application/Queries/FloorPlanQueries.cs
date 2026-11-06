using FluentValidation;
using MediatR;
using MestoBeach.Sessions.Application.Commands;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Domain.Entities;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Sessions.Application.Queries;

// ─── List floors (config only) ────────────────────────────────────────────────

public record ListFloorsQuery(Guid TenantId) : IRequest<Result<List<FloorDto>>>;

public sealed class ListFloorsValidator : AbstractValidator<ListFloorsQuery>
{
    public ListFloorsValidator() => RuleFor(q => q.TenantId).NotEmpty();
}

public sealed class ListFloorsHandler : IRequestHandler<ListFloorsQuery, Result<List<FloorDto>>>
{
    private readonly IFloorRepository _floors;
    public ListFloorsHandler(IFloorRepository floors) => _floors = floors;

    public async Task<Result<List<FloorDto>>> Handle(ListFloorsQuery q, CancellationToken ct)
    {
        var floors = await _floors.ListAsync(q.TenantId, ct);
        return Result.Success(floors.Select(FloorDto.From).ToList());
    }
}

// ─── Floor plan (floors + tables + live status), por modo (PRD-019, ADR-038) ──

public record FloorPlanTableDto(
    Guid Id,
    string TableCode,
    Guid? FloorId,
    double? PosX,
    double? PosY,
    int? Seats,
    string Shape,
    bool Active,
    bool HasActiveSession,
    Guid? ActiveSessionId,
    string QrMode,
    bool RequiresPin,
    // Comensales registrados por la Host en la sesión activa (RF-1840). Null si la mesa está
    // libre o si la sesión se abrió sin registrar el pax (autoservicio).
    int? GuestCount = null,
    // PRD-019: clase de puesto, interruptor global (tables.active) y barra de la fila del modo.
    string Kind = DiningTable.KindTable,
    bool ActiveGlobal = true,
    Guid? StationId = null);

public record FloorPlanFloorDto(
    Guid Id, string Name, int SortOrder, int Width, int Height, List<WallSegment> Walls,
    List<string> VisibleIn);

/// <param name="Mode">Modo servido.</param>
/// <param name="ActiveMode">Modo del local ahora mismo.</param>
public record FloorPlanResponse(
    string Mode, string ActiveMode, List<FloorPlanFloorDto> Floors, List<FloorPlanTableDto> Tables);

/// <param name="Mode">RESTAURANT | NIGHT; null = modo activo.</param>
/// <param name="IncludeHidden">Solo Admin: pisos ocultos en el modo y mesas apagadas (por modo o global).</param>
/// <param name="IncludeInactive">Host y Admin: mesas apagadas en el modo cuyo piso está visible.</param>
/// <param name="RequesterRole">Rol del JWT (Admin, Host, ...).</param>
public record GetFloorPlanQuery(
    Guid TenantId,
    string? Mode = null,
    bool IncludeHidden = false,
    bool IncludeInactive = false,
    string? RequesterRole = null) : IRequest<Result<FloorPlanResponse>>;

public sealed class GetFloorPlanValidator : AbstractValidator<GetFloorPlanQuery>
{
    public GetFloorPlanValidator() => RuleFor(q => q.TenantId).NotEmpty();
}

public sealed class GetFloorPlanHandler : IRequestHandler<GetFloorPlanQuery, Result<FloorPlanResponse>>
{
    private readonly IFloorRepository _floors;
    private readonly ITableRepository _tables;
    private readonly ISessionCacheService _cache;
    private readonly ISessionRepository _sessions;
    private readonly IVenueModeProvider _venueMode;
    private readonly ILogger<GetFloorPlanHandler> _logger;

    public GetFloorPlanHandler(
        IFloorRepository floors,
        ITableRepository tables,
        ISessionCacheService cache,
        ISessionRepository sessions,
        IVenueModeProvider venueMode,
        ILogger<GetFloorPlanHandler> logger)
    {
        _floors = floors;
        _tables = tables;
        _cache = cache;
        _sessions = sessions;
        _venueMode = venueMode;
        _logger = logger;
    }

    public async Task<Result<FloorPlanResponse>> Handle(GetFloorPlanQuery q, CancellationToken ct)
    {
        string? requested = null;
        if (q.Mode is not null)
        {
            requested = TableModeRules.ParseMode(q.Mode);
            if (requested is null)
                return Result.Failure<FloorPlanResponse>(
                    Error.Validation(TableModeRules.InvalidModeCode, "mode debe ser RESTAURANT o NIGHT."));
        }

        bool isAdmin = string.Equals(q.RequesterRole, "Admin", StringComparison.OrdinalIgnoreCase);
        bool isHost = string.Equals(q.RequesterRole, "Host", StringComparison.OrdinalIgnoreCase);
        if (q.IncludeHidden && !isAdmin)
            return Result.Failure<FloorPlanResponse>(
                new Error("FORBIDDEN_ROLE", "includeHidden solo está disponible para Admin.", ErrorType.Forbidden));
        if (q.IncludeInactive && !isAdmin && !isHost)
            return Result.Failure<FloorPlanResponse>(
                new Error("FORBIDDEN_ROLE", "includeInactive solo está disponible para Host y Admin.", ErrorType.Forbidden));

        // Sin caché propia: tras un cambio de modo, el siguiente sondeo ya trae el plano nuevo.
        var activeResult = await _venueMode.GetModeAsync(q.TenantId, ct);
        if (activeResult.IsFailure) return Result.Failure<FloorPlanResponse>(activeResult.Error);
        var activeMode = activeResult.Value;
        var mode = requested ?? activeMode;

        if (q.IncludeInactive && !isAdmin && mode != activeMode)
            return Result.Failure<FloorPlanResponse>(
                new Error("FORBIDDEN_ROLE", "Como Host, includeInactive solo vale para el modo activo.", ErrorType.Forbidden));

        var floors = await _floors.ListAsync(q.TenantId, ct);
        var floorsById = floors.ToDictionary(f => f.Id);
        var tables = await _tables.ListAsync(q.TenantId, active: null, zone: null, ct);
        var rows = (await _tables.ListLayoutsAsync(q.TenantId, mode, ct)).ToDictionary(l => l.TableId);

        // Las sesiones abiertas, cargadas de una vez: con ~200 hamacas no vamos a consultar una
        // por una. Mandan sobre el estado libre/ocupada, y además aportan el pax.
        var openByTableId = (await _sessions.GetOpenByTenantAsync(q.TenantId, ct))
            .GroupBy(s => s.TableId)
            .ToDictionary(g => g.Key, g => g.First());

        var tableDtos = new List<FloorPlanTableDto>(tables.Count);
        foreach (var t in tables.OrderBy(t => t.TableCode, StringComparer.OrdinalIgnoreCase))
        {
            rows.TryGetValue(t.Id, out var row);
            bool floorVisible = TableModeRules.FloorVisible(row, floorsById, mode);
            bool activeInMode = row?.Active ?? false;

            if (!q.IncludeHidden)
            {
                // Sin includeHidden: nunca viajan las apagadas en global ni las de pisos ocultos.
                if (!t.Active || !floorVisible) continue;
                // Las apagadas en el modo solo viajan con includeInactive.
                if (!activeInMode && !q.IncludeInactive) continue;
            }

            // Manda la base de datos, no la caché. La clave de Redis caduca a las 4 horas
            // (SessionTtl), mientras que la sesión sigue OPEN hasta que alguien la cierre: una mesa
            // ocupada más de ese rato desaparecía del mapa como libre, pero al pulsarla el backend
            // respondía 409 «ya existe una sesión abierta» —comprobando Postgres— y esa mesa se
            // quedaba imposible de abrir para siempre.
            var abierta = openByTableId.TryGetValue(t.Id, out var open) ? open : null;
            Guid? activeSessionId = abierta?.Id;
            bool hasActive = abierta is not null;

            // La caché solo se consulta si la base no ve nada, por si acabara de abrirse.
            if (!hasActive)
            {
                try
                {
                    var sid = await _cache.GetActiveSessionIdAsync(q.TenantId, t.TableCode);
                    if (sid.HasValue) { hasActive = true; activeSessionId = sid.Value; }
                }
                catch { /* Redis caído: la base ya ha respondido */ }
            }

            // El pax solo tiene sentido si la mesa está ocupada.
            int? guestCount = hasActive ? abierta?.GuestCount : null;

            // Sin fila para el modo: active = false, floorId = null.
            tableDtos.Add(new FloorPlanTableDto(
                t.Id, t.TableCode, row?.FloorId, row?.PosX, row?.PosY, t.Seats, t.Shape, activeInMode,
                hasActive, activeSessionId,
                t.QrMode.ToString().ToUpperInvariant(), t.RequiresPin, guestCount,
                t.Kind, t.Active, row?.StationId));
        }

        var floorDtos = floors
            .Where(f => q.IncludeHidden || f.IsVisibleIn(mode))
            .OrderBy(f => f.SortOrder).ThenBy(f => f.CreatedAt)
            .Select(f => new FloorPlanFloorDto(f.Id, f.Name, f.SortOrder, f.Width, f.Height, f.Walls, f.VisibleIn))
            .ToList();

        _logger.LogInformation(
            "sessions.floorplan.get {TenantId} {Mode} {ActiveMode} {IncludeHidden} {FloorCount} {TableCount} {RequesterRole}",
            q.TenantId, mode, activeMode, q.IncludeHidden, floorDtos.Count, tableDtos.Count, q.RequesterRole);
        SessionsMetrics.FloorplanGet.Add(1,
            new KeyValuePair<string, object?>("mode", mode),
            new KeyValuePair<string, object?>("includeHidden", q.IncludeHidden));

        return Result.Success(new FloorPlanResponse(mode, activeMode, floorDtos, tableDtos));
    }
}
