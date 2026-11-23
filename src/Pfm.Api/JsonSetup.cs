using Pfm.Application.UseCases;

namespace Pfm.Api;

public static class JsonSetup
{
    /// <summary>
    /// Enums travel as names, never as numbers, as <see cref="EnumText"/> says. The OpenAPI document lists the
    /// values, so the SPA's types do too.
    /// </summary>
    public static IServiceCollection AddPfmJson(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
        {
            foreach (var converter in EnumText.JsonConverters())
            {
                options.SerializerOptions.Converters.Add(converter);
            }
        });
}
