using MediatR;
using MestoBeach.Analytics.Application.Queries;
using MestoBeach.API.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Analytics;

/// <summary>
/// GET /api/v1/analytics/sales/summary
/// GET /api/v1/analytics/products/top
/// GET /api/v1/analytics/sessions/averages
/// GET /api/v1/analytics/heatmap/hourly
/// GET /api/v1/analytics/prep-time
/// All endpoints require a valid Staff JWT (any role).
/// TenantId is always sourced from the JWT — never from a query param.
/// </summary>
[Route("api/v1/analytics")]
[Authorize(Policy = AuthorizationPolicies.StaffJwt)]
public sealed class AnalyticsController : ApiController
{
    public AnalyticsController(ISender mediator) : base(mediator) { }

    private Guid TenantId => (HttpContext.Items["tenant_id"] as Guid?) ?? Guid.Empty;

    // ── GET /api/v1/analytics/sales/summary ───────────────────────────────────

    /// <summary>
    /// Revenue, session and order counts grouped by time period (day/week/month).
    /// Query: from=YYYY-MM-DD &amp; to=YYYY-MM-DD &amp; granularity=day|week|month
    /// </summary>
    [HttpGet("sales/summary")]
    public async Task<IActionResult> GetSalesSummary(
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] string granularity = "day",
        CancellationToken ct = default)
    {
        if (!DateOnly.TryParse(from, out var fromDate) || !DateOnly.TryParse(to, out var toDate))
            return BadRequest(new { error = "from y to deben tener formato YYYY-MM-DD." });

        return ToResponse(await Mediator.Send(
            new GetSalesSummaryQuery(TenantId, fromDate, toDate, granularity), ct));
    }

    // ── GET /api/v1/analytics/products/top ───────────────────────────────────

    /// <summary>
    /// Top-selling products in the period.
    /// Query: from &amp; to &amp; limit=1-50 (default 10) &amp; sort=quantity|revenue (default quantity)
    /// </summary>
    [HttpGet("products/top")]
    public async Task<IActionResult> GetTopProducts(
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] int limit = 10,
        [FromQuery] string sort = "quantity",
        CancellationToken ct = default)
    {
        if (!DateOnly.TryParse(from, out var fromDate) || !DateOnly.TryParse(to, out var toDate))
            return BadRequest(new { error = "from y to deben tener formato YYYY-MM-DD." });

        return ToResponse(await Mediator.Send(
            new GetTopProductsQuery(TenantId, fromDate, toDate, limit, sort), ct));
    }

    // ── GET /api/v1/analytics/sessions/averages ───────────────────────────────

    /// <summary>
    /// Aggregate session metrics: ticket average, duration, orders/session, items/order.
    /// Query: from &amp; to
    /// </summary>
    [HttpGet("sessions/averages")]
    public async Task<IActionResult> GetSessionAverages(
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken ct = default)
    {
        if (!DateOnly.TryParse(from, out var fromDate) || !DateOnly.TryParse(to, out var toDate))
            return BadRequest(new { error = "from y to deben tener formato YYYY-MM-DD." });

        return ToResponse(await Mediator.Send(
            new GetSessionAveragesQuery(TenantId, fromDate, toDate), ct));
    }

    // ── GET /api/v1/analytics/heatmap/hourly ──────────────────────────────────

    /// <summary>
    /// Order count and revenue by hour-of-day (Bogotá local time) across the date range.
    /// Query: from &amp; to
    /// </summary>
    [HttpGet("heatmap/hourly")]
    public async Task<IActionResult> GetHourlyHeatmap(
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken ct = default)
    {
        if (!DateOnly.TryParse(from, out var fromDate) || !DateOnly.TryParse(to, out var toDate))
            return BadRequest(new { error = "from y to deben tener formato YYYY-MM-DD." });

        return ToResponse(await Mediator.Send(
            new GetHourlyHeatmapQuery(TenantId, fromDate, toDate), ct));
    }

    // ── GET /api/v1/analytics/prep-time ──────────────────────────────────────

    /// <summary>
    /// Average and P90 preparation time per KDS station (confirm → ready).
    /// Query: from &amp; to
    /// </summary>
    [HttpGet("prep-time")]
    public async Task<IActionResult> GetPrepTime(
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken ct = default)
    {
        if (!DateOnly.TryParse(from, out var fromDate) || !DateOnly.TryParse(to, out var toDate))
            return BadRequest(new { error = "from y to deben tener formato YYYY-MM-DD." });

        return ToResponse(await Mediator.Send(
            new GetPrepTimeQuery(TenantId, fromDate, toDate), ct));
    }
}
