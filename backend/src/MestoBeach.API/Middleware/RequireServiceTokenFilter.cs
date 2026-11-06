using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MestoBeach.API.Middleware;

/// <summary>
/// Action filter that validates the X-Service-Token header for internal endpoints.
/// Token value is read from configuration key "InternalServiceToken"
/// or environment variable INTERNAL_SERVICE_TOKEN.
/// </summary>
public sealed class RequireServiceTokenFilter : IAsyncActionFilter
{
    private readonly IConfiguration _config;
    private readonly ILogger<RequireServiceTokenFilter> _logger;

    public RequireServiceTokenFilter(IConfiguration config, ILogger<RequireServiceTokenFilter> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var expected = _config["InternalServiceToken"]
            ?? Environment.GetEnvironmentVariable("INTERNAL_SERVICE_TOKEN");

        var provided = ctx.HttpContext.Request.Headers["X-Service-Token"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(provided) || provided != expected)
        {
            _logger.LogWarning("RequireServiceToken: token inválido o ausente for {Path}",
                ctx.HttpContext.Request.Path);
            ctx.Result = new UnauthorizedObjectResult(
                new { error = "X-Service-Token ausente o inválido." });
            return;
        }

        await next();
    }
}
