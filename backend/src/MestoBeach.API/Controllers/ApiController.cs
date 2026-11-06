using MediatR;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers;

[ApiController]
[Route("api/v1")]
public abstract class ApiController : ControllerBase
{
    protected readonly ISender Mediator;

    protected ApiController(ISender mediator) => Mediator = mediator;

    /// <summary>
    /// Converts a Result to an appropriate HTTP response.
    /// Maps ErrorType to HTTP status codes per Mesto Beach conventions.
    /// </summary>
    protected IActionResult ToResponse(Result result)
    {
        if (result.IsSuccess) return NoContent();
        return Problem(result.Error);
    }

    protected IActionResult ToResponse<T>(Result<T> result, int successStatus = 200)
    {
        if (result.IsSuccess)
            return successStatus == 201 ? Created(string.Empty, result.Value) : Ok(result.Value);

        return Problem(result.Error);
    }

    private ObjectResult Problem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.NotFound       => StatusCodes.Status404NotFound,
            ErrorType.Validation     => StatusCodes.Status400BadRequest,
            ErrorType.Conflict       => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized   => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden      => StatusCodes.Status403Forbidden,
            ErrorType.TooManyRequests=> StatusCodes.Status429TooManyRequests,
            ErrorType.Locked         => StatusCodes.Status423Locked,
            ErrorType.Gone           => StatusCodes.Status410Gone,
            ErrorType.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
            ErrorType.ServiceUnavailable  => StatusCodes.Status503ServiceUnavailable,
            _                        => StatusCodes.Status500InternalServerError
        };

        var problem = new ProblemDetails
        {
            Title = error.Code,
            Detail = error.Message,
            Status = statusCode
        };

        // Surface structured fields (e.g. plan_limit_exceeded → limit/current/suggestedPlan).
        if (error.Extensions is { Count: > 0 })
            foreach (var (key, value) in error.Extensions)
                problem.Extensions[key] = value;

        return StatusCode(statusCode, problem);
    }
}
