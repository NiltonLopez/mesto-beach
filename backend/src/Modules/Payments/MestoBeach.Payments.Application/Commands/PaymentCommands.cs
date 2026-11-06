using FluentValidation;
using MediatR;
using MestoBeach.Payments.Application.Common;
using MestoBeach.Payments.Domain.Entities;
using MestoBeach.Payments.Domain.Enums;
using MestoBeach.Payments.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.Logging;

namespace MestoBeach.Payments.Application.Commands;

// ══════════════════════════════════════════════════════════════════════════════
// REGISTER CASH PAYMENT
// ══════════════════════════════════════════════════════════════════════════════

public record RegisterCashPaymentDto(
    Guid PaymentId,
    Guid SessionId,
    int AmountCents,
    int TipCents,
    int TotalCents,
    int CommissionCents,
    string Status,
    int? SplitIndex,
    int? SplitTotal,
    bool SessionClosed,
    DateTime CreatedAt);

public record RegisterCashPaymentCommand(
    Guid SessionId,
    int AmountCents,
    int TipCents,
    int? SplitIndex,
    int? SplitTotal,
    Guid CallerTenantId,
    Guid ProcessedBy,
    string? PayerLabel = null) : IRequest<Result<RegisterCashPaymentDto>>;

public sealed class RegisterCashPaymentValidator : AbstractValidator<RegisterCashPaymentCommand>
{
    public RegisterCashPaymentValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.AmountCents).GreaterThan(0).WithMessage("amountCents debe ser mayor a 0.");
        RuleFor(c => c.TipCents).GreaterThanOrEqualTo(0);
        RuleFor(c => c).Must(c =>
                (c.SplitIndex == null && c.SplitTotal == null) ||
                (c.SplitIndex != null && c.SplitTotal != null &&
                 c.SplitTotal >= 2 && c.SplitIndex >= 1 && c.SplitIndex <= c.SplitTotal))
            .WithMessage("splitIndex y splitTotal deben ser consistentes (splitTotal >= 2, 1 <= splitIndex <= splitTotal).");
    }
}

