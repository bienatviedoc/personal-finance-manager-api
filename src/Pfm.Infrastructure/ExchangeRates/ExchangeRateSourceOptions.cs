namespace Pfm.Infrastructure.ExchangeRates;

/// <summary>
/// Where exchange rates are fetched from (`ExchangeRates__SourceUrl`). The default suits every environment. How
/// often the worker fetches them, and how far back, are the worker's own (`ExchangeRateFetchOptions`), from the same
/// section.
/// </summary>
public sealed class ExchangeRateSourceOptions
{
    public const string Section = "ExchangeRates";

    /// <summary>The Frankfurter API: the public instance, or a self-hosted one.</summary>
    public string SourceUrl { get; set; } = "https://api.frankfurter.dev/";
}
