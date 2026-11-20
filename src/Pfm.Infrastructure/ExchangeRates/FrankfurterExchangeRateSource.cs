using System.Globalization;
using System.Net.Http.Json;
using Pfm.Application.ExchangeRates;
using Pfm.Domain;

namespace Pfm.Infrastructure.ExchangeRates;

/// <summary>
/// Daily rates from Frankfurter (https://frankfurter.dev), which blends the reference rates central banks
/// publish. It needs no key, covers VND, USD and EUR, and answers a range of days in one request
/// (decision record 0003).
/// </summary>
public sealed class FrankfurterExchangeRateSource(HttpClient http) : IExchangeRateSource
{
    /// <summary>Every rate is asked for against the dollar; the rates between the others follow from those.</summary>
    private const Currency QuotedAgainst = Currency.USD;

    private static readonly string Quotes = string.Join(',', Enum.GetValues<Currency>().Where(currency => currency != QuotedAgainst));

    public async Task<IReadOnlyList<DailyQuotes>> FetchAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rates = await http.GetFromJsonAsync<List<FrankfurterRate>>(
            $"v2/rates?base={QuotedAgainst}&quotes={Quotes}&from={IsoDate(from)}&to={IsoDate(to)}", cancellationToken) ?? [];

        return
        [
            .. rates
                .Where(rate => rate.Base == QuotedAgainst.ToString())
                .GroupBy(rate => rate.Date)
                .OrderBy(day => day.Key)
                .Select(day => new DailyQuotes(day.Key, QuotedAgainst, QuotesOf(day)))
                .Where(day => day.Quotes.Count > 0),
        ];
    }

    private static Dictionary<Currency, decimal> QuotesOf(IEnumerable<FrankfurterRate> day)
    {
        var quotes = new Dictionary<Currency, decimal>();
        foreach (var rate in day)
        {
            if (Currencies.TryParse(rate.Quote, out var currency))
            {
                quotes[currency] = rate.Rate;
            }
        }

        return quotes;
    }

    private static string IsoDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>One row of <c>GET /v2/rates</c>: <c>{"date":"2026-11-20","base":"USD","quote":"VND","rate":25775}</c>.</summary>
    private sealed record FrankfurterRate(DateOnly Date, string Base, string Quote, decimal Rate);
}
