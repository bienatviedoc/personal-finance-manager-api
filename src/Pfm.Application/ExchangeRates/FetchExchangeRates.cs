using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.ExchangeRates;

namespace Pfm.Application.ExchangeRates;

/// <summary>
/// Fetches the exchange rates of every day since the latest one stored, up to today, and stores them. The
/// worker runs it each day; no user can (spec section 3, rule 7).
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
}

public sealed class FetchExchangeRatesValidator : AbstractValidator<FetchExchangeRatesCommand>
{
    public FetchExchangeRatesValidator()
    {
        RuleFor(command => command.DaysBackWhenEmpty).InclusiveBetween(0, FetchExchangeRatesCommand.MaxDaysBackWhenEmpty);
    }
}

/// <summary>How many days' rates a fetch stored: none when the stored rates were already up to date.</summary>
public sealed record ExchangeRatesFetched(int Days);

public sealed class FetchExchangeRatesHandler(
    IValidator<FetchExchangeRatesCommand> validator, IExchangeRateSource source, IExchangeRateRepository rates, IUnitOfWork unitOfWork,
    TimeProvider time)
    : CommandHandler<FetchExchangeRatesCommand, ExchangeRatesFetched>(validator)
{
    protected override async Task<Result<ExchangeRatesFetched>> ExecuteAsync(FetchExchangeRatesCommand command, CancellationToken cancellationToken)
    {
        // Sources publish by the UTC day.
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var latest = await rates.LatestDateAsync(cancellationToken);
        var from = latest?.AddDays(1) ?? today.AddDays(-command.DaysBackWhenEmpty);
        if (from > today)
        {
            return new ExchangeRatesFetched(0);
        }

        // A stored day is never fetched again: an amount converted at its rate stays right (decision record 0002).
        var days = (await source.FetchAsync(from, today, cancellationToken))
            .Where(day => day.Date >= from && day.Date <= today)
            .DistinctBy(day => day.Date)
            .Select(day => ExchangeRate.BetweenEvery(day.QuotedAgainst, day.Date, day.Quotes))
            .Where(day => day.Count > 0)
            .ToList();

        foreach (var rate in days.SelectMany(day => day))
        {
            rates.Add(rate);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ExchangeRatesFetched(days.Count);
    }
}
