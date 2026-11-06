using System.Reflection;
using FluentValidation;
using MediatR;
using MestoBeach.Shared.Errors;
using MestoBeach.Shared.Primitives;

namespace MestoBeach.Shared.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs every <see cref="IValidator{T}"/> registered for the
/// incoming request. On failure it short-circuits the pipeline and returns a
/// <see cref="Result"/>/<see cref="Result{T}"/> failure carrying an <see cref="ErrorType.Validation"/>
/// error (mapped to HTTP 400 by the API layer) instead of throwing — preserving the existing
/// Result-based error handling.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count == 0)
            return await next();

        var error = Error.Validation(
            "VALIDATION_ERROR",
            string.Join(" ", failures.Select(f => f.ErrorMessage)));

        // Map the failure onto the Result-shaped response so the API maps it to 400 ProblemDetails.
        if (FailureFactory is not null)
            return FailureFactory(error);

        // Request does not return Result/Result<T> — fall back to the FluentValidation default
        // (fail-fast exception) so the failure is never silently swallowed.
        throw new ValidationException(failures);
    }

    // Built once per closed generic type (TResponse is fixed), so the reflection cost is paid once.
    private static readonly Func<Error, TResponse>? FailureFactory = BuildFailureFactory();

    private static Func<Error, TResponse>? BuildFailureFactory()
    {
        var responseType = typeof(TResponse);

        if (responseType == typeof(Result))
            return error => (TResponse)(object)Result.Failure(error);

        if (responseType.IsGenericType &&
            responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = responseType.GetGenericArguments()[0];
            var failureMethod = typeof(Result).GetMethods()
                .Single(m => m is { Name: nameof(Result.Failure), IsGenericMethodDefinition: true })
                .MakeGenericMethod(valueType);

            return error => (TResponse)failureMethod.Invoke(null, new object[] { error })!;
        }

        return null;
    }
}
