using MediatR;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Application.Auth.Customer.Commands;

// ─── Password Reset Request ───────────────────────────────────────────────────

public record RequestCustomerPasswordResetCommand(string Email) : IRequest<Result>;

public sealed class RequestCustomerPasswordResetHandler
    : IRequestHandler<RequestCustomerPasswordResetCommand, Result>
{
    private readonly ICustomerAccountRepository _repo;
    private readonly IPasswordResetTokenService _tokens;
    private readonly IEmailNotificationService _email;

    public RequestCustomerPasswordResetHandler(
        ICustomerAccountRepository repo,
        IPasswordResetTokenService tokens,
        IEmailNotificationService email)
    {
        _repo = repo;
        _tokens = tokens;
        _email = email;
    }

    public async Task<Result> Handle(RequestCustomerPasswordResetCommand request, CancellationToken ct)
    {
        // Always return 200 — never reveal if email exists (anti-enumeration)
        var email = request.Email.ToLowerInvariant();
        var account = await _repo.GetByEmailAsync(email, ct);

        if (account is not null && account.HasPassword)
        {
            var token = _tokens.Generate();
            await _tokens.StoreCustomerAsync(token, account.Id, ct);
            // Email sent via SDD-007 (fire and forget in production)
            _ = _email.SendCustomerPasswordResetEmailAsync(
                account.Email, account.Name,
                $"${{FRONTEND_URL}}/reset-password/customer?token={token}", ct);
        }

        return Result.Success();
    }
}

// ─── Password Reset Confirm ───────────────────────────────────────────────────

public record ConfirmCustomerPasswordResetCommand(
    string Token,
    string NewPassword,
    string ConfirmPassword) : IRequest<Result>;

public sealed class ConfirmCustomerPasswordResetHandler
    : IRequestHandler<ConfirmCustomerPasswordResetCommand, Result>
{
    private readonly ICustomerAccountRepository _repo;
    private readonly IPasswordResetTokenService _tokens;
    private readonly IPasswordService _passwords;

    public ConfirmCustomerPasswordResetHandler(
        ICustomerAccountRepository repo,
        IPasswordResetTokenService tokens,
        IPasswordService passwords)
    {
        _repo = repo;
        _tokens = tokens;
        _passwords = passwords;
    }

    public async Task<Result> Handle(ConfirmCustomerPasswordResetCommand request, CancellationToken ct)
    {
        if (request.NewPassword != request.ConfirmPassword)
            return Result.Failure(Error.Validation("PASSWORD_MISMATCH", "Las contraseñas no coinciden."));

        var customerId = await _tokens.GetCustomerIdAsync(request.Token, ct);
        if (customerId is null)
            return Result.Failure(
                Error.Validation("INVALID_TOKEN", "El enlace de recuperación es inválido o ha expirado."));

        var account = await _repo.GetByIdAsync(customerId.Value, ct);
        if (account is null)
            return Result.Failure(
                Error.Validation("INVALID_TOKEN", "El enlace de recuperación es inválido o ha expirado."));

        account.UpdatePassword(_passwords.Hash(request.NewPassword));
        await _repo.UpdateAsync(account, ct);
        await _tokens.DeleteCustomerAsync(request.Token, ct);

        return Result.Success();
    }
}
