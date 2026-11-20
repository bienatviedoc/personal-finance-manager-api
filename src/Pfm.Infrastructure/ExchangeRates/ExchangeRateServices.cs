using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pfm.Application.ExchangeRates;

namespace Pfm.Infrastructure.ExchangeRates;

internal static class ExchangeRateServices
{
    /// <summary>Where rates are stored, and the external source they are fetched from.</summary>
    public static IServiceCollection AddExchangeRates(this IServiceCollection services)
    {
        services.AddOptions<ExchangeRateOptions>().BindConfiguration(ExchangeRateOptions.Section);
        services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
        services.AddHttpClient<IExchangeRateSource, FrankfurterExchangeRateSource>((provider, http) =>
        {
            http.BaseAddress = new Uri(provider.GetRequiredService<IOptions<ExchangeRateOptions>>().Value.SourceUrl);
            http.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
