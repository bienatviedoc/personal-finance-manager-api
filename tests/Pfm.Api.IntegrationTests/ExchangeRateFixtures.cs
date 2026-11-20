using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.ExchangeRates;
using Pfm.Application.UseCases;
using Pfm.Domain.ExchangeRates;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Api.IntegrationTests;

/// <summary>What tests about exchange rates need from the running API.</summary>
public static class ExchangeRateFixtures
{
    extension(PfmApi api)
    {
        /// <summary>The external exchange-rate source, faked: it quotes what a test says and is never the network.</summary>
        public FakeExchangeRateSource ExchangeRateSource => (FakeExchangeRateSource)api.Services.GetRequiredService<IExchangeRateSource>();

        /// <summary>
        /// Starts a test with no rates stored and a source that quotes nothing. Rates are shared by every
        /// user, so without this a test would convert at rates another test fetched.
        /// </summary>
        public async Task ForgetExchangeRatesAsync()
        {
            api.ExchangeRateSource.Reset();
            await using var scope = api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PfmDbContext>().Set<ExchangeRate>().ExecuteDeleteAsync();
        }

        /// <summary>Fetches rates the way the worker's daily job does.</summary>
        public async Task<Result<ExchangeRatesFetched>> FetchExchangeRatesAsync(int daysBackWhenEmpty = FetchExchangeRatesCommand.DefaultDaysBackWhenEmpty)
        {
            await using var scope = api.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<FetchExchangeRatesHandler>().HandleAsync(new FetchExchangeRatesCommand(daysBackWhenEmpty));
        }
    }
}
