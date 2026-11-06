using FluentValidation;
using MediatR;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;
using MestoBeach.Sessions.Domain.Entities;

namespace MestoBeach.Sessions.Application.Queries;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record TableDto(
    Guid Id,
    Guid TenantId,
    string TableCode,
    string? Zone,
    bool Active,
    bool HasActiveSession,
    Guid? ActiveSessionId,
    string QrMode,
    bool RequiresPin,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Kind = DiningTable.KindTable);

public record GetTablesResponse(List<TableDto> Tables, int Total);

// ─── Query ────────────────────────────────────────────────────────────────────

public record GetTablesQuery(
    Guid TenantId,
    bool? Active,
    string? Zone,
    Guid RequesterId) : IRequest<Result<GetTablesResponse>>;

// ─── Validator ────────────────────────────────────────────────────────────────

public sealed class GetTablesValidator : AbstractValidator<GetTablesQuery>
{
    public GetTablesValidator()
    {
        RuleFor(q => q.TenantId).NotEmpty().WithMessage("tenantId es requerido.");
    }
}

// ─── Handler ─────────────────────────────────────────────────────────────────

public sealed class GetTablesHandler : IRequestHandler<GetTablesQuery, Result<GetTablesResponse>>
{
    private readonly ITableRepository _tables;
    private readonly ISessionCacheService _cache;
    private readonly ISessionRepository _sessions;
    private readonly IFloorRepository _floors;
    private readonly IVenueModeProvider _venueMode;

    public GetTablesHandler(
        ITableRepository tables,
        ISessionCacheService cache,
        ISessionRepository sessions,
        IFloorRepository floors,
        IVenueModeProvider venueMode)
    {
        _tables = tables;
        _cache = cache;
        _sessions = sessions;
        _floors = floors;
        _venueMode = venueMode;
    }

    public async Task<Result<GetTablesResponse>> Handle(GetTablesQuery q, CancellationToken ct)
    {
        var tables = await _tables.ListAsync(q.TenantId, q.Active, q.Zone, ct);

        // PRD-019: solo las mesas en servicio en el modo activo (tables.active, piso visible o sin
        // piso, y fila del modo con active = true). Una mesa sin fila queda fuera. El modo se
        // resuelve en cada petición, sin caché propia.
        var modeResult = await _venueMode.GetModeAsync(q.TenantId, ct);
        if (modeResult.IsFailure) return Result.Failure<GetTablesResponse>(modeResult.Error);
        var mode = modeResult.Value;
        var floorsById = (await _floors.ListAsync(q.TenantId, ct)).ToDictionary(f => f.Id);
        var rows = (await _tables.ListLayoutsAsync(q.TenantId, mode, ct)).ToDictionary(l => l.TableId);
        tables = tables
            .Where(t => TableModeRules.InService(t, rows.GetValueOrDefault(t.Id), floorsById, mode))
            .ToList();

        // Las sesiones abiertas, de una vez. Mandan sobre el estado libre/ocupada: la clave
        // de Redis caduca a las 4 horas (SessionTtl) mientras que la sesión sigue OPEN hasta
        // que alguien la cierre, así que una mesa ocupada más de ese rato aparecía libre aquí
        // pero el backend respondía 409 al intentar abrirla —comprueba Postgres—, y esa mesa
        // se quedaba imposible de abrir para siempre.
        var openByTableId = (await _sessions.GetOpenByTenantAsync(q.TenantId, ct))
            .GroupBy(s => s.TableId)
            .ToDictionary(g => g.Key, g => g.First());

        var dtos = new List<TableDto>(tables.Count);
        foreach (var t in tables)
        {
            var abierta = openByTableId.TryGetValue(t.Id, out var open) ? open : null;
            Guid? activeSessionId = abierta?.Id;
            bool hasActive = abierta is not null;

            // La caché solo se mira si la base no ve nada, por si acabara de abrirse.
            if (!hasActive)
            {
                try
                {
                    var sessionId = await _cache.GetActiveSessionIdAsync(q.TenantId, t.TableCode);
                    if (sessionId.HasValue)
                    {
                        hasActive = true;
                        activeSessionId = sessionId.Value;
                    }
                }
                catch { /* Redis caído: la base ya ha respondido */ }
            }

            dtos.Add(new TableDto(
                t.Id, t.TenantId, t.TableCode, t.Zone, t.Active,
                hasActive, activeSessionId,
                t.QrMode.ToString().ToUpperInvariant(), t.RequiresPin,
                t.CreatedAt, t.UpdatedAt, t.Kind));
        }

        // Sort: zone ASC, tableCode ASC (nulls last)
        dtos.Sort((a, b) =>
        {
            int zoneCompare = string.Compare(a.Zone ?? "\uFFFF", b.Zone ?? "\uFFFF", StringComparison.OrdinalIgnoreCase);
            return zoneCompare != 0 ? zoneCompare : string.Compare(a.TableCode, b.TableCode, StringComparison.OrdinalIgnoreCase);
        });

        return Result.Success(new GetTablesResponse(dtos, dtos.Count));
    }
}
