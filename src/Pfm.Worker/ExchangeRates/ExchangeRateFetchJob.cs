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
    : PeriodicJob(scopes, time)
{
    protected override TimeSpan RunEvery => options.Value.FetchEvery;

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<FetchExchangeRatesHandler>()
            .HandleAsync(new FetchExchangeRatesCommand(options.Value.DaysBackWhenEmpty), cancellationToken);

        if (result.IsValid)
        {
            LogFetched(result.Outcome.Days);
            if (result.Outcome.UsersNotSaved > 0)
            {
                LogUsersNotSaved(result.Outcome.UsersNotSaved);
            }
        }
        else
        {
            LogMisconfigured(ExchangeRateOptions.Section, string.Join(", ", result.Errors.Keys));
        }
    }

    protected override void OnFailed(Exception exception) => LogFailed(exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetched exchange rates for {Days} day(s)")]
    private partial void LogFetched(int days);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The not-converted transactions of {Users} user(s) kept changing and were not converted; the next fetch tries again")]
    private partial void LogUsersNotSaved(int users);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exchange rates were not fetched: the {Section} configuration is wrong ({Fields})")]
    private partial void LogMisconfigured(string section, string fields);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fetching exchange rates failed; the next fetch will catch up")]
    private partial void LogFailed(Exception exception);
}
