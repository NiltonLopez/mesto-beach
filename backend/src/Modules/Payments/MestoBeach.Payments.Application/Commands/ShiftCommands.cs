using FluentValidation;
using MediatR;
using MestoBeach.Payments.Domain.Entities;
using MestoBeach.Payments.Domain.Enums;
using MestoBeach.Payments.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Payments.Application.Commands;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record ShiftDto(
    Guid Id, Guid TenantId, string Status, int OpeningFloatCents,
    Guid OpenedBy, DateTime OpenedAt, DateTime? ClosedAt, string? Note);

public record ShiftMethodBreakdownDto(string Method, int Count, int AmountCents, int TipCents);

public record ShiftReportDto(
    Guid ShiftId,
    string Status,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    int OpeningFloatCents,
    int TotalSalesCents,
    int TotalTipsCents,
    int TotalCommissionCents,
    int ExpectedCashCents,
    int PaymentCount,
    int SessionCount,
    IReadOnlyList<ShiftMethodBreakdownDto> ByMethod);

internal static class ShiftMapper
{
    public static ShiftDto ToDto(Shift s) =>
        new(s.Id, s.TenantId, s.Status.ToString().ToUpperInvariant(), s.OpeningFloatCents,
            s.OpenedBy, s.OpenedAt, s.ClosedAt, s.Note);
}

// ─── Open Shift ───────────────────────────────────────────────────────────────

public record OpenShiftCommand(Guid TenantId, Guid OpenedBy, int OpeningFloatCents)
    : IRequest<Result<ShiftDto>>;

public sealed class OpenShiftValidator : AbstractValidator<OpenShiftCommand>
{
    public OpenShiftValidator() =>
        RuleFor(c => c.OpeningFloatCents).GreaterThanOrEqualTo(0);
}

public sealed class OpenShiftHandler : IRequestHandler<OpenShiftCommand, Result<ShiftDto>>
{
    private readonly IShiftRepository _shifts;
    public OpenShiftHandler(IShiftRepository shifts) => _shifts = shifts;

    public async Task<Result<ShiftDto>> Handle(OpenShiftCommand cmd, CancellationToken ct)
    {
        var open = await _shifts.GetOpenByTenantAsync(cmd.TenantId, ct);
        if (open is not null)
            return Result.Failure<ShiftDto>(Error.Conflict("Ya hay un turno abierto."));

        var shift = new Shift(Guid.NewGuid(), cmd.TenantId, cmd.OpenedBy, cmd.OpeningFloatCents);
        await _shifts.AddAsync(shift, ct);
        await _shifts.SaveChangesAsync(ct);
        return Result.Success(ShiftMapper.ToDto(shift));
    }
}

// ─── Close Shift ──────────────────────────────────────────────────────────────

public record CloseShiftCommand(Guid ShiftId, Guid TenantId, Guid ClosedBy, string? Note)
    : IRequest<Result<ShiftReportDto>>;

public sealed class CloseShiftValidator : AbstractValidator<CloseShiftCommand>
{
    public CloseShiftValidator() =>
        RuleFor(c => c.Note).MaximumLength(500).When(c => c.Note is not null);
}

public sealed class CloseShiftHandler : IRequestHandler<CloseShiftCommand, Result<ShiftReportDto>>
{
    private readonly IShiftRepository _shifts;
    private readonly IPaymentRepository _payments;

    public CloseShiftHandler(IShiftRepository shifts, IPaymentRepository payments)
    {
        _shifts = shifts;
        _payments = payments;
    }

    public async Task<Result<ShiftReportDto>> Handle(CloseShiftCommand cmd, CancellationToken ct)
    {
        var shift = await _shifts.GetByIdAsync(cmd.ShiftId, ct);
        if (shift is null || shift.TenantId != cmd.TenantId)
            return Result.Failure<ShiftReportDto>(Error.NotFound("Turno no encontrado."));
        if (!shift.IsOpen)
            return Result.Failure<ShiftReportDto>(Error.Validation("ALREADY_CLOSED", "El turno ya está cerrado."));

        shift.Close(cmd.ClosedBy, cmd.Note);
        await _shifts.UpdateAsync(shift, ct);
        await _shifts.SaveChangesAsync(ct);

        return Result.Success(await BuildReport(shift, ct));
    }

