using System.Text.Json;
using System.Text.Json.Serialization;
using Pfm.Domain;

namespace Pfm.Api;

public static class JsonSetup
{
    /// <summary>
    /// Enums travel as names, never as numbers: in camelCase (<c>"income"</c>; a locale as its language tag,
    /// <c>"vi"</c>), except a currency, which is its ISO 4217 code (<c>"VND"</c>). The OpenAPI document lists
    /// the values, so the SPA's types do too.
    /// </summary>
    public static IServiceCollection AddPfmJson(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
        {
            // The first converter that takes a type wins, so the exception comes before the rule.
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<Currency>(allowIntegerValues: false));
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        });
}
