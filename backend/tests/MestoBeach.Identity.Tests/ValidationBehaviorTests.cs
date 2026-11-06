using FluentAssertions;
using FluentValidation;
using MediatR;
using MestoBeach.Identity.Application.Common;
using MestoBeach.Identity.Application.Staff.Commands;
using MestoBeach.Shared.Behaviors;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace MestoBeach.Identity.Tests;

/// <summary>
/// Proves that FluentValidation validators are now enforced through the MediatR
/// <see cref="ValidationBehavior{TRequest,TResponse}"/>: bad input short-circuits the pipeline
/// (the handler never runs) and surfaces as a <see cref="ErrorType.Validation"/> failure (HTTP 400).
/// </summary>
public class ValidationBehaviorTests
{
    private static Task<TResponse> RunBehavior<TRequest, TResponse>(
        TRequest request,
        IValidator<TRequest> validator,
        RequestHandlerDelegate<TResponse> next)
        where TRequest : notnull
    {
        var behavior = new ValidationBehavior<TRequest, TResponse>(new[] { validator });
        return behavior.Handle(request, next, CancellationToken.None);
    }

    // RequestHandlerDelegate that fails the test if the pipeline does not short-circuit.
    private static RequestHandlerDelegate<TResponse> HandlerMustNotRun<TResponse>() =>
        () => throw new InvalidOperationException(
            "Handler should not run — validation must short-circuit the pipeline.");

    [Theory]
    [InlineData("12345678")]    // 8 chars but no upper/lower letters — the real-world bug
    [InlineData("short1A")]     // < 8 chars
    [InlineData("alllowercase1")] // no uppercase
    [InlineData("ALLUPPERCASE1")] // no lowercase
    [InlineData("NoDigitsHere")]  // no digit
    public async Task ActivateStaff_WeakPassword_ShortCircuitsWith400(string weakPassword)
    {
        var cmd = new ActivateStaffCommand("valid-activation-token", weakPassword, weakPassword);

        var result = await RunBehavior<ActivateStaffCommand, Result<StaffMemberDto>>(
            cmd, new ActivateStaffValidator(), HandlerMustNotRun<Result<StaffMemberDto>>());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task ActivateStaff_StrongPassword_PassesThroughToHandler()
    {
        var cmd = new ActivateStaffCommand("valid-activation-token", "Str0ngPass", "Str0ngPass");
        var handlerRan = false;

        RequestHandlerDelegate<Result<StaffMemberDto>> next = () =>
        {
            handlerRan = true;
            return Task.FromResult(Result.Failure<StaffMemberDto>(Error.NotFound("stub")));
        };

        await RunBehavior<ActivateStaffCommand, Result<StaffMemberDto>>(
            cmd, new ActivateStaffValidator(), next);

        handlerRan.Should().BeTrue("a valid command must reach the handler");
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("nouppercase1")]
    public async Task ChangeStaffPassword_WeakNewPassword_ShortCircuitsWith400(string weakNew)
    {
        // ChangeStaffPasswordCommand returns the non-generic Result — exercises the other
        // branch of the behavior's failure factory.
        var cmd = new ChangeStaffPasswordCommand(Guid.NewGuid(), "Curr3ntPass", weakNew, weakNew);

        var result = await RunBehavior<ChangeStaffPasswordCommand, Result>(
            cmd, new ChangeStaffPasswordValidator(), HandlerMustNotRun<Result>());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void AddModuleValidation_RegistersBehaviorAndValidators()
    {
        var services = new ServiceCollection();

        services.AddModuleValidation(typeof(ActivateStaffValidator).Assembly);

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IValidator<ActivateStaffCommand>>()
            .Should().ContainSingle().Which.Should().BeOfType<ActivateStaffValidator>();

        provider.GetServices<IPipelineBehavior<ActivateStaffCommand, Result<StaffMemberDto>>>()
            .Should().ContainSingle()
            .Which.Should().BeOfType<ValidationBehavior<ActivateStaffCommand, Result<StaffMemberDto>>>();
    }
}
