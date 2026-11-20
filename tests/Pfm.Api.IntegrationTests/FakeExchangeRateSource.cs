using Pfm.Application.ExchangeRates;
using Pfm.Domain;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// Stands in for the external exchange-rate source, so no test reaches the network: it quotes what a test
/// says it does, and remembers every range of days it was asked for.
/// </summary>
public sealed class FakeExchangeRateSource : IExchangeRateSource
{
    private readonly Dictionary<DateOnly, DailyQuotes> _days = [];

    /// <summary>Every range of days asked for, in order.</summary>
    public List<(DateOnly From, DateOnly To)> Asked { get; } = [];

    /// <summary>When set, the source is down: asking it throws this.</summary>
    public Exception? Failure { get; set; }

    /// <summary>What a US dollar is worth on a day.</summary>
    public void Quote(DateOnly date, decimal? dong = null, decimal? euros = null)
    {
        var quotes = new Dictionary<Currency, decimal>();
        if (dong is { } vnd)
        {
            quotes[Currency.VND] = vnd;
        }

        if (euros is { } eur)
        {
            quotes[Currency.EUR] = eur;
        }

        _days[date] = new DailyQuotes(date, Currency.USD, quotes);
    }

    public void Reset()
    {
        _days.Clear();
        Asked.Clear();
        Failure = null;
    }

    public Task<IReadOnlyList<DailyQuotes>> FetchAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Asked.Add((from, to));
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult<IReadOnlyList<DailyQuotes>>([.. _days.Values.Where(day => day.Date >= from && day.Date <= to).OrderBy(day => day.Date)]);
    }
}
