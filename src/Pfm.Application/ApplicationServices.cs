using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pfm.Application.Accounts;

namespace Pfm.Application;

public static class ApplicationServices
{
    /// <summary>Registers the use cases of every module. Called by the API and the worker.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // One validator per command; each handler runs its own before doing anything.
        services.AddValidatorsFromAssembly(typeof(ApplicationServices).Assembly, ServiceLifetime.Singleton, includeInternalTypes: true);

        return services.AddAccountsModule();
    }
}
