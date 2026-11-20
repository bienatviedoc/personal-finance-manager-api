using Microsoft.Extensions.Options;
using Pfm.Application.ExchangeRates;
using Pfm.Infrastructure.ExchangeRates;

namespace Pfm.Worker.ExchangeRates;

/// <summary>
/// Fetches exchange rates when the worker starts and several times a day after that, so each day's rates
/// arrive without anyone entering them (spec section 3, rule 7). A fetch that fails is logged, which reports
/// it to the error tracker, and the next one catches up on the days it missed.
/// </summary>
public sealed partial class ExchangeRateFetchJob(
    IServiceScopeFactory scopes, IOptions<ExchangeRateOptions> options, TimeProvider time, ILogger<ExchangeRateFetchJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.FetchEvery, time);
        do
        {
            await FetchAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<FetchExchangeRatesHandler>()
                .HandleAsync(new FetchExchangeRatesCommand(options.Value.DaysBackWhenEmpty), cancellationToken);

            if (result.IsValid)
            {
                LogFetched(result.Outcome.Days);
            }
            else
            {
                LogMisconfigured(ExchangeRateOptions.Section, string.Join(", ", result.Errors.Keys));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetched exchange rates for {Days} day(s)")]
    private partial void LogFetched(int days);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exchange rates were not fetched: the {Section} configuration is wrong ({Fields})")]
    private partial void LogMisconfigured(string section, string fields);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fetching exchange rates failed; the next fetch will catch up")]
    private partial void LogFailed(Exception exception);
}
