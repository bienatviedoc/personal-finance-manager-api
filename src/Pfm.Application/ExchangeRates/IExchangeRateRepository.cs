using Pfm.Domain;
using Pfm.Domain.ExchangeRates;

namespace Pfm.Application.ExchangeRates;

/// <summary>The stored exchange rates, shared by every user.</summary>
public interface IExchangeRateRepository
{
    /// <summary>
    /// The rate of the pair on the date, or on the nearest earlier day that has one; null when there is
    /// none on or before it.
    /// </summary>
    Task<ExchangeRate?> FindOnOrBeforeAsync(Currency baseCurrency, Currency quoteCurrency, DateOnly date, CancellationToken cancellationToken);

    /// <summary>The latest day any rate is stored for; null while there are none.</summary>
    Task<DateOnly?> LatestDateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The earliest day on or after <paramref name="since"/> that is stored without a rate for every pair of
    /// currencies (<see cref="ExchangeRate.PairsADay"/>); null when every stored day since then is complete.
    /// </summary>
    Task<DateOnly?> EarliestIncompleteDateAsync(DateOnly since, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the rates that are not stored yet, at once, and answers how many days it stored any for. A
    /// pair already stored for a day is left as it is, so a stored rate never changes and two fetches
    /// storing the same day at the same moment do not collide.
    /// </summary>
    Task<int> StoreMissingAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken cancellationToken);
}
