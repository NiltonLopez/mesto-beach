using MediatR;
using Microsoft.Extensions.Logging;
using MestoBeach.Sessions.Application.Commands;
using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Domain.Entities;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Primitives;
using MestoBeach.Shared.Venue;

namespace MestoBeach.Sessions.Application.Queries;

public record ListWaiterCallsQuery(Guid TenantId) : IRequest<Result<List<WaiterCallDto>>>;

/// <summary>
/// GET /sessions/calls. (PRD-019, SDD-003) Cada aviso gana <c>tableKind</c>, resuelto al listar
/// desde <c>tables.kind</c> por <c>tableCode</c> (no se guarda en el aviso). En NIGHT la lista sale
/// con los reservados primero y luego por <c>createdAt</c> ascendente; en RESTAURANT, solo por
/// <c>createdAt</c>, como hasta ahora.
/// </summary>
public sealed class ListWaiterCallsHandler : IRequestHandler<ListWaiterCallsQuery, Result<List<WaiterCallDto>>>
{
    private readonly IWaiterCallService _calls;
    private readonly ITableRepository _tables;
    private readonly IVenueModeProvider _venueMode;
    private readonly ILogger<ListWaiterCallsHandler> _logger;

    public ListWaiterCallsHandler(
        IWaiterCallService calls, ITableRepository tables, IVenueModeProvider venueMode,
        ILogger<ListWaiterCallsHandler> logger)
    {
        _calls = calls;
        _tables = tables;
        _venueMode = venueMode;
        _logger = logger;
    }

    public async Task<Result<List<WaiterCallDto>>> Handle(ListWaiterCallsQuery q, CancellationToken ct)
    {
        var items = await _calls.ListByTenantAsync(q.TenantId, ct);
        if (items.Count == 0) return Result.Success(new List<WaiterCallDto>());

        var kinds = (await _tables.ListAsync(q.TenantId, null, null, ct))
            .GroupBy(t => t.TableCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Kind, StringComparer.OrdinalIgnoreCase);

        var dtos = items
            .Select(c => new WaiterCallDto(
                c.TenantId, c.SessionId, c.TableCode, c.CreatedAt, c.Reason,
                kinds.TryGetValue(c.TableCode, out var k) ? k : DiningTable.KindTable))
            .ToList();

        // Precedencia de reservados SOLO en NIGHT. Si el modo no se puede leer, se sirve el orden
        // de siempre (por llegada): la lista no debe caerse por eso.
        var night = false;
        try { night = await _venueMode.GetAsync(q.TenantId, ct) == VenueMode.NIGHT; }
        catch (VenueModeUnavailableException ex)
        {
            _logger.LogWarning(ex, "waiter_calls.mode_unavailable {TenantId}", q.TenantId);
        }

        var ordenados = night
            ? dtos.OrderByDescending(d => d.TableKind == DiningTable.KindReservado).ThenBy(d => d.CreatedAt)
            : dtos.OrderBy(d => d.CreatedAt);
        return Result.Success(ordenados.ToList());
    }
}
