namespace MestoBeach.Notifications.Application.Common;

public record MaintenanceStatusDto(
    bool Active,
    string? Message,
    string[] Apps,
    DateTime? ActiveFrom,
    DateTime? ActiveUntil,
    Guid? ActivatedBy = null);

public interface IMaintenanceService
{
    Task<MaintenanceStatusDto> GetAsync(string? app, CancellationToken ct);

    Task SetAsync(
        bool active,
        string? message,
        string[] apps,
        DateTime? activeFrom,
        DateTime? activeUntil,
        Guid activatedBy,
        CancellationToken ct);
}
