using FluentValidation;
using MediatR;
using MestoBeach.Notifications.Application.Common;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Notifications.Application.Queries;

public record GetMaintenanceQuery(string? App) : IRequest<Result<MaintenanceStatusDto>>;

public sealed class GetMaintenanceValidator : AbstractValidator<GetMaintenanceQuery>
{
    private static readonly HashSet<string> ValidApps = new() { "menu", "waiter", "kds", "admin", "ops" };

    public GetMaintenanceValidator()
    {
        RuleFor(q => q.App)
            .Must(a => a is null || ValidApps.Contains(a))
            .WithMessage("app debe ser uno de: menu, waiter, kds, admin, ops.");
    }
}

public sealed class GetMaintenanceHandler
    : IRequestHandler<GetMaintenanceQuery, Result<MaintenanceStatusDto>>
{
    private readonly IMaintenanceService _maintenance;

    public GetMaintenanceHandler(IMaintenanceService maintenance) => _maintenance = maintenance;

    public async Task<Result<MaintenanceStatusDto>> Handle(
        GetMaintenanceQuery q, CancellationToken ct)
    {
        var dto = await _maintenance.GetAsync(q.App, ct);
        return Result.Success(dto);
    }
}
