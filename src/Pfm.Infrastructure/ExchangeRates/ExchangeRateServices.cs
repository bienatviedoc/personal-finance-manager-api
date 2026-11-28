using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pfm.Application.ExchangeRates;

namespace Pfm.Infrastructure.ExchangeRates;

internal static class ExchangeRateServices
{
    /// <summary>Where rates are stored, and the external source they are fetched from.</summary>
    public static IServiceCollection AddExchangeRates(this IServiceCollection services)
    {
        services.AddOptions<ExchangeRateSourceOptions>().BindConfiguration(ExchangeRateSourceOptions.Section)
            .Validate(
                options => Uri.TryCreate(options.SourceUrl, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp),
                $"{ExchangeRateSourceOptions.Section}:SourceUrl must be an absolute http or https URL.")
            .ValidateOnStart();
        services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
        services.AddHttpClient<IExchangeRateSource, FrankfurterExchangeRateSource>((provider, http) =>
        {
            http.BaseAddress = new Uri(provider.GetRequiredService<IOptions<ExchangeRateSourceOptions>>().Value.SourceUrl);
            http.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
