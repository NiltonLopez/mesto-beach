using MediatR;
using MestoBeach.Identity.Application.Auth.Customer.Commands;
using MestoBeach.Identity.Application.Auth.Customer.Queries;
using MestoBeach.Shared.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MestoBeach.API.Controllers.Identity;

/// <summary>
/// Customer account endpoints: register, login, google, profile.
/// Base path: /api/v1/identity/customers
/// </summary>
[Route("api/v1/identity/customers")]
public sealed class CustomersController : ApiController
{
    private readonly ICurrentUser _currentUser;

    public CustomersController(ISender mediator, ICurrentUser currentUser)
        : base(mediator)
    {
        _currentUser = currentUser;
    }

    // POST /api/v1/identity/customers/register
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CustomerAuthResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterCustomerRequest req,
        CancellationToken ct)
    {
        var result = await Mediator.Send(
            new RegisterCustomerCommand(req.Email, req.Password, req.Name, req.Phone, req.BirthDate), ct);

        return result.IsSuccess
            ? Created(string.Empty, new
            {
                token = result.Value.Token,
                tokenType = "Bearer",
                expiresIn = result.Value.ExpiresIn,
                customer = result.Value.Customer
            })
            : ToResponse(result);
    }

    // POST /api/v1/identity/customers/login
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CustomerAuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginCustomerRequest req,
        CancellationToken ct)
    {
        var result = await Mediator.Send(
            new LoginCustomerCommand(req.Email, req.Password), ct);

        return result.IsSuccess
            ? Ok(new
            {
                token = result.Value.Token,
                tokenType = "Bearer",
                expiresIn = result.Value.ExpiresIn,
                customer = result.Value.Customer
            })
            : ToResponse(result);
    }

    // POST /api/v1/identity/customers/google
    [HttpPost("google")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CustomerAuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Google(
        [FromBody] GoogleAuthRequest req,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new GoogleAuthCustomerCommand(req.IdToken), ct);

        return result.IsSuccess
            ? Ok(new
            {
                token = result.Value.Token,
                tokenType = "Bearer",
                expiresIn = result.Value.ExpiresIn,
                customer = result.Value.Customer
            })
            : ToResponse(result);
    }

    // GET /api/v1/identity/customers/me
    [HttpGet("me")]
    [Authorize(Policy = "CustomerJwt")]
    [ProducesResponseType(typeof(CustomerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetCustomerProfileQuery(_currentUser.UserId), ct);
        return ToResponse(result);
    }

    // PATCH /api/v1/identity/customers/me
    [HttpPatch("me")]
    [Authorize(Policy = "CustomerJwt")]
    [ProducesResponseType(typeof(CustomerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateMe(
        [FromBody] UpdateCustomerProfileRequest req,
        CancellationToken ct)
    {
        // Validate at least one field is present
        if (req.Name is null && !req.PhoneProvided && req.BirthDate is null)
            return BadRequest(new ProblemDetails
            {
                Title = "EMPTY_BODY",
                Detail = "Debe enviar al menos un campo.",
                Status = 400
            });

        var result = await Mediator.Send(new UpdateCustomerProfileCommand(
            _currentUser.UserId,
            req.Name,
            req.Phone,
            req.BirthDate,
            req.PhoneProvided,
            req.BirthDate is not null), ct);

        return ToResponse(result);
    }

    // POST /api/v1/identity/customers/password-reset/request
    [HttpPost("password-reset/request")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestPasswordReset(
        [FromBody] PasswordResetRequestBody req,
        CancellationToken ct)
    {
        await Mediator.Send(new RequestCustomerPasswordResetCommand(req.Email), ct);
        return Ok(new { message = "Si el correo está registrado, recibirás las instrucciones en breve." });
    }

    // POST /api/v1/identity/customers/password-reset/confirm
    [HttpPost("password-reset/confirm")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmPasswordReset(
        [FromBody] PasswordResetConfirmBody req,
        CancellationToken ct)
    {
        var result = await Mediator.Send(
            new ConfirmCustomerPasswordResetCommand(req.Token, req.NewPassword, req.ConfirmPassword), ct);

        return result.IsSuccess
            ? Ok(new { message = "Contraseña actualizada exitosamente." })
            : ToResponse(result);
    }
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public record RegisterCustomerRequest(
    string Email,
    string Password,
    string Name,
    string? Phone,
    DateOnly? BirthDate);

public record LoginCustomerRequest(string Email, string Password);

public record GoogleAuthRequest(string IdToken);

public class UpdateCustomerProfileRequest
{
    public string? Name { get; init; }
    public string? Phone { get; init; }
    public DateOnly? BirthDate { get; init; }

    // True when "phone" key was explicitly included in the JSON body (even if null)
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PhoneProvided { get; set; }
}

public record PasswordResetRequestBody(string Email);

public record PasswordResetConfirmBody(
    string Token,
    string NewPassword,
    string ConfirmPassword);
