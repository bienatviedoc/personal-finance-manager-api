namespace Pfm.Domain.Transactions;

/// <summary>
/// What a transaction's amount is in the user's base currency (spec section 3, rule 7; decision record
/// 0002): converted, with the exchange rate it was converted at and the day that rate is of, or not converted
/// and waiting for a rate, in the base currency it will be converted into. The amount, the rate and its day
/// are all there or none of them is.
/// </summary>
public sealed record BaseCurrencyConversion
{
    /// <summary>For persistence, which sets every property itself.</summary>
    private BaseCurrencyConversion()
    {
    }

    /// <summary>The base currency the amount is converted into, or is waiting to be.</summary>
    public Currency BaseCurrency { get; private init; }

    /// <summary>The amount in <see cref="BaseCurrency"/>, without a sign like the transaction's own; null while not converted.</summary>
    public decimal? Amount { get; private init; }

    /// <summary>What one unit of the transaction's currency was worth in <see cref="BaseCurrency"/>; null while not converted.</summary>
    public decimal? ExchangeRate { get; private init; }

    /// <summary>The day <see cref="ExchangeRate"/> is of: the transaction's date, or the nearest earlier day that had a rate.</summary>
    public DateOnly? ExchangeRateDate { get; private init; }

    public bool IsConverted => Amount is not null;

    /// <summary>Whether an amount fits the base currency: what a converted amount must do to be kept.</summary>
    public static bool CanHold(decimal amount, Currency baseCurrency) => amount >= 0 && Amounts.IsValid(amount, baseCurrency);

    /// <param name="amount">The amount in <paramref name="baseCurrency"/>, rounded to its decimals.</param>
    /// <param name="exchangeRate">What one unit of the transaction's currency is worth in <paramref name="baseCurrency"/>.</param>
    /// <param name="exchangeRateDate">The day the rate is of.</param>
    public static BaseCurrencyConversion Converted(Currency baseCurrency, decimal amount, decimal exchangeRate, DateOnly exchangeRateDate)
    {
        if (!CanHold(amount, Supported(baseCurrency)))
        {
            throw new ArgumentException($"A converted amount is not negative and fits {baseCurrency}.", nameof(amount));
        }

        if (exchangeRate <= 0)
        {
            throw new ArgumentException("An exchange rate is greater than zero.", nameof(exchangeRate));
        }

        return new BaseCurrencyConversion { BaseCurrency = baseCurrency, Amount = amount, ExchangeRate = exchangeRate, ExchangeRateDate = exchangeRateDate };
    }

    /// <summary>No exchange rate exists on or before the transaction's date, so it waits for one.</summary>
    public static BaseCurrencyConversion NotConverted(Currency baseCurrency) => new() { BaseCurrency = Supported(baseCurrency) };

    private static Currency Supported(Currency baseCurrency) =>
        Enum.IsDefined(baseCurrency) ? baseCurrency : throw new ArgumentOutOfRangeException(nameof(baseCurrency), baseCurrency, "Not a supported currency.");
}
