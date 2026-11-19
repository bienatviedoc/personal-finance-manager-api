using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Identity;

namespace Pfm.Application;

public static class ApplicationServices
{
    /// <summary>Registers the use cases of every module. Called by the API and the worker.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GetProfileHandler>();
        return services;
    }
}
