using Pfm.Domain;

namespace Pfm.Application.ExchangeRates;

/// <summary>
/// How every module turns an amount in one currency into another, usually a user's base currency. Rates
/// are fetched each day and shared by all users; nobody enters one (spec section 3, rule 7).
/// </summary>
public interface ICurrencyConversion
{
    /// <summary>
    /// The amount in <paramref name="to"/> at the rate of <paramref name="date"/>, or of the nearest earlier
    /// day that has one. With no rate on or before the date the answer is <see cref="Conversion.NotConverted"/>:
    /// the caller keeps the amount, flags it as not converted and leaves it out of totals. An amount already
    /// in <paramref name="to"/> is answered as it is, at a rate of one.
    /// </summary>
    Task<Conversion> ConvertAsync(decimal amount, Currency from, Currency to, DateOnly date, CancellationToken cancellationToken);

    /// <summary>
    /// Several amounts of one currency on one date, each converted as <see cref="ConvertAsync(decimal, Currency, Currency, DateOnly, CancellationToken)"/>
    /// would and answered in the order given, with the rate looked up once for all of them.
    /// </summary>
    Task<IReadOnlyList<Conversion>> ConvertAsync(
        IReadOnlyList<decimal> amounts, Currency from, Currency to, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>What converting an amount came to.</summary>
/// <param name="Amount">The amount in the currency asked for, rounded to its decimals; null when not converted.</param>
/// <param name="Rate">The rate it was converted at; null when not converted.</param>
/// <param name="RateDate">The day that rate is of: the date asked for, or the nearest earlier one with a rate.</param>
public sealed record Conversion(decimal? Amount, decimal? Rate, DateOnly? RateDate)
{
    /// <summary>No rate exists on or before the date, so there is no converted amount.</summary>
    public static readonly Conversion NotConverted = new(null, null, null);

    public bool IsConverted => Amount is not null;
}

/// <summary>Converts with the stored rates.</summary>
public sealed class CurrencyConversion(IExchangeRateRepository rates) : ICurrencyConversion
{
    public async Task<Conversion> ConvertAsync(decimal amount, Currency from, Currency to, DateOnly date, CancellationToken cancellationToken) =>
        (await ConvertAsync([amount], from, to, date, cancellationToken))[0];

    public async Task<IReadOnlyList<Conversion>> ConvertAsync(
        IReadOnlyList<decimal> amounts, Currency from, Currency to, DateOnly date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        if (from == to)
        {
            return [.. amounts.Select(amount => new Conversion(amount, 1m, date))];
        }

        return await rates.FindOnOrBeforeAsync(from, to, date, cancellationToken) is { } rate
            ? [.. amounts.Select(amount => new Conversion(rate.Convert(amount), rate.Rate, rate.Date))]
            : [.. amounts.Select(_ => Conversion.NotConverted)];
    }
}
