namespace Pfm.Domain.ExchangeRates;

/// <summary>
/// What one unit of one currency (<see cref="From"/>) was worth in another (<see cref="To"/>) on one day (spec
/// section 3, rule 7).
/// Fetched from an external source, never entered by a user, and shared by every user: it belongs to no one.
/// There is at most one for a pair of currencies on a day, and it never changes once stored.
/// </summary>
/// <remarks>
/// The pair is named from and to, not base and quote as rate sources name it: base currency is the glossary's
/// word for the currency a user's totals are in.
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

    private ExchangeRate(Currency from, Currency to, DateOnly date, decimal rate)
    {
        From = from;
        To = to;
        Date = date;
        Rate = rate;
    }

    /// <summary>The currency the rate is the worth of one unit of.</summary>
    public Currency From { get; private set; }

    /// <summary>The currency that worth is in.</summary>
    public Currency To { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>How much of <see cref="To"/> one unit of <see cref="From"/> buys.</summary>
    public decimal Rate { get; private set; }

    public static ExchangeRate Create(Currency from, Currency to, DateOnly date, decimal rate)
    {
        if (from == to)
        {
            throw new ArgumentException("An exchange rate is between two different currencies.", nameof(to));
        }

        if (!IsValidRate(rate))
        {
            throw new ArgumentException("An exchange rate is a positive number below ten billion.", nameof(rate));
        }

        return new ExchangeRate(from, to, date, decimal.Round(rate, Decimals));
    }

    /// <summary>
    /// An amount of <see cref="From"/> in <see cref="To"/>, rounded to the decimals that currency has
    /// (a half rounds away from zero).
    /// </summary>
    public decimal Convert(decimal amount) =>
        decimal.Round(amount * Rate, To.DecimalPlaces(), MidpointRounding.AwayFromZero);

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
            .. from one in worth
               from other in worth
               where one.Key != other.Key
               let rate = decimal.Round(other.Value / one.Value, Decimals)
               where IsValidRate(rate)
               select new ExchangeRate(one.Key, other.Key, date, rate),
        ];
    }

    private static bool IsValidRate(decimal rate) => rate > 0 && rate < Limit;
}
