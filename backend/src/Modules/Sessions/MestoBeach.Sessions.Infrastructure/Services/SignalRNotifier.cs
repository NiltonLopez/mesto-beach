using MestoBeach.Sessions.Application.Common;
using MestoBeach.Sessions.Infrastructure.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MestoBeach.Sessions.Infrastructure.Services;

public sealed class SignalRNotifier : ISignalRNotifier
{
    private readonly IHubContext<SessionsHub> _hub;

    public SignalRNotifier(IHubContext<SessionsHub> hub) => _hub = hub;

    public Task NotifyWaitersAsync(Guid tenantId, object payload, CancellationToken ct) =>
        _hub.Clients.Group($"waiter-{tenantId}").SendAsync("SessionEvent", payload, ct);
}
