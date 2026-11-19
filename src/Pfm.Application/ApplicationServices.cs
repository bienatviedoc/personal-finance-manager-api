using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Transactions.Categories;

namespace Pfm.Application;

public static class ApplicationServices
{
    /// <summary>Registers the use cases of every module. Called by the API and the worker.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ApplicationServices).Assembly, includeInternalTypes: false);

        services.AddScoped<ListCategoriesHandler>();
        services.AddScoped<CreateCategoryHandler>();
        services.AddScoped<UpdateCategoryHandler>();
        services.AddScoped<ArchiveCategoryHandler>();
        services.AddScoped<CreateDefaultCategoriesHandler>();

        return services;
    }
}
