using MestoBeach.Analytics.Application.Common;
using MestoBeach.Shared.Time;
using MestoBeach.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MestoBeach.Analytics.Infrastructure.Repositories;

internal sealed class AnalyticsRepository : IAnalyticsRepository
{
    private readonly AnalyticsDbContext _db;
    private readonly ILogger<AnalyticsRepository> _logger;

    public AnalyticsRepository(AnalyticsDbContext db, ILogger<AnalyticsRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── El día de negocio se corta a medianoche de Europe/Madrid ──────────────
    // (antes estaba cableado el desfase fijo de Colombia; España tiene horario
    //  de verano, así que la conversión la hace VenueTime, no una resta de horas)
    private static (DateTime From, DateTime ToExclusive) ToUtcRange(DateOnly from, DateOnly to)
        => VenueTime.ToUtcRange(from, to);

    // ═════════════════════════════════════════════════════════════════════════
    // GET /api/v1/analytics/sales/summary
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<SalesSummaryDto> GetSalesSummaryAsync(
        Guid tenantId, DateOnly from, DateOnly to, string granularity, CancellationToken ct)
    {
        var (fromUtc, toExclusive) = ToUtcRange(from, to);

        var trunc = granularity switch
        {
            "week"  => "week",
            "month" => "month",
            _       => "day"
        };

        // Dynamic DATE_TRUNC granularity is safe — validated as enum in Application layer.
        var sql = $"""
            WITH session_buckets AS (
              SELECT DATE_TRUNC('{trunc}', created_at AT TIME ZONE 'Europe/Madrid') AS bucket,
                     COUNT(DISTINCT id)::int AS session_count
              FROM sessions
              WHERE tenant_id = @tenantId
                AND created_at >= @fromUtc AND created_at < @toExclusive
              GROUP BY 1
            ),
            order_buckets AS (
              SELECT DATE_TRUNC('{trunc}', created_at AT TIME ZONE 'Europe/Madrid') AS bucket,
                     COUNT(*)::int AS order_count
              FROM orders
              WHERE tenant_id = @tenantId AND status NOT IN ('Cancelled')
                AND created_at >= @fromUtc AND created_at < @toExclusive
              GROUP BY 1
            ),
            payment_buckets AS (
              SELECT DATE_TRUNC('{trunc}', COALESCE(completed_at, created_at) AT TIME ZONE 'Europe/Madrid') AS bucket,
                     COALESCE(SUM(amount_cents), 0)::bigint AS revenue_cents
              FROM payments
              WHERE tenant_id = @tenantId AND status = 'Completed'
                AND COALESCE(completed_at, created_at) >= @fromUtc
                AND COALESCE(completed_at, created_at) < @toExclusive
              GROUP BY 1
            ),
            all_buckets AS (
              SELECT bucket FROM session_buckets
              UNION SELECT bucket FROM order_buckets
              UNION SELECT bucket FROM payment_buckets
            )
            SELECT ab.bucket                               AS "Period",
                   COALESCE(pb.revenue_cents, 0)::bigint    AS "RevenueCents",
                   COALESCE(sb.session_count, 0)::int     AS "SessionCount",
                   COALESCE(ob.order_count, 0)::int       AS "OrderCount"
            FROM all_buckets ab
            LEFT JOIN session_buckets sb ON ab.bucket = sb.bucket
            LEFT JOIN order_buckets   ob ON ab.bucket = ob.bucket
            LEFT JOIN payment_buckets pb ON ab.bucket = pb.bucket
            ORDER BY ab.bucket
            """;

        var series = await _db.Database
            .SqlQueryRaw<SeriesRaw>(sql,
                new NpgsqlParameter("tenantId",    tenantId),
                new NpgsqlParameter("fromUtc",     fromUtc),
                new NpgsqlParameter("toExclusive", toExclusive))
            .ToListAsync(ct);

        var totalRevenue  = series.Sum(s => s.RevenueCents);
        var totalSessions = series.Sum(s => s.SessionCount);
        var totalOrders   = series.Sum(s => s.OrderCount);
        var avgTicket     = totalSessions > 0 ? totalRevenue / totalSessions : 0L;

        var seriesPts = series
            .Select(s => new SalesSeriesPoint(
                s.Period.ToString("yyyy-MM-dd"),
                s.RevenueCents,
                s.SessionCount,
                s.OrderCount))
            .ToList();

        return new SalesSummaryDto(
            tenantId,
            from.ToString("yyyy-MM-dd"),
            to.ToString("yyyy-MM-dd"),
            granularity,
            totalRevenue,
            totalOrders,
            totalSessions,
            avgTicket,
            seriesPts);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GET /api/v1/analytics/products/top
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<TopProductsDto> GetTopProductsAsync(
        Guid tenantId, DateOnly from, DateOnly to, int limit, string sort, CancellationToken ct)
    {
        var (fromUtc, toExclusive) = ToUtcRange(from, to);

        // sort is validated as "quantity"|"revenue" in Application layer.
        var orderBy = sort == "revenue"
            ? "\"TotalRevenueCents\" DESC"
            : "\"TotalQuantity\" DESC";

        var sql = $"""
            SELECT oi.catalog_item_id                                                     AS "CatalogItemId",
                   oi.item_name_snapshot                                                  AS "ItemNameSnapshot",
                   COALESCE(SUM(oi.quantity), 0)::int                                     AS "TotalQuantity",
                   COALESCE(SUM(oi.item_price_snapshot_cents::bigint * oi.quantity), 0)     AS "TotalRevenueCents",
                   COUNT(DISTINCT oi.order_id)::int                                       AS "OrderCount"
            FROM order_items oi
            JOIN orders o ON o.id = oi.order_id
            WHERE o.tenant_id = @tenantId
              AND o.status NOT IN ('Cancelled')
              AND o.created_at >= @fromUtc AND o.created_at < @toExclusive
            GROUP BY oi.catalog_item_id, oi.item_name_snapshot
            ORDER BY {orderBy}
            LIMIT @limit
            """;

        var rows = await _db.Database
            .SqlQueryRaw<TopProductRaw>(sql,
                new NpgsqlParameter("tenantId",    tenantId),
                new NpgsqlParameter("fromUtc",     fromUtc),
                new NpgsqlParameter("toExclusive", toExclusive),
                new NpgsqlParameter("limit",       limit))
            .ToListAsync(ct);

        var products = rows
            .Select(r => new TopProductItem(
                r.CatalogItemId,
                r.ItemNameSnapshot,
                r.TotalQuantity,
                r.TotalRevenueCents,
                r.OrderCount))
            .ToList();

        return new TopProductsDto(
            tenantId,
            from.ToString("yyyy-MM-dd"),
            to.ToString("yyyy-MM-dd"),
            sort,
            products);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GET /api/v1/analytics/sessions/averages
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<SessionAveragesDto> GetSessionAveragesAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (fromUtc, toExclusive) = ToUtcRange(from, to);

        // One correlated-subquery pass for all scalars except items/order (separate query).
        var scalarSql = """
            SELECT
              (SELECT COUNT(*)::int
               FROM sessions
               WHERE tenant_id = @tenantId
                 AND created_at >= @fromUtc AND created_at < @toExclusive
              ) AS "TotalSessions",

              (SELECT COUNT(*)::int
               FROM orders
               WHERE tenant_id = @tenantId AND status NOT IN ('Cancelled')
                 AND created_at >= @fromUtc AND created_at < @toExclusive
              ) AS "TotalOrders",

              (SELECT COUNT(DISTINCT session_id)::int
               FROM payments
               WHERE tenant_id = @tenantId AND status = 'Completed'
                 AND created_at >= @fromUtc AND created_at < @toExclusive
              ) AS "TotalPaidSessions",

              (SELECT COALESCE(SUM(amount_cents), 0)::bigint
               FROM payments
               WHERE tenant_id = @tenantId AND status = 'Completed'
                 AND created_at >= @fromUtc AND created_at < @toExclusive
              ) AS "TotalRevenueCents",

              (SELECT COALESCE(
                 AVG(EXTRACT(EPOCH FROM (closed_at - created_at)) / 60.0)::int, 0)
               FROM sessions
               WHERE tenant_id = @tenantId
                 AND created_at >= @fromUtc AND created_at < @toExclusive
                 AND closed_at IS NOT NULL
              ) AS "AvgDurationMin"
            """;

        var scalars = await _db.Database
            .SqlQueryRaw<SessionScalarsRaw>(scalarSql,
                new NpgsqlParameter("tenantId",    tenantId),
                new NpgsqlParameter("fromUtc",     fromUtc),
                new NpgsqlParameter("toExclusive", toExclusive))
            .FirstOrDefaultAsync(ct) ?? new SessionScalarsRaw();

        // Average items per order (aggregate sub-query, separate round trip).
        var itemsSql = """
            SELECT COALESCE(AVG(item_sum), 0.0) AS "Value"
            FROM (
              SELECT oi.order_id, SUM(oi.quantity)::float AS item_sum
              FROM order_items oi
              JOIN orders o ON o.id = oi.order_id
              WHERE o.tenant_id = @tenantId
                AND o.status NOT IN ('Cancelled')
                AND o.created_at >= @fromUtc AND o.created_at < @toExclusive
              GROUP BY oi.order_id
            ) sub
            """;

        var itemsResult = await _db.Database
            .SqlQueryRaw<ScalarDouble>(itemsSql,
                new NpgsqlParameter("tenantId",    tenantId),
                new NpgsqlParameter("fromUtc",     fromUtc),
                new NpgsqlParameter("toExclusive", toExclusive))
            .FirstOrDefaultAsync(ct);

        var avgItemsPerOrder   = Math.Round(itemsResult?.Value ?? 0.0, 2);
        var avgOrdersPerSession = scalars.TotalSessions > 0
            ? Math.Round((double)scalars.TotalOrders / scalars.TotalSessions, 2)
            : 0.0;
        var avgTicket = scalars.TotalPaidSessions > 0
            ? scalars.TotalRevenueCents / scalars.TotalPaidSessions
            : 0L;

        return new SessionAveragesDto(
            tenantId,
            from.ToString("yyyy-MM-dd"),
            to.ToString("yyyy-MM-dd"),
            scalars.TotalSessions,
            scalars.TotalOrders,
            scalars.TotalPaidSessions,
            avgTicket,
            scalars.AvgDurationMin,
            avgOrdersPerSession,
            avgItemsPerOrder);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GET /api/v1/analytics/heatmap/hourly
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<HourlyHeatmapDto> GetHourlyHeatmapAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (fromUtc, toExclusive) = ToUtcRange(from, to);

        var sql = """
            SELECT EXTRACT(HOUR FROM o.created_at AT TIME ZONE 'Europe/Madrid')::int        AS "Hour",
                   COUNT(DISTINCT o.id)::int                                         AS "OrderCount",
                   COALESCE(SUM(oi.item_price_snapshot_cents::bigint * oi.quantity), 0) AS "RevenueCents"
            FROM orders o
            LEFT JOIN order_items oi ON oi.order_id = o.id
            WHERE o.tenant_id = @tenantId
              AND o.status NOT IN ('Cancelled')
              AND o.created_at >= @fromUtc AND o.created_at < @toExclusive
            GROUP BY 1
            ORDER BY 1
            """;

        var rows = await _db.Database
            .SqlQueryRaw<HeatmapRaw>(sql,
                new NpgsqlParameter("tenantId",    tenantId),
                new NpgsqlParameter("fromUtc",     fromUtc),
                new NpgsqlParameter("toExclusive", toExclusive))
            .ToListAsync(ct);

        var hours = rows
            .Select(r => new HourlyPoint(r.Hour, r.OrderCount, r.RevenueCents))
            .ToList();

        return new HourlyHeatmapDto(
            tenantId,
            from.ToString("yyyy-MM-dd"),
            to.ToString("yyyy-MM-dd"),
            "America/Bogota",
            hours);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // GET /api/v1/analytics/prep-time
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<PrepTimeDto> GetPrepTimeAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (fromUtc, toExclusive) = ToUtcRange(from, to);

        var sql = """
            SELECT o.station_id                                                         AS "StationId",
                   COALESCE(ks.name, 'General')                                         AS "StationName",
                   AVG(EXTRACT(EPOCH FROM (o.ready_at - o.confirmed_at)) / 60.0)::int  AS "AvgPrepTimeMin",
                   (PERCENTILE_DISC(0.9) WITHIN GROUP (
                      ORDER BY EXTRACT(EPOCH FROM (o.ready_at - o.confirmed_at))
                   ) / 60.0)::int                                                       AS "P90PrepTimeMin",
                   COUNT(*)::int                                                        AS "SampleCount"
            FROM orders o
            LEFT JOIN kds_stations ks ON ks.id = o.station_id
            WHERE o.tenant_id = @tenantId
              AND o.ready_at IS NOT NULL
              AND o.confirmed_at IS NOT NULL
              AND o.ready_at >= @fromUtc AND o.ready_at < @toExclusive
            GROUP BY o.station_id, ks.name
            ORDER BY "AvgPrepTimeMin" DESC
            """;

        var rows = await _db.Database
            .SqlQueryRaw<PrepTimeRaw>(sql,
                new NpgsqlParameter("tenantId",    tenantId),
                new NpgsqlParameter("fromUtc",     fromUtc),
                new NpgsqlParameter("toExclusive", toExclusive))
            .ToListAsync(ct);

        var stations = rows
            .Select(r => new StationPrepTime(
                r.StationId,
                r.StationName,
                r.AvgPrepTimeMin,
                r.P90PrepTimeMin,
                r.SampleCount))
            .ToList();

        return new PrepTimeDto(
            tenantId,
            from.ToString("yyyy-MM-dd"),
            to.ToString("yyyy-MM-dd"),
            stations);
    }
}

// ── Internal POCOs for SqlQueryRaw result mapping ─────────────────────────────

internal sealed class SeriesRaw
{
    public DateTime Period       { get; set; }
    public long     RevenueCents   { get; set; }
    public int      SessionCount { get; set; }
    public int      OrderCount   { get; set; }
}

internal sealed class TopProductRaw
{
    public string CatalogItemId    { get; set; } = string.Empty;
    public string ItemNameSnapshot { get; set; } = string.Empty;
    public int    TotalQuantity    { get; set; }
    public long   TotalRevenueCents  { get; set; }
    public int    OrderCount       { get; set; }
}

internal sealed class SessionScalarsRaw
{
    public int  TotalSessions     { get; set; }
    public int  TotalOrders       { get; set; }
    public int  TotalPaidSessions { get; set; }
    public long TotalRevenueCents   { get; set; }
    public int  AvgDurationMin    { get; set; }
}

internal sealed class HeatmapRaw
{
    public int  Hour       { get; set; }
    public int  OrderCount { get; set; }
    public long RevenueCents { get; set; }
}

internal sealed class PrepTimeRaw
{
    public Guid?  StationId      { get; set; }
    public string StationName    { get; set; } = "General";
    public int    AvgPrepTimeMin { get; set; }
    public int    P90PrepTimeMin { get; set; }
    public int    SampleCount    { get; set; }
}

internal sealed class ScalarDouble
{
    public double Value { get; set; }
}
