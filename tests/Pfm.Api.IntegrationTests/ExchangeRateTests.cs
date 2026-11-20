using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.ExchangeRates;
using Pfm.Domain;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// Exchange rates have no endpoint: the worker fetches them and other use cases convert with them. These
/// tests run that fetch against the real database with the source faked, and convert through
/// <see cref="ICurrencyConversion"/>, the interface every module converts through.
/// </summary>
[Collection(ApiCollection.Name)]
public class ExchangeRateTests(PfmApi api) : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_fetch_stores_the_rates_the_source_has_and_an_amount_converts_at_the_rate_of_its_date()
    {
        api.ExchangeRateSource.Quote(Today.AddDays(-1), dong: 25000m, euros: 0.8m);
        api.ExchangeRateSource.Quote(Today, dong: 26000m, euros: 0.9m);

        await api.FetchExchangeRatesAsync();

        Assert.Equal(new Conversion(2_500_000m, 25000m, Today.AddDays(-1)), await ConvertAsync(100m, Currency.USD, Currency.VND, Today.AddDays(-1)));
        Assert.Equal(new Conversion(2_600_000m, 26000m, Today), await ConvertAsync(100m, Currency.USD, Currency.VND, Today));
    }

    [Fact]
    public async Task Any_pair_of_currencies_converts_in_either_direction()
    {
        api.ExchangeRateSource.Quote(Today, dong: 25000m, euros: 0.8m);

        await api.FetchExchangeRatesAsync();

        Assert.Equal(40m, (await ConvertAsync(1_000_000m, Currency.VND, Currency.USD, Today)).Amount);
        Assert.Equal(3_125_000m, (await ConvertAsync(100m, Currency.EUR, Currency.VND, Today)).Amount);
        Assert.Equal(32m, (await ConvertAsync(1_000_000m, Currency.VND, Currency.EUR, Today)).Amount);
        Assert.Equal(125m, (await ConvertAsync(100m, Currency.EUR, Currency.USD, Today)).Amount);
    }

    [Fact]
    public async Task A_day_with_no_rate_falls_back_to_the_nearest_earlier_day_that_has_one()
    {
        api.ExchangeRateSource.Quote(Today.AddDays(-5), dong: 24000m);
        api.ExchangeRateSource.Quote(Today.AddDays(-3), dong: 25000m);
        api.ExchangeRateSource.Quote(Today, dong: 26000m);
        await api.FetchExchangeRatesAsync();

        var converted = await ConvertAsync(1m, Currency.USD, Currency.VND, Today.AddDays(-1));

        Assert.Equal(new Conversion(25000m, 25000m, Today.AddDays(-3)), converted);
    }

    [Fact]
    public async Task With_no_rate_on_or_before_the_date_the_amount_is_reported_as_not_converted()
    {
        api.ExchangeRateSource.Quote(Today, dong: 26000m);
        await api.FetchExchangeRatesAsync();

        var converted = await ConvertAsync(1m, Currency.USD, Currency.VND, Today.AddDays(-1));

        Assert.False(converted.IsConverted);
        Assert.Equal(Conversion.NotConverted, converted);
    }

    [Fact]
    public async Task A_currency_the_source_did_not_quote_that_day_is_not_converted_while_the_others_are()
    {
        api.ExchangeRateSource.Quote(Today, euros: 0.8m);
        await api.FetchExchangeRatesAsync();

        Assert.Equal(125m, (await ConvertAsync(100m, Currency.EUR, Currency.USD, Today)).Amount);
        Assert.False((await ConvertAsync(100m, Currency.EUR, Currency.VND, Today)).IsConverted);
    }

    [Fact]
    public async Task An_amount_already_in_the_currency_asked_for_needs_no_rate()
    {
        var converted = await ConvertAsync(1500m, Currency.VND, Currency.VND, Today);

        Assert.Equal(new Conversion(1500m, 1m, Today), converted);
    }

    [Fact]
    public async Task The_first_fetch_goes_back_as_far_as_it_is_told_and_later_ones_only_ask_for_the_days_since_the_latest_stored()
    {
        api.ExchangeRateSource.Quote(Today.AddDays(-2), dong: 25000m);

        var first = await api.FetchExchangeRatesAsync(daysBackWhenEmpty: 30);
        api.ExchangeRateSource.Quote(Today.AddDays(-1), dong: 25500m);
        api.ExchangeRateSource.Quote(Today, dong: 26000m);
        var second = await api.FetchExchangeRatesAsync(daysBackWhenEmpty: 30);

        Assert.Equal((1, 2), (first.Outcome.Days, second.Outcome.Days));
        Assert.Equal([(Today.AddDays(-30), Today), (Today.AddDays(-1), Today)], api.ExchangeRateSource.Asked);
        Assert.Equal(26000m, (await ConvertAsync(1m, Currency.USD, Currency.VND, Today)).Rate);
    }

    [Fact]
    public async Task Once_today_is_stored_a_fetch_asks_the_source_for_nothing_and_a_stored_rate_never_changes()
    {
        api.ExchangeRateSource.Quote(Today, dong: 26000m);
        await api.FetchExchangeRatesAsync();
        api.ExchangeRateSource.Quote(Today, dong: 27000m);

        var again = await api.FetchExchangeRatesAsync();

        Assert.Equal(0, again.Outcome.Days);
        Assert.Single(api.ExchangeRateSource.Asked);
        Assert.Equal(26000m, (await ConvertAsync(1m, Currency.USD, Currency.VND, Today)).Rate);
    }

    [Fact]
    public async Task By_default_the_first_fetch_goes_back_a_year()
    {
        await api.FetchExchangeRatesAsync();

        Assert.Equal([(Today.AddDays(-365), Today)], api.ExchangeRateSource.Asked);
    }

    [Fact]
    public async Task When_the_source_is_down_the_fetch_fails_stores_nothing_and_the_next_one_catches_up()
    {
        api.ExchangeRateSource.Quote(Today.AddDays(-1), dong: 25000m);
        api.ExchangeRateSource.Failure = new HttpRequestException("down");

        await Assert.ThrowsAsync<HttpRequestException>(() => api.FetchExchangeRatesAsync());
        Assert.False((await ConvertAsync(1m, Currency.USD, Currency.VND, Today)).IsConverted);

        api.ExchangeRateSource.Failure = null;
        await api.FetchExchangeRatesAsync();

        Assert.Equal(25000m, (await ConvertAsync(1m, Currency.USD, Currency.VND, Today)).Amount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3661)]
    public async Task A_fetch_cannot_be_told_to_go_back_a_negative_number_of_days_or_more_than_ten_years(int daysBack)
    {
        var result = await api.FetchExchangeRatesAsync(daysBack);

        Assert.Equal(["DaysBackWhenEmpty"], result.Errors.Keys);
        Assert.Empty(api.ExchangeRateSource.Asked);
    }

    private async Task<Conversion> ConvertAsync(decimal amount, Currency from, Currency to, DateOnly date)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICurrencyConversion>().ConvertAsync(amount, from, to, date, CancellationToken.None);
    }
}
