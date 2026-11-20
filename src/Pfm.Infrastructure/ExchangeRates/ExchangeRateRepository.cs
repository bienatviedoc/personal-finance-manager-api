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

    public async Task<DateOnly?> EarliestIncompleteDateAsync(DateOnly since, CancellationToken cancellationToken) =>
        await db.Set<ExchangeRate>()
            .Where(rate => rate.Date >= since)
            .GroupBy(rate => rate.Date)
            .Where(day => day.Count() < ExchangeRate.PairsADay)
            .Select(day => (DateOnly?)day.Key)
            .OrderBy(date => date)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// One <c>INSERT … ON CONFLICT DO NOTHING</c>: the database, not a read made beforehand, decides which
    /// rates are new, so fetches running at once cannot both store one.
    /// </summary>
    public async Task<int> StoreMissingAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken cancellationToken)
    {
        if (rates.Count == 0)
        {
            return 0;
        }

        var baseCurrencies = rates.Select(rate => rate.BaseCurrency.ToString()).ToArray();
        var quoteCurrencies = rates.Select(rate => rate.QuoteCurrency.ToString()).ToArray();
        var dates = rates.Select(rate => rate.Date).ToArray();
        var values = rates.Select(rate => rate.Rate).ToArray();

        var stored = await db.Database
            .SqlQuery<DateOnly>($"""
                INSERT INTO "ExchangeRates" ("BaseCurrency", "QuoteCurrency", "Date", "Rate")
                SELECT * FROM unnest({baseCurrencies}, {quoteCurrencies}, {dates}, {values})
                ON CONFLICT DO NOTHING
                RETURNING "Date" AS "Value"
                """)
            .ToListAsync(cancellationToken);

        return stored.Distinct().Count();
    }
}
