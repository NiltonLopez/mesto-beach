namespace MestoBeach.Analytics.Application.Common;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record SalesSeriesPoint(string Period, long RevenueCents, int SessionCount, int OrderCount);

public record SalesSummaryDto(
    Guid TenantId,
    string From,
    string To,
    string Granularity,
    long TotalRevenueCents,
    int TotalOrderCount,
    int TotalSessionCount,
    long AverageTicketCents,
    List<SalesSeriesPoint> Series);

public record TopProductItem(
    string CatalogItemId,
    string ItemNameSnapshot,
    int TotalQuantity,
    long TotalRevenueCents,
    int OrderCount);

public record TopProductsDto(
    Guid TenantId,
    string From,
    string To,
    string Sort,
    List<TopProductItem> Products);

public record SessionAveragesDto(
    Guid TenantId,
    string From,
    string To,
    int TotalSessions,
    int TotalOrders,
    int TotalPaidSessions,
    long AverageTicketCents,
    int AverageSessionDurationMin,
    double AverageOrdersPerSession,
    double AverageItemsPerOrder);

public record HourlyPoint(int Hour, int OrderCount, long RevenueCents);

public record HourlyHeatmapDto(
    Guid TenantId,
    string From,
    string To,
    string Timezone,
    List<HourlyPoint> Hours);

public record StationPrepTime(
    Guid? StationId,
    string StationName,
    int AvgPrepTimeMin,
    int P90PrepTimeMin,
    int SampleCount);

public record PrepTimeDto(
    Guid TenantId,
    string From,
    string To,
    List<StationPrepTime> Stations);

// ── Repository interface ──────────────────────────────────────────────────────

public interface IAnalyticsRepository
{
    Task<SalesSummaryDto> GetSalesSummaryAsync(
        Guid tenantId, DateOnly from, DateOnly to, string granularity, CancellationToken ct);

    Task<TopProductsDto> GetTopProductsAsync(
        Guid tenantId, DateOnly from, DateOnly to, int limit, string sort, CancellationToken ct);

    Task<SessionAveragesDto> GetSessionAveragesAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<HourlyHeatmapDto> GetHourlyHeatmapAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<PrepTimeDto> GetPrepTimeAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);
}
