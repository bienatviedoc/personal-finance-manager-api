using Pfm.Domain;
using Pfm.Domain.ExchangeRates;

namespace Pfm.Application.ExchangeRates;

/// <summary>The stored exchange rates, shared by every user. Additions are saved through <see cref="Abstractions.IUnitOfWork"/>.</summary>
public interface IExchangeRateRepository
{
    /// <summary>
    /// The rate of the pair on the date, or on the nearest earlier day that has one; null when there is
    /// none on or before it.
    /// </summary>
    Task<ExchangeRate?> FindOnOrBeforeAsync(Currency baseCurrency, Currency quoteCurrency, DateOnly date, CancellationToken cancellationToken);

    /// <summary>The latest day any rate is stored for; null while there are none.</summary>
    Task<DateOnly?> LatestDateAsync(CancellationToken cancellationToken);

    void Add(ExchangeRate rate);
}
