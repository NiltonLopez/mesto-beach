using FluentValidation;
using MestoBeach.Shared.Time;
using MediatR;
using MestoBeach.Payments.Application.Common;
using MestoBeach.Payments.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Payments.Application.Queries;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record PaymentSummaryDto(
    Guid Id,
    int AmountCents,
    int TipCents,
    int TotalCents,
    string Method,
    string Status,
    int? SplitIndex,
    int? SplitTotal,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? PayerLabel = null);

public record SessionPaymentStatusDto(
    Guid SessionId,
    int OrdersTotalCents,
    List<PaymentSummaryDto> Payments,
    int TotalPaidCents,
    int RemainingCents,
    bool IsFullyPaid);

public record PaymentConfigDto(
    Guid TenantId,
    bool DigitalPaymentsEnabled,
    bool TipEnabled,
    List<int> TipOptions,
    string? GatewayType);

// ─── GET /api/v1/payments/sessions/{sessionId}/status ─────────────────────────

public record GetSessionPaymentStatusQuery(
    Guid SessionId,
    Guid CallerTenantId,
    string CallerType)    // "staff" | "customer"
    : IRequest<Result<SessionPaymentStatusDto>>;

public sealed class GetSessionPaymentStatusValidator
    : AbstractValidator<GetSessionPaymentStatusQuery>
{
    public GetSessionPaymentStatusValidator() =>
        RuleFor(q => q.SessionId).NotEmpty();
}

public sealed class GetSessionPaymentStatusHandler
    : IRequestHandler<GetSessionPaymentStatusQuery, Result<SessionPaymentStatusDto>>
{
    private readonly IPaymentRepository _payments;
    private readonly ISessionReader _sessionReader;
    private readonly ISessionOrderReader _orderReader;
    private readonly ILogger<GetSessionPaymentStatusHandler> _logger;

    public GetSessionPaymentStatusHandler(
        IPaymentRepository payments,
        ISessionReader sessionReader,
        ISessionOrderReader orderReader,
        ILogger<GetSessionPaymentStatusHandler> logger)
    {
        _payments = payments;
        _sessionReader = sessionReader;
        _orderReader = orderReader;
        _logger = logger;
    }

    public async Task<Result<SessionPaymentStatusDto>> Handle(
        GetSessionPaymentStatusQuery q, CancellationToken ct)
    {
        var session = await _sessionReader.GetAsync(q.SessionId, ct);
        if (session is null)
            return Result.Failure<SessionPaymentStatusDto>(
                Error.NotFound("Sesión no encontrada."));

        if (q.CallerType == "staff" && session.TenantId != q.CallerTenantId)
            return Result.Failure<SessionPaymentStatusDto>(
                Error.Forbidden("Sesión de otro tenant."));

        var paymentList = await _payments.GetBySessionAsync(q.SessionId, ct);
        var ordersTotal = await _orderReader.GetTotalAsync(q.SessionId, ct);

        int totalPaid = paymentList
            .Where(p => p.Status == Domain.Enums.PaymentStatus.Completed)
            .Sum(p => p.TotalCents);

        int remaining = Math.Max(0, ordersTotal.TotalCents - totalPaid);
        bool fullyPaid = remaining == 0 && paymentList.Any(p =>
            p.Status == Domain.Enums.PaymentStatus.Completed);

        var dtos = paymentList.Select(p => new PaymentSummaryDto(
            p.Id, p.AmountCents, p.TipCents, p.TotalCents,
            p.Method.ToString().ToUpperInvariant(),
            p.Status.ToString().ToUpperInvariant(),
            p.SplitIndex, p.SplitTotal, p.CreatedAt, p.CompletedAt,
            p.PayerLabel)).ToList();

        _logger.LogInformation(
            "payment.status.queried {SessionId} {TenantId} {CallerType} TotalPaid={TotalPaid}",
            q.SessionId, session.TenantId, q.CallerType, totalPaid);

        return Result.Success(new SessionPaymentStatusDto(
            q.SessionId, ordersTotal.TotalCents, dtos,
            totalPaid, remaining, fullyPaid));
    }
}

// ─── GET /api/v1/payments/config ─────────────────────────────────────────────

public record GetPaymentConfigQuery(
    Guid TenantId) : IRequest<Result<PaymentConfigDto>>;

public sealed class GetPaymentConfigHandler
    : IRequestHandler<GetPaymentConfigQuery, Result<PaymentConfigDto>>
{
    private readonly IPaymentConfigRepository _configs;

    public GetPaymentConfigHandler(IPaymentConfigRepository configs) => _configs = configs;

    public async Task<Result<PaymentConfigDto>> Handle(
        GetPaymentConfigQuery q, CancellationToken ct)
    {
        var cfg = await _configs.GetByTenantAsync(q.TenantId, ct)
                  ?? Domain.Entities.TenantPaymentConfig.CreateDefault(q.TenantId);

        return Result.Success(new PaymentConfigDto(
            cfg.TenantId, cfg.DigitalPaymentsEnabled,
            cfg.TipEnabled, cfg.TipOptions, cfg.GatewayType));
    }
}

// ─── GET /api/v1/payments  (recent sales list for the restaurant) ─────────────

public record PaymentListItemDto(
    Guid Id,
    Guid SessionId,
    int AmountCents,
    int TipCents,
    int TotalCents,
    string Method,
    string Status,
    string? PayerLabel,
    DateTime CreatedAt);

public record RecentPaymentsDto(
    int Count,
    int TotalCents,
    int TotalTipsCents,
    List<PaymentListItemDto> Items);

public record GetRecentPaymentsQuery(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    int Limit) : IRequest<Result<RecentPaymentsDto>>;

public sealed class GetRecentPaymentsHandler
    : IRequestHandler<GetRecentPaymentsQuery, Result<RecentPaymentsDto>>
{
    private readonly IPaymentRepository _payments;
    public GetRecentPaymentsHandler(IPaymentRepository payments) => _payments = payments;

    public async Task<Result<RecentPaymentsDto>> Handle(GetRecentPaymentsQuery q, CancellationToken ct)
    {
                // El día de negocio se corta a medianoche de Europe/Madrid (VenueTime).
        var (fromUtc, toExclusive) = VenueTime.ToUtcRange(q.From, q.To);

        var list = await _payments.GetByTenantAsync(q.TenantId, fromUtc, toExclusive, q.Limit, ct);

        var items = list.Select(p => new PaymentListItemDto(
            p.Id, p.SessionId, p.AmountCents, p.TipCents, p.TotalCents,
            p.Method.ToString(), p.Status.ToString(), p.PayerLabel, p.CreatedAt)).ToList();

        var completed = list.Where(p => p.Status == Domain.Enums.PaymentStatus.Completed).ToList();
        return Result.Success(new RecentPaymentsDto(
            completed.Count,
            completed.Sum(p => p.TotalCents),
            completed.Sum(p => p.TipCents),
            items));
    }
}
