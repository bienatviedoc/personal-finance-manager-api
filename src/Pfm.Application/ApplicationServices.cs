using Microsoft.Extensions.DependencyInjection;

namespace Pfm.Application;

public static class ApplicationServices
{
    /// <summary>Registers the use cases of every module. Called by the API and the worker.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services) => services;
}
