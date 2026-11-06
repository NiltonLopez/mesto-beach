using MediatR;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Entities;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Application.Auth.Customer.Commands;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record CustomerDto(
    Guid Id,
    string Email,
    string Name,
    int LoyaltyPoints,
    bool? IsNewAccount = null);

public record CustomerAuthResult(string Token, int ExpiresIn, CustomerDto Customer);

// ─── Register ─────────────────────────────────────────────────────────────────

public record RegisterCustomerCommand(
    string Email,
    string Password,
    string Name,
    string? Phone,
    DateOnly? BirthDate) : IRequest<Result<CustomerAuthResult>>;

public sealed class RegisterCustomerHandler
    : IRequestHandler<RegisterCustomerCommand, Result<CustomerAuthResult>>
{
    private readonly ICustomerAccountRepository _repo;
    private readonly IPasswordService _passwords;
    private readonly IJwtService _jwt;

    public RegisterCustomerHandler(
        ICustomerAccountRepository repo,
        IPasswordService passwords,
        IJwtService jwt)
    {
        _repo = repo;
        _passwords = passwords;
        _jwt = jwt;
    }

    public async Task<Result<CustomerAuthResult>> Handle(
        RegisterCustomerCommand request,
        CancellationToken ct)
    {
        var email = request.Email.ToLowerInvariant();
        var existing = await _repo.GetByEmailAsync(email, ct);

        if (existing is not null)
        {
            return existing.HasPassword
                ? Result.Failure<CustomerAuthResult>(
                    Error.Conflict("Ya existe una cuenta con este correo electrónico."))
                : Result.Failure<CustomerAuthResult>(
                    Error.Conflict("Esta cuenta usa Google para iniciar sesión."));
        }

        var hash = _passwords.Hash(request.Password);
        var account = new CustomerAccount(
            Guid.NewGuid(), email, request.Name, hash, request.Phone, request.BirthDate);

        await _repo.AddAsync(account, ct);

        var token = _jwt.GenerateCustomerToken(account.Id, out _);
        return Result.Success(new CustomerAuthResult(
            token, 14400,
            new CustomerDto(account.Id, account.Email, account.Name, 0)));
    }
}

// ─── Login ────────────────────────────────────────────────────────────────────

public record LoginCustomerCommand(
    string Email,
    string Password) : IRequest<Result<CustomerAuthResult>>;

public sealed class LoginCustomerHandler
    : IRequestHandler<LoginCustomerCommand, Result<CustomerAuthResult>>
{
    private readonly ICustomerAccountRepository _repo;
    private readonly IPasswordService _passwords;
    private readonly IJwtService _jwt;

    public LoginCustomerHandler(
        ICustomerAccountRepository repo,
        IPasswordService passwords,
        IJwtService jwt)
    {
        _repo = repo;
        _passwords = passwords;
        _jwt = jwt;
    }

    public async Task<Result<CustomerAuthResult>> Handle(
        LoginCustomerCommand request,
        CancellationToken ct)
    {
        var email = request.Email.ToLowerInvariant();
        var account = await _repo.GetByEmailAsync(email, ct);

        if (account is null)
            return Result.Failure<CustomerAuthResult>(Error.Unauthorized("Credenciales inválidas."));

        if (!account.HasPassword)
            return Result.Failure<CustomerAuthResult>(
                Error.Unauthorized("Esta cuenta usa Google para iniciar sesión."));

        if (!_passwords.Verify(request.Password, account.PasswordHash!))
            return Result.Failure<CustomerAuthResult>(Error.Unauthorized("Credenciales inválidas."));

        account.RecordLogin();
        await _repo.UpdateAsync(account, ct);

        var token = _jwt.GenerateCustomerToken(account.Id, out _);
        return Result.Success(new CustomerAuthResult(
            token, 14400,
            new CustomerDto(account.Id, account.Email, account.Name, account.LoyaltyPoints)));
    }
}

// ─── Google Auth ──────────────────────────────────────────────────────────────

public record GoogleAuthCustomerCommand(string IdToken) : IRequest<Result<CustomerAuthResult>>;

public sealed class GoogleAuthCustomerHandler
    : IRequestHandler<GoogleAuthCustomerCommand, Result<CustomerAuthResult>>
{
    private readonly ICustomerAccountRepository _repo;
    private readonly IGoogleAuthService _google;
    private readonly IJwtService _jwt;

    public GoogleAuthCustomerHandler(
        ICustomerAccountRepository repo,
        IGoogleAuthService google,
        IJwtService jwt)
    {
        _repo = repo;
        _google = google;
        _jwt = jwt;
    }

    public async Task<Result<CustomerAuthResult>> Handle(
        GoogleAuthCustomerCommand request,
        CancellationToken ct)
    {
        var (email, name, googleId) = await _google.ValidateAsync(request.IdToken, ct);
        email = email.ToLowerInvariant();

        // Check by Google ID first (fastest path)
        var account = await _repo.GetByGoogleIdAsync(googleId, ct);
        bool isNew = false;

        if (account is null)
        {
            // Check by email
            var byEmail = await _repo.GetByEmailAsync(email, ct);
            if (byEmail is not null && byEmail.HasPassword)
                return Result.Failure<CustomerAuthResult>(
                    Error.Conflict("Ya existe una cuenta con este correo. Inicia sesión con tu contraseña."));

            // Create new account
            account = CustomerAccount.CreateFromGoogle(Guid.NewGuid(), email, name, googleId);
            await _repo.AddAsync(account, ct);
            isNew = true;
        }
        else
        {
            account.RecordLogin();
            await _repo.UpdateAsync(account, ct);
        }

        var token = _jwt.GenerateCustomerToken(account.Id, out _);
        return Result.Success(new CustomerAuthResult(
            token, 14400,
            new CustomerDto(account.Id, account.Email, account.Name, account.LoyaltyPoints, isNew)));
    }
}
