using MediatR;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Sessions.Application.Queries;

public record ClosedSessionDto(
    Guid SessionId,
    string TableCode,
    DateTime CreatedAt,
    DateTime? ClosedAt,
    int ParticipantCount);

public record ClosedSessionsPageDto(
    IReadOnlyList<ClosedSessionDto> Items,
    int Total,
    int Page,
    int PageSize);

public record ListClosedSessionsQuery(
    Guid TenantId,
    DateTime? From,
    DateTime? To,
    string? TableCode,
    int Page,
    int PageSize) : IRequest<Result<ClosedSessionsPageDto>>;

public sealed class ListClosedSessionsHandler
    : IRequestHandler<ListClosedSessionsQuery, Result<ClosedSessionsPageDto>>
{
    private readonly ISessionRepository _sessions;
    public ListClosedSessionsHandler(ISessionRepository sessions) => _sessions = sessions;

    public async Task<Result<ClosedSessionsPageDto>> Handle(ListClosedSessionsQuery q, CancellationToken ct)
    {
        var page = q.Page < 1 ? 1 : q.Page;
        var pageSize = q.PageSize is < 1 or > 100 ? 50 : q.PageSize;

        // `from`/`to` arrive as date-only DateTimes with Kind=Unspecified; PostgreSQL
        // `timestamptz` only accepts UTC. Normalize to a UTC [start-of-day, end-of-day]
        // range so the filter doesn't crash and a single-day filter includes that whole day.
        DateTime? from = q.From.HasValue
            ? DateTime.SpecifyKind(q.From.Value.Date, DateTimeKind.Utc)
            : null;
        DateTime? to = q.To.HasValue
            ? DateTime.SpecifyKind(q.To.Value.Date, DateTimeKind.Utc).AddDays(1).AddTicks(-1)
            : null;

        var total = await _sessions.CountClosedByTenantAsync(q.TenantId, from, to, q.TableCode, ct);
        var sessions = await _sessions.GetClosedByTenantAsync(
            q.TenantId, from, to, q.TableCode, pageSize, (page - 1) * pageSize, ct);

        var items = new List<ClosedSessionDto>(sessions.Count);
        foreach (var s in sessions)
        {
            var participants = await _sessions.CountParticipantsAsync(s.Id, ct);
            items.Add(new ClosedSessionDto(s.Id, s.TableCode, s.CreatedAt, s.ClosedAt, participants));
        }

        return Result.Success(new ClosedSessionsPageDto(items, total, page, pageSize));
    }
}
