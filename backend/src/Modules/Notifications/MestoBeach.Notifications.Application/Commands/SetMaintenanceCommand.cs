using FluentValidation;
using MediatR;
using MestoBeach.Notifications.Application.Common;
using MestoBeach.Notifications.Domain.Entities;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using System.Text.Json;

namespace MestoBeach.Notifications.Application.Commands;

file static class AppNames
{
    public static readonly HashSet<string> Valid = new() { "menu", "waiter", "kds", "admin", "ops" };
}

public record SetMaintenanceCommand(
    bool Active,
    string? Message,
    string[] Apps,
    DateTime? ActiveFrom,
    DateTime? ActiveUntil,
    Guid ActivatedBy) : IRequest<Result<MaintenanceStatusDto>>;

public sealed class SetMaintenanceValidator : AbstractValidator<SetMaintenanceCommand>
{
    public SetMaintenanceValidator()
    {
        RuleFor(c => c.Apps)
            .NotNull()
            .Must(apps => apps.All(a => AppNames.Valid.Contains(a)))
            .WithMessage("apps contiene valores inválidos. Permitidos: menu, waiter, kds, admin, ops.");

        // When activating
        When(c => c.Active, () =>
        {
            RuleFor(c => c.Message)
                .NotEmpty()
                .MaximumLength(500)
                .WithMessage("message es requerido al activar mantenimiento (máx. 500 chars).");

            RuleFor(c => c.ActiveFrom)
                .NotNull()
                .Must(f => f <= DateTime.UtcNow.AddHours(24))
                .WithMessage("activeFrom no puede ser más de 24 horas en el futuro.");

            RuleFor(c => c.ActiveUntil)
                .NotNull()
                .WithMessage("activeUntil es requerido al activar mantenimiento.");

            RuleFor(c => c)
                .Must(c => c.ActiveUntil > c.ActiveFrom)
                .WithMessage("activeUntil debe ser mayor que activeFrom.")
                .Must(c => c.ActiveUntil <= c.ActiveFrom!.Value.AddHours(24))
                .WithMessage("activeUntil no puede ser más de 24 horas después de activeFrom.")
                .When(c => c.ActiveFrom.HasValue && c.ActiveUntil.HasValue);
        });
    }
}

public sealed class SetMaintenanceHandler
    : IRequestHandler<SetMaintenanceCommand, Result<MaintenanceStatusDto>>
{
    private readonly IMaintenanceService _maintenance;
    private readonly IBroadcastService _broadcast;
    private readonly IEmailTemplateRepository _auditRepo;

    public SetMaintenanceHandler(
        IMaintenanceService maintenance,
        IBroadcastService broadcast,
        IEmailTemplateRepository auditRepo)
    {
        _maintenance = maintenance;
        _broadcast   = broadcast;
        _auditRepo   = auditRepo;
    }

    public async Task<Result<MaintenanceStatusDto>> Handle(
        SetMaintenanceCommand cmd, CancellationToken ct)
    {
        await _maintenance.SetAsync(
            cmd.Active, cmd.Message, cmd.Apps,
            cmd.ActiveFrom, cmd.ActiveUntil, cmd.ActivatedBy, ct);

        // SignalR broadcast to all clients
        var payload = cmd.Active
            ? (object)new { message = cmd.Message, apps = cmd.Apps, activeFrom = cmd.ActiveFrom, activeUntil = cmd.ActiveUntil }
            : (object)new { active = false, apps = cmd.Apps };

        await _broadcast.BroadcastToAllAsync("maintenance.updated", payload, ct);

        // Audit log
        var audit = new AuditLog(
            "maintenance.set",
            cmd.ActivatedBy,
            JsonSerializer.Serialize(new
            {
                active = cmd.Active,
                message = cmd.Message,
                apps = cmd.Apps,
                activeFrom = cmd.ActiveFrom,
                activeUntil = cmd.ActiveUntil
            }));
        await _auditRepo.AddAuditAsync(audit, ct);
        await _auditRepo.SaveChangesAsync(ct);

        // Return current state
        var result = await _maintenance.GetAsync(null, ct);
        return Result.Success(result with { ActivatedBy = cmd.Active ? cmd.ActivatedBy : null });
    }
}
