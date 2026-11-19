using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Identity;
using Pfm.Application.UseCases;

namespace Pfm.Application;

public static class ApplicationServices
{
    /// <summary>
    /// Registers the use cases of every module: each <see cref="CommandHandler{TCommand, TOutcome}"/>
    /// and each validator in this assembly. Called by the API and the worker.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationServices).Assembly;

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped);

        foreach (var handler in assembly.GetTypes().Where(IsCommandHandler))
        {
            services.AddScoped(handler);
        }

        // Queries: they read and change nothing, so they have no command or validator.
        services.AddScoped<GetProfileHandler>();

        return services;
    }

    private static bool IsCommandHandler(Type type)
    {
        if (type.IsAbstract)
        {
            return false;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(CommandHandler<,>))
            {
                return true;
            }
        }

        return false;
    }
}
