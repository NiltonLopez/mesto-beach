using MediatR;
using MestoBeach.Catalog.Application.Commands;
using MestoBeach.Catalog.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Catalog.Application.Queries;

/// <summary>Lists the tenant add-on library (modifier groups) with usage counts.</summary>
public sealed record GetModifierGroupsQuery(Guid TenantId, Guid RequesterId)
    : IRequest<Result<IReadOnlyList<ModifierGroupDto>>>;

public sealed class GetModifierGroupsQueryHandler
    : IRequestHandler<GetModifierGroupsQuery, Result<IReadOnlyList<ModifierGroupDto>>>
{
    private readonly ICatalogRepository _repo;
    private readonly ILogger<GetModifierGroupsQueryHandler> _log;

    public GetModifierGroupsQueryHandler(ICatalogRepository repo,
        ILogger<GetModifierGroupsQueryHandler> log)
    {
        _repo = repo;
        _log = log;
    }

    public async Task<Result<IReadOnlyList<ModifierGroupDto>>> Handle(
        GetModifierGroupsQuery query, CancellationToken ct)
    {
        var catalog = await _repo.GetByTenantIdAsync(query.TenantId, ct);
        var groups = catalog?.ModifierGroups ?? [];
        var items = catalog?.Items ?? [];

        var result = groups
            .OrderBy(g => g.Name)
            .Select(g => ModifierGroupMapping.ToDto(
                g,
                items.Count(i => !i.IsDeleted && i.ModifierGroupIds.Contains(g.Id))))
            .ToList();

        _log.LogInformation(
            "catalog.admin.modgroups.list tenantId={TenantId} requesterId={RequesterId} count={Count}",
            query.TenantId, query.RequesterId, result.Count);

        return Result.Success<IReadOnlyList<ModifierGroupDto>>(result);
    }
}
