namespace Pfm.Domain.ExchangeRates;

/// <summary>
/// What one unit of the base currency was worth in the quote currency on one day (spec section 3, rule 7).
/// Fetched from an external source, never entered by a user, and shared by every user: it belongs to no one.
/// There is at most one for a pair of currencies on a day, and it never changes once stored.
/// </summary>
/// <remarks>
/// "Base currency" here is the pair's first currency, as rate sources name it. It is not a user's base
/// currency (see the glossary), which is the currency their totals are converted into.
/// </remarks>
public sealed class ExchangeRate
{
    /// <summary>How many decimals a rate keeps: enough that the largest amount converts to the cent.</summary>
    public const int Decimals = 18;

    /// <summary>How many rates a day has once every pair of currencies is quoted, in both directions.</summary>
    public static readonly int PairsADay = Enum.GetValues<Currency>().Length * (Enum.GetValues<Currency>().Length - 1);

    /// <summary>What <c>numeric(28,18)</c> holds before the decimal point.</summary>
    private const decimal Limit = 10_000_000_000m;

    /// <summary>For persistence, which sets every property itself.</summary>
    private ExchangeRate()
    {
    }

    private ExchangeRate(Currency baseCurrency, Currency quoteCurrency, DateOnly date, decimal rate)
    {
        BaseCurrency = baseCurrency;
        QuoteCurrency = quoteCurrency;
        Date = date;
        Rate = rate;
    }

    public Currency BaseCurrency { get; private set; }

    public Currency QuoteCurrency { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>How much of the quote currency one unit of the base currency buys.</summary>
    public decimal Rate { get; private set; }

    public static ExchangeRate Create(Currency baseCurrency, Currency quoteCurrency, DateOnly date, decimal rate)
    {
        if (baseCurrency == quoteCurrency)
        {
            throw new ArgumentException("An exchange rate is between two different currencies.", nameof(quoteCurrency));
        }

        if (!IsValidRate(rate))
        {
            throw new ArgumentException("An exchange rate is a positive number below ten billion.", nameof(rate));
        }

        return new ExchangeRate(baseCurrency, quoteCurrency, date, decimal.Round(rate, Decimals));
    }

    /// <summary>
    /// An amount of the base currency in the quote currency, rounded to the decimals that currency has
    /// (a half rounds away from zero).
    /// </summary>
    public decimal Convert(decimal amount) =>
        decimal.Round(amount * Rate, QuoteCurrency.DecimalPlaces(), MidpointRounding.AwayFromZero);

    /// <summary>
    /// The rate of every pair of currencies, in both directions, that one day's quotes against one currency
    /// give: with USD quoted at 25,000 VND and 0.8 EUR, a euro is 31,250 VND. A currency the quotes leave
    /// out, or quote at something that is not a rate, gets none.
    /// </summary>
    /// <param name="quotedAgainst">The currency the quotes are prices of one unit of.</param>
    public static IReadOnlyList<ExchangeRate> BetweenEvery(Currency quotedAgainst, DateOnly date, IReadOnlyDictionary<Currency, decimal> quotes)
    {
        var worth = quotes
            .Where(quote => quote.Key != quotedAgainst && IsValidRate(quote.Value))
            .Append(new(quotedAgainst, 1m))
            .ToList();

        return
        [
            .. from @base in worth
               from quote in worth
               where @base.Key != quote.Key
               let rate = decimal.Round(quote.Value / @base.Value, Decimals)
               where IsValidRate(rate)
               select new ExchangeRate(@base.Key, quote.Key, date, rate),
        ];
    }

    private static bool IsValidRate(decimal rate) => rate > 0 && rate < Limit;
}
