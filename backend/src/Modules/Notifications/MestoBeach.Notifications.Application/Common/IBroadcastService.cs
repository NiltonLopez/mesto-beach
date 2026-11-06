namespace MestoBeach.Notifications.Application.Common;

public interface IBroadcastService
{
    /// <summary>
    /// Sends a SignalR event to a specific group.
    /// Returns the estimated number of recipients (0 if no one connected — not an error).
    /// </summary>
    Task<int> BroadcastToGroupAsync(
        string group,
        string eventName,
        object payload,
        CancellationToken ct);

    /// <summary>
    /// Sends a SignalR event to ALL connected clients.
    /// Reserved for maintenance.updated events only.
    /// </summary>
    Task<int> BroadcastToAllAsync(
        string eventName,
        object payload,
        CancellationToken ct);
}
