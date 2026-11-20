using Microsoft.EntityFrameworkCore;
using Pfm.Application.ExchangeRates;
using Pfm.Domain;
using Pfm.Domain.ExchangeRates;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.ExchangeRates;

/// <summary>Exchange rates in the database. They belong to no user, so no user filter applies.</summary>
internal sealed class ExchangeRateRepository(PfmDbContext db) : IExchangeRateRepository
{
    public Task<ExchangeRate?> FindOnOrBeforeAsync(Currency baseCurrency, Currency quoteCurrency, DateOnly date, CancellationToken cancellationToken) =>
        db.Set<ExchangeRate>().AsNoTracking()
            .Where(rate => rate.BaseCurrency == baseCurrency && rate.QuoteCurrency == quoteCurrency && rate.Date <= date)
            .OrderByDescending(rate => rate.Date)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<DateOnly?> LatestDateAsync(CancellationToken cancellationToken) =>
        await db.Set<ExchangeRate>().MaxAsync(rate => (DateOnly?)rate.Date, cancellationToken);

    public void Add(ExchangeRate rate) => db.Set<ExchangeRate>().Add(rate);
}
