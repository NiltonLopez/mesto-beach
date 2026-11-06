using MediatR;
using MestoBeach.Identity.Domain.Enums;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Application.Auth.Staff.Commands;

/// <summary>
/// The owner (Admin) launches their own restaurant: moves it from <see cref="TenantStatus.Pending"/>
/// to live so the rest of the staff (cajeros, meseros) can sign in. Idempotent: if it's already
/// active/trial it just returns the current status. A suspended tenant can only be reactivated by Ops.
/// </summary>
public sealed record ActivateTenantCommand(Guid TenantId) : IRequest<Result<TenantActivationDto>>;

public sealed record TenantActivationDto(Guid TenantId, string Name, string Status);

public sealed class ActivateTenantCommandHandler
    : IRequestHandler<ActivateTenantCommand, Result<TenantActivationDto>>
{
    private readonly ITenantRepository _tenants;
    public ActivateTenantCommandHandler(ITenantRepository tenants) => _tenants = tenants;

    public async Task<Result<TenantActivationDto>> Handle(ActivateTenantCommand cmd, CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null)
            return Result.Failure<TenantActivationDto>(Error.NotFound("Restaurante no encontrado."));

        if (tenant.Status == TenantStatus.Suspended)
            return Result.Failure<TenantActivationDto>(
                Error.Forbidden("El restaurante está suspendido. Contacta a Mesto Beach para reactivarlo."));

        if (!tenant.IsActive) // Pending → go live
        {
            tenant.Activate();
            await _tenants.UpdateAsync(tenant, ct);
        }

        return Result.Success(new TenantActivationDto(tenant.Id, tenant.Name, tenant.Status.ToString()));
    }
}
