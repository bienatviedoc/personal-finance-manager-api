using FluentValidation;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain.ExchangeRates;

namespace Pfm.Application.ExchangeRates;

/// <summary>
/// Fetches the exchange rates of every day since the latest one stored, up to today, and stores them. A day
/// of the last week stored without every pair of currencies is asked for again, and the pairs it lacks are
/// filled in. The worker runs it several times a day; no user can (spec section 3, rule 7). Every fetch then
/// has the transactions flagged not converted converted (decision record 0002), also when it stored
/// nothing: the rates may have been stored by a fetch that stopped before converting.
/// </summary>
/// <param name="DaysBackWhenEmpty">
/// How far back the first fetch goes, while no rate is stored yet, so that amounts dated before the app was
/// set up can be converted too.
/// </param>
public sealed record FetchExchangeRatesCommand(int DaysBackWhenEmpty = FetchExchangeRatesCommand.DefaultDaysBackWhenEmpty)
{
    public const int DefaultDaysBackWhenEmpty = 365;

    /// <summary>Ten years.</summary>
    public const int MaxDaysBackWhenEmpty = 3660;

    /// <summary>
    /// How long a day stored without every pair of currencies is asked for again. A source that still has
    /// not quoted a currency for that day a week later is not going to.
    /// </summary>
    public const int DaysToComplete = 7;
}

public sealed class FetchExchangeRatesValidator : AbstractValidator<FetchExchangeRatesCommand>
{
    public FetchExchangeRatesValidator()
    {
        RuleFor(command => command.DaysBackWhenEmpty).InclusiveBetween(0, FetchExchangeRatesCommand.MaxDaysBackWhenEmpty);
    }
}

/// <summary>What a fetch came to.</summary>
/// <param name="Days">How many days it stored rates for: none when the stored rates were already up to date.</param>
/// <param name="UsersNotSaved">
/// How many users' not-converted transactions could not be saved, because they kept changing meanwhile. They
/// wait for the next fetch; everyone else's were converted.
/// </param>
public sealed record ExchangeRatesFetched(int Days, int UsersNotSaved);

public sealed class FetchExchangeRatesHandler(
    IValidator<FetchExchangeRatesCommand> validator, IExchangeRateSource source, IExchangeRateRepository rates,
    ITransactionConversion transactions, TimeProvider time)
    : CommandHandler<FetchExchangeRatesCommand, ExchangeRatesFetched>(validator)
{
    protected override async Task<Result<ExchangeRatesFetched>> ExecuteAsync(FetchExchangeRatesCommand command, CancellationToken cancellationToken)
    {
        // Sources publish by the UTC day.
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var from = await FirstDayToAskForAsync(today, command.DaysBackWhenEmpty, cancellationToken);
        var days = from > today ? 0 : await FetchAsync(from, today, cancellationToken);

        return new ExchangeRatesFetched(days, await transactions.ConvertNotConvertedAsync(cancellationToken));
    }

    /// <summary>The day after the latest one stored, or an earlier day of the last week that still lacks a pair.</summary>
    private async Task<DateOnly> FirstDayToAskForAsync(DateOnly today, int daysBackWhenEmpty, CancellationToken cancellationToken)
    {
        if (await rates.LatestDateAsync(cancellationToken) is not { } latest)
        {
            return today.AddDays(-daysBackWhenEmpty);
        }

        var incomplete = await rates.EarliestIncompleteDateAsync(today.AddDays(-FetchExchangeRatesCommand.DaysToComplete), cancellationToken);
        return incomplete ?? latest.AddDays(1);
    }

    /// <summary>
    /// How many days rates were stored for. A stored rate is never replaced: an amount converted at it
    /// stays right (decision record 0002).
    /// </summary>
    private async Task<int> FetchAsync(DateOnly from, DateOnly today, CancellationToken cancellationToken)
    {
        var fetched = (await source.FetchAsync(from, today, cancellationToken))
            .Where(day => day.Date >= from && day.Date <= today)
            .DistinctBy(day => day.Date)
            .SelectMany(day => ExchangeRate.BetweenEvery(day.QuotedAgainst, day.Date, day.Quotes))
            .ToList();

        return await rates.StoreMissingAsync(fetched, cancellationToken);
    }
}