public sealed class RegisterCashPaymentHandler
    : IRequestHandler<RegisterCashPaymentCommand, Result<RegisterCashPaymentDto>>
{
    private readonly IPaymentRepository _payments;
    private readonly IPaymentConfigRepository _configs;
    private readonly IShiftRepository _shifts;
    private readonly ISessionReader _sessionReader;
    private readonly ISessionOrderReader _orderReader;
    private readonly ISessionCloser _sessionCloser;
    private readonly ILogger<RegisterCashPaymentHandler> _logger;

    public RegisterCashPaymentHandler(
        IPaymentRepository payments,
        IPaymentConfigRepository configs,
        IShiftRepository shifts,
        ISessionReader sessionReader,
        ISessionOrderReader orderReader,
        ISessionCloser sessionCloser,
        ILogger<RegisterCashPaymentHandler> logger)
    {
        _payments = payments;
        _configs = configs;
        _shifts = shifts;
        _sessionReader = sessionReader;
        _orderReader = orderReader;
        _sessionCloser = sessionCloser;
        _logger = logger;
    }

    public async Task<Result<RegisterCashPaymentDto>> Handle(
        RegisterCashPaymentCommand cmd, CancellationToken ct)
    {
        // ── Validate session ──────────────────────────────────────────────────
        var session = await _sessionReader.GetAsync(cmd.SessionId, ct);
        if (session is null || session.TenantId != cmd.CallerTenantId)
            return Result.Failure<RegisterCashPaymentDto>(
                Error.NotFound("Sesión no encontrada."));

        if (!session.IsOpen)
            return Result.Failure<RegisterCashPaymentDto>(
                Error.Conflict("La sesión ya está cerrada."));

        // ── Duplicate split check ─────────────────────────────────────────────
        if (cmd.SplitIndex.HasValue)
        {
            var existing = await _payments.GetSplitPaymentAsync(cmd.SessionId, cmd.SplitIndex.Value, ct);
            if (existing is not null)
                return Result.Failure<RegisterCashPaymentDto>(
                    Error.Conflict($"El split {cmd.SplitIndex} ya fue registrado para esta sesión."));
        }

        // ── Commission calculation ────────────────────────────────────────────
        var config = await _configs.GetByTenantAsync(cmd.CallerTenantId, ct)
                     ?? TenantPaymentConfig.CreateDefault(cmd.CallerTenantId);

        // Dinero con decimal, no con double: CommissionPct ya es decimal y pasar por
        // coma flotante introduce error de representación en un importe que se cobra.
        // MidpointRounding.AwayFromZero porque el redondeo bancario por defecto de .NET
        // sorprende al contable (2,5 -> 2 en vez de 3).
        int commissionCents = (int)Math.Round(
            cmd.AmountCents * config.CommissionPct / 100m,
            MidpointRounding.AwayFromZero);

        // ── Create payment (cash → Completed immediately) ─────────────────────
        var payment = new Payment(
            Guid.NewGuid(),
            cmd.CallerTenantId,
            cmd.SessionId,
            PaymentMethod.Cash,
            cmd.AmountCents,
            cmd.TipCents,
            commissionCents,
            gatewayReference: null,
            gatewayType: null,
            cmd.SplitIndex,
            cmd.SplitTotal,
            cmd.ProcessedBy,
            cmd.PayerLabel);

        // Stamp the open cashier shift (if any) for the cash report.
        var openShift = await _shifts.GetOpenByTenantAsync(cmd.CallerTenantId, ct);
        if (openShift is not null) payment.AssignShift(openShift.Id);

        await _payments.AddAsync(payment, ct);
        await _payments.SaveChangesAsync(ct);

        // ── Auto-close session ────────────────────────────────────────────────
        // Close when the bill is fully paid (running total ≥ orders total). This supports
        // partial cash payments — e.g. paying only selected products — without closing early.
        bool sessionClosed = false;

        var allPayments = await _payments.GetBySessionAsync(cmd.SessionId, ct);
        int totalPaid = allPayments
            .Where(p => p.Status == PaymentStatus.Completed)
            .Sum(p => p.AmountCents);
        var ordersTotal = await _orderReader.GetTotalAsync(cmd.SessionId, ct);
        bool fullyPaid = totalPaid >= ordersTotal.TotalCents;

        bool shouldClose;
        if (cmd.SplitTotal.HasValue)
        {
            // Equal/per-person split: close when all parts are in, or the bill is fully covered.
            int completedSplits = await _payments.CountCompletedSplitsAsync(cmd.SessionId, ct);
            shouldClose = fullyPaid || completedSplits >= cmd.SplitTotal.Value;
        }
        else
        {
            // Full or partial-by-amount payment: only close once the whole bill is paid.
            shouldClose = fullyPaid;
        }

        if (shouldClose)
        {
            sessionClosed = await _sessionCloser.CloseAsync(
                cmd.CallerTenantId, cmd.SessionId, cmd.ProcessedBy, ct);
        }

        _logger.LogInformation(
            "payment.cash.registered {PaymentId} {SessionId} {TenantId} {AmountCents} " +
            "{TipCents} {SplitIndex} {SplitTotal} {SessionClosed} {ProcessedBy}",
            payment.Id, cmd.SessionId, cmd.CallerTenantId,
            cmd.AmountCents, cmd.TipCents, cmd.SplitIndex, cmd.SplitTotal,
            sessionClosed, cmd.ProcessedBy);

        return Result.Success(new RegisterCashPaymentDto(
            payment.Id, payment.SessionId,
            payment.AmountCents, payment.TipCents, payment.TotalCents, payment.CommissionCents,
            "COMPLETED", payment.SplitIndex, payment.SplitTotal,
            sessionClosed, payment.CreatedAt));
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// UPSERT PAYMENT CONFIG
// ══════════════════════════════════════════════════════════════════════════════

public record UpsertPaymentConfigDto(
    Guid TenantId,
    bool TipEnabled,
    List<int> TipOptions,
    bool DigitalPaymentsEnabled,
    DateTime UpdatedAt);

public record UpsertPaymentConfigCommand(
    Guid TenantId,
    bool? TipEnabled,
    List<int>? TipOptions,
    Guid RequesterId) : IRequest<Result<UpsertPaymentConfigDto>>;

public sealed class UpsertPaymentConfigValidator : AbstractValidator<UpsertPaymentConfigCommand>
{
    public UpsertPaymentConfigValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.TipOptions)
            .Must(opts => opts!.Count <= 5).When(c => c.TipOptions is not null)
            .WithMessage("Máximo 5 opciones de propina.")
            .ForEach(pct => pct.InclusiveBetween(1, 100).WithMessage("Cada % de propina debe estar entre 1 y 100."))
            .When(c => c.TipOptions is not null);
        RuleFor(c => c)
            .Must(c => c.TipEnabled.HasValue || c.TipOptions is not null)
            .WithMessage("Body vacío: debe proveer tipEnabled o tipOptions.");
    }
}

public sealed class UpsertPaymentConfigHandler
    : IRequestHandler<UpsertPaymentConfigCommand, Result<UpsertPaymentConfigDto>>
{
    private readonly IPaymentConfigRepository _configs;
    private readonly ILogger<UpsertPaymentConfigHandler> _logger;

    public UpsertPaymentConfigHandler(
        IPaymentConfigRepository configs,
        ILogger<UpsertPaymentConfigHandler> logger)
    {
        _configs = configs;
        _logger = logger;
    }

    public async Task<Result<UpsertPaymentConfigDto>> Handle(
        UpsertPaymentConfigCommand cmd, CancellationToken ct)
    {
        var config = await _configs.GetByTenantAsync(cmd.TenantId, ct)
                     ?? TenantPaymentConfig.CreateDefault(cmd.TenantId);

        config.UpdateTip(cmd.TipEnabled, cmd.TipOptions);
        await _configs.UpsertAsync(config, ct);
        await _configs.SaveChangesAsync(ct);

        _logger.LogInformation(
            "payment_config.updated {TenantId} {TipEnabled} {UpdatedBy}",
            cmd.TenantId, config.TipEnabled, cmd.RequesterId);

        return Result.Success(new UpsertPaymentConfigDto(
            config.TenantId, config.TipEnabled, config.TipOptions,
            config.DigitalPaymentsEnabled, config.UpdatedAt));
    }
}
