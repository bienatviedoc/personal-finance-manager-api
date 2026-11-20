using Pfm.Domain;

namespace Pfm.Application.ExchangeRates;

/// <summary>
/// The external source daily exchange rates come from. It must cover every <see cref="Currency"/>.
/// Which source it is, and why, is decision record 0003.
/// </summary>
public interface IExchangeRateSource
{
    /// <summary>
    /// The quotes of each day from <paramref name="from"/> to <paramref name="to"/>, both included, that the
    /// source has; a day it has nothing for is left out. Throws when the source cannot be reached.
    /// </summary>
    Task<IReadOnlyList<DailyQuotes>> FetchAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>What one unit of a currency was worth in other currencies on one day.</summary>
/// <param name="QuotedAgainst">The currency the quotes are prices of one unit of.</param>
/// <param name="Quotes">The price of one <paramref name="QuotedAgainst"/> in each other currency.</param>
public sealed record DailyQuotes(DateOnly Date, Currency QuotedAgainst, IReadOnlyDictionary<Currency, decimal> Quotes);
