namespace Pfm.Infrastructure.ExchangeRates;

/// <summary>Where exchange rates come from and how often. The defaults suit every environment.</summary>
public sealed class ExchangeRateOptions
{
    public const string Section = "ExchangeRates";

    /// <summary>The Frankfurter API: the public instance, or a self-hosted one.</summary>
    public string SourceUrl { get; set; } = "https://api.frankfurter.dev/";

    /// <summary>How far back the first fetch goes, while no rate is stored yet.</summary>
    public int DaysBackWhenEmpty { get; set; } = Application.ExchangeRates.FetchExchangeRatesCommand.DefaultDaysBackWhenEmpty;

    /// <summary>
    /// How long the worker waits between fetches. Shorter than a day, so that a day's rates arrive within
    /// hours of the source publishing them and a failed fetch is soon tried again; a fetch that finds
    /// nothing new stores nothing.
    /// </summary>
    public TimeSpan FetchEvery { get; set; } = TimeSpan.FromHours(6);
}
