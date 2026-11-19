using System.Text.Json;
using System.Text.Json.Serialization;
using Pfm.Domain;
using Pfm.Domain.Identity;

namespace Pfm.Api;

public static class JsonSetup
{
    /// <summary>
    /// Enums travel as their names: a currency as its ISO 4217 code (<c>"VND"</c>), a locale as its
    /// language tag (<c>"vi"</c>). The OpenAPI document lists the values, so the SPA's types do too.
    /// </summary>
    public static IServiceCollection AddPfmJson(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<Currency>(allowIntegerValues: false));
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<Locale>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        });
}
