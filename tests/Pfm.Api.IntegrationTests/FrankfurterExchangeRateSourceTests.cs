using System.Net;
using System.Text;
using Pfm.Domain;
using Pfm.Infrastructure.ExchangeRates;

namespace Pfm.Api.IntegrationTests;

/// <summary>The adapter to Frankfurter, against answers recorded from it. Nothing here reaches the network.</summary>
public class FrankfurterExchangeRateSourceTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Tuesday = new(2026, 11, 17);

    [Fact]
    public async Task It_asks_for_what_a_dollar_was_worth_in_every_other_currency_over_the_days_and_reads_the_answer_a_day_at_a_time()
    {
        var frankfurter = new StubHandler("""
            [{"date":"2026-11-16","base":"USD","quote":"EUR","rate":0.90628},{"date":"2026-11-16","base":"USD","quote":"VND","rate":25775},
             {"date":"2026-11-17","base":"USD","quote":"EUR","rate":0.90868},{"date":"2026-11-17","base":"USD","quote":"VND","rate":25794}]
            """);

        var days = await SourceOver(frankfurter).FetchAsync(Monday, Tuesday, CancellationToken.None);

        Assert.Equal("https://rates.test/v2/rates?base=USD&quotes=VND,EUR&from=2026-11-16&to=2026-11-17", frankfurter.Asked.Single());
        Assert.Equal([Monday, Tuesday], days.Select(day => day.Date));
        Assert.All(days, day => Assert.Equal(Currency.USD, day.QuotedAgainst));
        Assert.Equal((0.90628m, 25775m), (days[0].Quotes[Currency.EUR], days[0].Quotes[Currency.VND]));
        Assert.Equal((0.90868m, 25794m), (days[1].Quotes[Currency.EUR], days[1].Quotes[Currency.VND]));
    }

    [Fact]
    public async Task Days_it_has_nothing_for_and_currencies_the_app_does_not_know_are_left_out()
    {
        var frankfurter = new StubHandler("""[{"date":"2026-11-17","base":"USD","quote":"XAU","rate":0.0002},{"date":"2026-11-17","base":"USD","quote":"VND","rate":25794}]""");

        var days = await SourceOver(frankfurter).FetchAsync(Monday, Tuesday, CancellationToken.None);

        var tuesday = Assert.Single(days);
        Assert.Equal(Tuesday, tuesday.Date);
        Assert.Equal([Currency.VND], tuesday.Quotes.Keys);
    }

    [Fact]
    public async Task No_rates_at_all_is_no_days()
    {
        Assert.Empty(await SourceOver(new StubHandler("[]")).FetchAsync(Monday, Tuesday, CancellationToken.None));
    }

    [Fact]
    public async Task An_answer_that_is_not_a_success_throws()
    {
        var down = new StubHandler("""{"message":"down"}""", HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<HttpRequestException>(() => SourceOver(down).FetchAsync(Monday, Tuesday, CancellationToken.None));
    }

    private static FrankfurterExchangeRateSource SourceOver(StubHandler frankfurter) =>
        new(new HttpClient(frankfurter) { BaseAddress = new Uri("https://rates.test/") });

    private sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