    private async Task<ShiftReportDto> BuildReport(Shift shift, CancellationToken ct)
    {
        var payments = await _payments.GetByShiftAsync(shift.Id, ct);
        var totalSales = payments.Sum(p => p.AmountCents);
        var totalTips = payments.Sum(p => p.TipCents);
        var totalCommission = payments.Sum(p => p.CommissionCents);
        var cashSales = payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.AmountCents + p.TipCents);
        var byMethod = payments
            .GroupBy(p => p.Method)
            .Select(g => new ShiftMethodBreakdownDto(
                g.Key.ToString().ToUpperInvariant(), g.Count(), g.Sum(p => p.AmountCents), g.Sum(p => p.TipCents)))
            .ToList();
        var sessionCount = payments.Select(p => p.SessionId).Distinct().Count();

        return new ShiftReportDto(
            shift.Id, shift.Status.ToString().ToUpperInvariant(), shift.OpenedAt, shift.ClosedAt,
            shift.OpeningFloatCents, totalSales, totalTips, totalCommission,
            shift.OpeningFloatCents + cashSales, payments.Count, sessionCount, byMethod);
    }
}

// ─── Queries ──────────────────────────────────────────────────────────────────

public record GetOpenShiftQuery(Guid TenantId) : IRequest<Result<ShiftDto?>>;

public sealed class GetOpenShiftHandler : IRequestHandler<GetOpenShiftQuery, Result<ShiftDto?>>
{
    private readonly IShiftRepository _shifts;
    public GetOpenShiftHandler(IShiftRepository shifts) => _shifts = shifts;

    public async Task<Result<ShiftDto?>> Handle(GetOpenShiftQuery q, CancellationToken ct)
    {
        var shift = await _shifts.GetOpenByTenantAsync(q.TenantId, ct);
        return Result.Success(shift is null ? null : ShiftMapper.ToDto(shift));
    }
}

public record GetShiftReportQuery(Guid ShiftId, Guid TenantId) : IRequest<Result<ShiftReportDto>>;

public sealed class GetShiftReportHandler : IRequestHandler<GetShiftReportQuery, Result<ShiftReportDto>>
{
    private readonly IShiftRepository _shifts;
    private readonly IPaymentRepository _payments;

    public GetShiftReportHandler(IShiftRepository shifts, IPaymentRepository payments)
    {
        _shifts = shifts;
        _payments = payments;
    }

    public async Task<Result<ShiftReportDto>> Handle(GetShiftReportQuery q, CancellationToken ct)
    {
        var shift = await _shifts.GetByIdAsync(q.ShiftId, ct);
        if (shift is null || shift.TenantId != q.TenantId)
            return Result.Failure<ShiftReportDto>(Error.NotFound("Turno no encontrado."));

        var payments = await _payments.GetByShiftAsync(shift.Id, ct);
        var cashSales = payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.AmountCents + p.TipCents);
        var byMethod = payments
            .GroupBy(p => p.Method)
            .Select(g => new ShiftMethodBreakdownDto(
                g.Key.ToString().ToUpperInvariant(), g.Count(), g.Sum(p => p.AmountCents), g.Sum(p => p.TipCents)))
            .ToList();

        return Result.Success(new ShiftReportDto(
            shift.Id, shift.Status.ToString().ToUpperInvariant(), shift.OpenedAt, shift.ClosedAt,
            shift.OpeningFloatCents,
            payments.Sum(p => p.AmountCents), payments.Sum(p => p.TipCents), payments.Sum(p => p.CommissionCents),
            shift.OpeningFloatCents + cashSales, payments.Count,
            payments.Select(p => p.SessionId).Distinct().Count(), byMethod));
    }
}
