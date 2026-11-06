using MediatR;
using MestoBeach.Identity.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Identity.Application.Auth.Customer.Queries;

public record CustomerProfileDto(
    Guid Id,
    string Email,
    string Name,
    string? Phone,
    DateOnly? BirthDate,
    int LoyaltyPoints,
    bool HasPassword,
    bool HasGoogle,
    DateTime CreatedAt,
    DateTime LastLogin);

public record GetCustomerProfileQuery(Guid CustomerId) : IRequest<Result<CustomerProfileDto>>;

public sealed class GetCustomerProfileHandler
    : IRequestHandler<GetCustomerProfileQuery, Result<CustomerProfileDto>>
{
    private readonly ICustomerAccountRepository _repo;

    public GetCustomerProfileHandler(ICustomerAccountRepository repo) => _repo = repo;

    public async Task<Result<CustomerProfileDto>> Handle(
        GetCustomerProfileQuery request,
        CancellationToken ct)
    {
        var account = await _repo.GetByIdAsync(request.CustomerId, ct);
        if (account is null)
            return Result.Failure<CustomerProfileDto>(Error.NotFound("Cuenta no encontrada."));

        return Result.Success(new CustomerProfileDto(
            account.Id,
            account.Email,
            account.Name,
            account.Phone,
            account.BirthDate,
            account.LoyaltyPoints,
            account.HasPassword,
            account.HasGoogle,
            account.CreatedAt,
            account.LastLogin));
    }
}

// ─── PATCH Profile ────────────────────────────────────────────────────────────

public record UpdateCustomerProfileCommand(
    Guid CustomerId,
    string? Name,
    string? Phone,   // null = remove; omitted = don't change
    DateOnly? BirthDate,
    bool PhoneProvided,
    bool BirthDateProvided) : IRequest<Result<CustomerProfileDto>>;

public sealed class UpdateCustomerProfileHandler
    : IRequestHandler<UpdateCustomerProfileCommand, Result<CustomerProfileDto>>
{
    private readonly ICustomerAccountRepository _repo;
    private readonly IRequestHandler<GetCustomerProfileQuery, Result<CustomerProfileDto>> _profileQuery;

    public UpdateCustomerProfileHandler(
        ICustomerAccountRepository repo,
        IRequestHandler<GetCustomerProfileQuery, Result<CustomerProfileDto>> profileQuery)
    {
        _repo = repo;
        _profileQuery = profileQuery;
    }

    public async Task<Result<CustomerProfileDto>> Handle(
        UpdateCustomerProfileCommand request,
        CancellationToken ct)
    {
        var account = await _repo.GetByIdAsync(request.CustomerId, ct);
        if (account is null)
            return Result.Failure<CustomerProfileDto>(Error.NotFound("Cuenta no encontrada."));

        // Build partial update — only update fields that were explicitly provided
        string? phone = request.PhoneProvided ? request.Phone : "SKIP";
        DateOnly? birthDate = request.BirthDateProvided ? request.BirthDate : null;

        account.UpdateProfile(
            request.Name,
            phone == "SKIP" ? null : phone,
            request.BirthDateProvided ? birthDate : null);

        await _repo.UpdateAsync(account, ct);

        return await _profileQuery.Handle(new GetCustomerProfileQuery(request.CustomerId), ct);
    }
}
