using Pfm.Application.ExchangeRates;
using Pfm.Infrastructure.ExchangeRates;

namespace Pfm.Worker.ExchangeRates;

/// <summary>
/// How often the worker fetches exchange rates and how far back the first fetch goes (`ExchangeRates__FetchEvery`,
/// `ExchangeRates__DaysBackWhenEmpty`). The defaults suit every environment. Where they are fetched from is
/// Infrastructure's (`ExchangeRateSourceOptions`), from the same section.
/// </summary>
public sealed class ExchangeRateFetchOptions
{
    /// <summary>The section Infrastructure reads where rates come from.</summary>
    public const string Section = ExchangeRateSourceOptions.Section;

    /// <summary>How far back the first fetch goes, while no rate is stored yet.</summary>
    public int DaysBackWhenEmpty { get; set; } = FetchExchangeRatesCommand.DefaultDaysBackWhenEmpty;

    /// <summary>
    /// How long the worker waits between fetches. Shorter than a day, so that a day's rates arrive within
    /// hours of the source publishing them and a failed fetch is soon tried again; a fetch that finds
    /// nothing new stores nothing.
    /// </summary>
    public TimeSpan FetchEvery { get; set; } = TimeSpan.FromHours(6);
}
