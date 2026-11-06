using FluentValidation;
using MediatR;
using MestoBeach.Sessions.Domain.Repositories;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Sessions.Application.Commands;

// The menu's "identify" screen: a guest sets a display name; a logged-in customer also links
// their Mesto Beach account to the participant (customerAccountId comes from the customer JWT).

public record IdentifyParticipantDto(Guid ParticipantId, string? DisplayName, bool LinkedAccount);

public record IdentifyParticipantCommand(
    Guid SessionId,
    Guid ParticipantId,
    string DisplayName,
    Guid? CustomerAccountId) : IRequest<Result<IdentifyParticipantDto>>;

public sealed class IdentifyParticipantValidator : AbstractValidator<IdentifyParticipantCommand>
{
    public IdentifyParticipantValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.ParticipantId).NotEmpty();
        RuleFor(c => c.DisplayName)
            .NotEmpty().WithMessage("El nombre es requerido.")
            .MaximumLength(120);
    }
}

public sealed class IdentifyParticipantHandler
    : IRequestHandler<IdentifyParticipantCommand, Result<IdentifyParticipantDto>>
{
    private readonly ISessionRepository _sessions;
    public IdentifyParticipantHandler(ISessionRepository sessions) => _sessions = sessions;

    public async Task<Result<IdentifyParticipantDto>> Handle(IdentifyParticipantCommand cmd, CancellationToken ct)
    {
        var p = await _sessions.GetParticipantByIdAsync(cmd.ParticipantId, ct);
        if (p is null || p.SessionId != cmd.SessionId)
            return Result.Failure<IdentifyParticipantDto>(Error.NotFound("Participante no encontrado."));

        p.SetDisplayName(cmd.DisplayName);
        if (cmd.CustomerAccountId is { } cid && cid != Guid.Empty)
            p.AssociateCustomer(cid);

        await _sessions.SaveChangesAsync(ct);
        return Result.Success(new IdentifyParticipantDto(p.Id, p.DisplayName, p.CustomerAccountId.HasValue));
    }
}
