using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MestoBeach.Shared.Behaviors;

public static class ValidationRegistration
{
    /// <summary>
    /// Registers the FluentValidation validators found in <paramref name="applicationAssembly"/> and
    /// wires the shared <see cref="ValidationBehavior{TRequest,TResponse}"/> into the MediatR pipeline.
    /// Call once per module (from its <c>Add&lt;Module&gt;Module</c>). The behavior registration is
    /// idempotent (<see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection,ServiceDescriptor)"/>),
    /// so it runs a single time per request regardless of how many modules call this.
    /// </summary>
    public static IServiceCollection AddModuleValidation(
        this IServiceCollection services,
        Assembly applicationAssembly)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)));

        services.AddValidatorsFromAssembly(applicationAssembly, includeInternalTypes: true);

        return services;
    }
}
