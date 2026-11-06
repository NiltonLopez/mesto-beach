using FluentValidation;
using MediatR;
using MestoBeach.Analytics.Application.Common;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Analytics.Application.Queries;

// ── Shared validation helpers ─────────────────────────────────────────────────

file static class DateRangeRules
{
    public const int MaxDays = 366;

    public static IRuleBuilderOptions<T, DateOnly> ValidDate<T>(IRuleBuilder<T, DateOnly> rule) =>
        rule.NotEmpty();

    public static IRuleBuilderOptions<T, T> ValidRange<T>(
        IRuleBuilder<T, T> rule,
        Func<T, DateOnly> getFrom,
        Func<T, DateOnly> getTo) =>
        rule.Must(q =>
            {
                var f = getFrom(q); var t2 = getTo(q);
                return f <= t2 && (t2.DayNumber - f.DayNumber) <= MaxDays;
            })
            .WithMessage($"from debe ser ≤ to y el rango no puede superar {MaxDays} días.");
}

// ══════════════════════════════════════════════════════════════════════════════
// GET /api/v1/analytics/sales/summary
// ══════════════════════════════════════════════════════════════════════════════

public record GetSalesSummaryQuery(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    string Granularity) : IRequest<Result<SalesSummaryDto>>;

public sealed class GetSalesSummaryValidator : AbstractValidator<GetSalesSummaryQuery>
{
    public GetSalesSummaryValidator()
    {
        RuleFor(q => q.TenantId).NotEmpty();
        DateRangeRules.ValidDate(RuleFor(q => q.From));
        DateRangeRules.ValidDate(RuleFor(q => q.To));
        DateRangeRules.ValidRange(RuleFor(q => q), q => q.From, q => q.To);
        RuleFor(q => q.Granularity)
            .Must(g => g is "day" or "week" or "month")
            .WithMessage("granularity debe ser 'day', 'week' o 'month'.");
    }
}

