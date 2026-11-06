using MediatR;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Application.Auth.Staff.Queries;

public record StaffProfileDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Email,
    IReadOnlyList<string> Roles,
    string Status,
    DateTime CreatedAt,
    DateTime? LastLogin,
    // Asignaciones de Mesto Beach (SDD-001 v2.0): el PDA y el Runner necesitan su sector y su
    // estación al arrancar la app, sin tener que abrir el JWT.
    Guid? AssignedFloorId = null,
    Guid? AssignedStationId = null);

public record GetStaffProfileQuery(Guid UserId, Guid TenantId) : IRequest<Result<StaffProfileDto>>;

public sealed class GetStaffProfileHandler
    : IRequestHandler<GetStaffProfileQuery, Result<StaffProfileDto>>
{
    private readonly IUserRepository _users;
    private readonly IUserMembershipRepository _memberships;

    public GetStaffProfileHandler(IUserRepository users, IUserMembershipRepository memberships)
    {
        _users = users;
        _memberships = memberships;
    }

    public async Task<Result<StaffProfileDto>> Handle(
        GetStaffProfileQuery request,
        CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return Result.Failure<StaffProfileDto>(Error.NotFound("Usuario no encontrado."));

        if (!user.IsActive)
            return Result.Failure<StaffProfileDto>(Error.Forbidden("Cuenta inactiva."));

        var membership = await _memberships.GetByUserAndTenantAsync(request.UserId, request.TenantId, ct);
        if (membership is null || !membership.IsActive)
            return Result.Failure<StaffProfileDto>(Error.Forbidden("No tiene acceso a este restaurante."));

        return Result.Success(new StaffProfileDto(
            user.Id,
            request.TenantId,
            user.Name,
            user.Email,
            membership.Roles.Select(r => r.ToString()).ToList(),
            user.Status.ToString(),
            user.CreatedAt,
            user.LastLogin,
            membership.AssignedFloorId,
            membership.AssignedStationId));
    }
}
