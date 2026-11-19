using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Accounts.Institutions;

namespace Pfm.Application.Accounts;

public static class AccountsServices
{
    /// <summary>The Accounts module's use cases: institutions and, later, accounts.</summary>
    public static IServiceCollection AddAccountsModule(this IServiceCollection services) =>
        services
            .AddScoped<ListInstitutionsHandler>()
            .AddScoped<GetInstitutionHandler>()
            .AddScoped<CreateInstitutionHandler>()
            .AddScoped<EditInstitutionHandler>()
            .AddScoped<ArchiveInstitutionHandler>();
}