public sealed class GetSalesSummaryHandler
    : IRequestHandler<GetSalesSummaryQuery, Result<SalesSummaryDto>>
{
    private readonly IAnalyticsRepository _repo;
    public GetSalesSummaryHandler(IAnalyticsRepository repo) => _repo = repo;

    public async Task<Result<SalesSummaryDto>> Handle(
        GetSalesSummaryQuery q, CancellationToken ct)
    {
        var dto = await _repo.GetSalesSummaryAsync(q.TenantId, q.From, q.To, q.Granularity, ct);
        return Result.Success(dto);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GET /api/v1/analytics/products/top
// ══════════════════════════════════════════════════════════════════════════════

public record GetTopProductsQuery(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    int Limit,
    string Sort) : IRequest<Result<TopProductsDto>>;

public sealed class GetTopProductsValidator : AbstractValidator<GetTopProductsQuery>
{
    public GetTopProductsValidator()
    {
        RuleFor(q => q.TenantId).NotEmpty();
        DateRangeRules.ValidDate(RuleFor(q => q.From));
        DateRangeRules.ValidDate(RuleFor(q => q.To));
        DateRangeRules.ValidRange(RuleFor(q => q), q => q.From, q => q.To);
        RuleFor(q => q.Limit).InclusiveBetween(1, 50);
        RuleFor(q => q.Sort)
            .Must(s => s is "quantity" or "revenue")
            .WithMessage("sort debe ser 'quantity' o 'revenue'.");
    }
}

public sealed class GetTopProductsHandler
    : IRequestHandler<GetTopProductsQuery, Result<TopProductsDto>>
{
    private readonly IAnalyticsRepository _repo;
    public GetTopProductsHandler(IAnalyticsRepository repo) => _repo = repo;

    public async Task<Result<TopProductsDto>> Handle(
        GetTopProductsQuery q, CancellationToken ct)
    {
        var dto = await _repo.GetTopProductsAsync(q.TenantId, q.From, q.To, q.Limit, q.Sort, ct);
        return Result.Success(dto);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GET /api/v1/analytics/sessions/averages
// ══════════════════════════════════════════════════════════════════════════════

public record GetSessionAveragesQuery(
    Guid TenantId,
    DateOnly From,
    DateOnly To) : IRequest<Result<SessionAveragesDto>>;

public sealed class GetSessionAveragesValidator : AbstractValidator<GetSessionAveragesQuery>
{
    public GetSessionAveragesValidator()
    {
        RuleFor(q => q.TenantId).NotEmpty();
        DateRangeRules.ValidDate(RuleFor(q => q.From));
        DateRangeRules.ValidDate(RuleFor(q => q.To));
        DateRangeRules.ValidRange(RuleFor(q => q), q => q.From, q => q.To);
    }
}

public sealed class GetSessionAveragesHandler
    : IRequestHandler<GetSessionAveragesQuery, Result<SessionAveragesDto>>
{
    private readonly IAnalyticsRepository _repo;
    public GetSessionAveragesHandler(IAnalyticsRepository repo) => _repo = repo;

    public async Task<Result<SessionAveragesDto>> Handle(
        GetSessionAveragesQuery q, CancellationToken ct)
    {
        var dto = await _repo.GetSessionAveragesAsync(q.TenantId, q.From, q.To, ct);
        return Result.Success(dto);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GET /api/v1/analytics/heatmap/hourly
// ══════════════════════════════════════════════════════════════════════════════

public record GetHourlyHeatmapQuery(
    Guid TenantId,
    DateOnly From,
    DateOnly To) : IRequest<Result<HourlyHeatmapDto>>;

public sealed class GetHourlyHeatmapValidator : AbstractValidator<GetHourlyHeatmapQuery>
{
    public GetHourlyHeatmapValidator()
    {
        RuleFor(q => q.TenantId).NotEmpty();
        DateRangeRules.ValidDate(RuleFor(q => q.From));
        DateRangeRules.ValidDate(RuleFor(q => q.To));
        DateRangeRules.ValidRange(RuleFor(q => q), q => q.From, q => q.To);
    }
}

public sealed class GetHourlyHeatmapHandler
    : IRequestHandler<GetHourlyHeatmapQuery, Result<HourlyHeatmapDto>>
{
    private readonly IAnalyticsRepository _repo;
    public GetHourlyHeatmapHandler(IAnalyticsRepository repo) => _repo = repo;

    public async Task<Result<HourlyHeatmapDto>> Handle(
        GetHourlyHeatmapQuery q, CancellationToken ct)
    {
        var dto = await _repo.GetHourlyHeatmapAsync(q.TenantId, q.From, q.To, ct);
        return Result.Success(dto);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// GET /api/v1/analytics/prep-time
// ══════════════════════════════════════════════════════════════════════════════

public record GetPrepTimeQuery(
    Guid TenantId,
    DateOnly From,
    DateOnly To) : IRequest<Result<PrepTimeDto>>;

public sealed class GetPrepTimeValidator : AbstractValidator<GetPrepTimeQuery>
{
    public GetPrepTimeValidator()
    {
        RuleFor(q => q.TenantId).NotEmpty();
        DateRangeRules.ValidDate(RuleFor(q => q.From));
        DateRangeRules.ValidDate(RuleFor(q => q.To));
        DateRangeRules.ValidRange(RuleFor(q => q), q => q.From, q => q.To);
    }
}

public sealed class GetPrepTimeHandler
    : IRequestHandler<GetPrepTimeQuery, Result<PrepTimeDto>>
{
    private readonly IAnalyticsRepository _repo;
    public GetPrepTimeHandler(IAnalyticsRepository repo) => _repo = repo;

    public async Task<Result<PrepTimeDto>> Handle(
        GetPrepTimeQuery q, CancellationToken ct)
    {
        var dto = await _repo.GetPrepTimeAsync(q.TenantId, q.From, q.To, ct);
        return Result.Success(dto);
    }
}
