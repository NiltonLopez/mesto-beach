using MestoBeach.Payments.Application.Common;
using MestoBeach.Sessions.Application.Commands;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Payments.Infrastructure.Services;

public sealed class SessionCloserService : ISessionCloser
{
    private readonly ISender _mediator;
    private readonly ILogger<SessionCloserService> _logger;

    public SessionCloserService(ISender mediator, ILogger<SessionCloserService> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<bool> CloseAsync(
        Guid tenantId, Guid sessionId, Guid requesterId, CancellationToken ct = default)
    {
        try
        {
            var result = await _mediator.Send(new CloseSessionCommand(
                tenantId, sessionId,
                ClosureReason: "PAYMENT_COMPLETED",
                RequesterId: requesterId), ct);

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "payment.session.closed {SessionId} {TenantId} {RequestedBy}",
                    sessionId, tenantId, requesterId);
                return true;
            }

            // Already closed or not found — not an error for the payment flow
            _logger.LogInformation(
                "payment.session.close_skipped {SessionId} {TenantId} {Error}",
                sessionId, tenantId, result.Error.Code);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "payment.session.close_failed {SessionId} {TenantId}",
                sessionId, tenantId);
            return false;
        }
    }
}
