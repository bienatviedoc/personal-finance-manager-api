using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>What a user is worth over time: assets minus liabilities at each month end (spec section 7, RPT-5).</summary>
[Collection(ApiCollection.Name)]
public class NetWorthTests(PfmApi api) : IAsyncLifetime
{
    private const string Report = "/api/v1/reports/net-worth";

    private const string FirstQuarter = "from=2026-01-01&to=2026-03-31";

    private static readonly DateOnly EndOfJanuary = new(2026, 1, 31);

    private static readonly DateOnly EndOfFebruary = new(2026, 2, 28);

    private static readonly DateOnly EndOfMarch = new(2026, 3, 31);

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_report_shows_assets_liabilities_and_net_worth_at_each_month_end_with_credit_cards_and_loans_as_liabilities()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", openingBalance: "1000000");
        var card = await AddAsync(browser, new { name = "Visa", type = "creditCard", currency = "VND", amountOwed = "300000", openingDate = "2026-01-01" });
        await AddAsync(browser, new { name = "Car loan", type = "loan", currency = "VND", amountOwed = "5000000", openingDate = "2026-02-15" });
        await RecordAsync(browser, "income", everyday, "Salary", "2000000", new DateOnly(2026, 2, 10));
        await SpendAsync(browser, card, "Groceries", "200000", new DateOnly(2026, 3, 5));
        await TransferAsync(browser, everyday, card, "300000", new DateOnly(2026, 3, 20));
        await SpendAsync(browser, everyday, "Groceries", "999000", new DateOnly(2026, 4, 1));

        var report = await ReportAsync(browser, FirstQuarter);

        Assert.Equal((new DateOnly(2026, 1, 1), EndOfMarch, "VND"), (report.From, report.To, report.Currency));
        Assert.Equal(
            [
                new Point(EndOfJanuary, "1000000.00", "300000.00", "700000.00", 0),
                new Point(EndOfFebruary, "3000000.00", "5300000.00", "-2300000.00", 0),
                new Point(EndOfMarch, "2700000.00", "5200000.00", "-2500000.00", 0),
            ],
            report.Items);
    }

    [Fact]
    public async Task Archived_accounts_stay_in_net_worth()
    {
        var (browser, _) = await api.SignedInAsync();
        await AddAccountAsync(browser, "Everyday", openingBalance: "1000000");
        var old = await AddAccountAsync(browser, "Old savings", openingBalance: "400000");
        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"/api/v1/accounts/{old}")).StatusCode);

        var report = await ReportAsync(browser, "from=2026-01-01&to=2026-01-31");

        Assert.Equal([new Point(EndOfJanuary, "1400000.00", "0.00", "1400000.00", 0)], report.Items);
    }

    [Fact]
    public async Task Balances_are_converted_to_the_base_currency_at_the_rate_of_each_month_end()
    {
        await RatesAreAsync((EndOfJanuary, 25000m), (new DateOnly(2026, 2, 27), 26000m), (EndOfMarch, 27000m), (new DateOnly(2026, 4, 1), 99000m));
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "Dollars", "USD", "100");
        await AddAsync(browser, new { name = "Dollar card", type = "creditCard", currency = "USD", amountOwed = "20", openingDate = "2026-01-01" });
        await RecordAsync(browser, "income", dollars, "Salary", "10", new DateOnly(2026, 2, 10));

        var report = await ReportAsync(browser, FirstQuarter);

        // February ends on a Saturday: its rate is the Friday's, the nearest earlier day with one.
        Assert.Equal(
            [
                new Point(EndOfJanuary, "2500000.00", "500000.00", "2000000.00", 0),
                new Point(EndOfFebruary, "2860000.00", "520000.00", "2340000.00", 0),
                new Point(EndOfMarch, "2970000.00", "540000.00", "2430000.00", 0),
            ],
            report.Items);
    }

    [Fact]
    public async Task A_balance_with_no_rate_on_or_before_the_month_end_is_left_out_and_counted_as_not_converted()
    {
        await RatesAreAsync((EndOfFebruary, 25000m));
        var (browser, _) = await api.SignedInAsync();
        await AddAccountAsync(browser, "Everyday", openingBalance: "1000000");
        await AddAccountAsync(browser, "Dollars", "USD", "100");
        await AddAccountAsync(browser, "Euros", "EUR", "50");

        var report = await ReportAsync(browser, "from=2026-01-01&to=2026-02-28");

        Assert.Equal(
            [
                new Point(EndOfJanuary, "1000000.00", "0.00", "1000000.00", 2),
                new Point(EndOfFebruary, "3500000.00", "0.00", "3500000.00", 1),
            ],
            report.Items);
    }

    [Fact]
    public async Task An_account_counts_from_its_opening_date_on()
    {
        var (browser, _) = await api.SignedInAsync();
        await AddAccountAsync(browser, "Everyday", openingBalance: "1000000");
        await AddAsync(browser, new { name = "New savings", type = "savings", currency = "VND", openingBalance = "500000", openingDate = "2026-02-01" });

        var report = await ReportAsync(browser, "from=2026-01-01&to=2026-02-28");

        Assert.Equal(["1000000.00", "1500000.00"], report.Items.Select(point => point.NetWorth));
    }

    [Fact]
    public async Task A_period_that_ends_inside_a_month_ends_on_its_last_day()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", openingBalance: "1000000");
        await SpendAsync(browser, everyday, "Groceries", "100000", new DateOnly(2026, 2, 10));
        await SpendAsync(browser, everyday, "Groceries", "999000", new DateOnly(2026, 2, 11));

        var report = await ReportAsync(browser, "from=2026-01-15&to=2026-02-10");

        Assert.Equal([(EndOfJanuary, "1000000.00"), (new DateOnly(2026, 2, 10), "900000.00")], report.Items.Select(point => (point.Date, point.NetWorth)));
    }

    [Fact]
    public async Task The_account_filter_shows_one_account_and_an_account_the_user_does_not_have_shows_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var (stranger, _) = await api.SignedInAsync();
        await AddAccountAsync(browser, "Everyday", openingBalance: "1000000");
        var card = await AddAsync(browser, new { name = "Visa", type = "creditCard", currency = "VND", amountOwed = "300000", openingDate = "2026-01-01" });
        var theirs = await AddAccountAsync(stranger, "Theirs", openingBalance: "7000000");

        var onCard = await ReportAsync(browser, $"from=2026-01-01&to=2026-01-31&accountId={card}");
        var onTheirs = await ReportAsync(browser, $"from=2026-01-01&to=2026-01-31&accountId={theirs}");

        Assert.Equal([new Point(EndOfJanuary, "0.00", "300000.00", "-300000.00", 0)], onCard.Items);
        Assert.Equal([new Point(EndOfJanuary, "0.00", "0.00", "0.00", 0)], onTheirs.Items);
    }

    [Fact]
    public async Task A_deleted_transaction_does_not_count()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", openingBalance: "1000000");
        var spent = await SpendAsync(browser, everyday, "Groceries", "100000", new DateOnly(2026, 1, 10));
        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"/api/v1/transactions/{spent}")).StatusCode);

        var report = await ReportAsync(browser, "from=2026-01-01&to=2026-01-31");

        Assert.Equal("1000000.00", Assert.Single(report.Items).NetWorth);
    }

    [Theory]
    [InlineData("to=2026-01-31", "from")]
    [InlineData("from=2026-02-01&to=2026-01-31", "to")]
    [InlineData("from=2026-01-01&to=2026-01-31&accountId=card", "accountId")]
    public async Task A_period_or_account_that_cannot_be_read_is_refused_by_name(string query, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Report}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, (await response.Content.ReadFromJsonAsync<Problem>())!.Errors.Keys);
    }

    private async Task RatesAreAsync(params (DateOnly Date, decimal Dong)[] rates)
    {
        foreach (var (date, dong) in rates)
        {
            api.ExchangeRateSource.Quote(date, dong);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await api.FetchExchangeRatesAsync(daysBackWhenEmpty: today.DayNumber - new DateOnly(2025, 12, 1).DayNumber);
    }

    private static async Task<Guid> AddAsync(Browser browser, object account)
    {
        var response = await browser.PostAsync("/api/v1/accounts", account);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    private static async Task<NetWorthBody> ReportAsync(Browser browser, string query)
    {
        var response = await browser.GetAsync($"{Report}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NetWorthBody>())!;
    }

    private sealed record Identified(Guid Id);

    private sealed record Point(DateOnly Date, string Assets, string Liabilities, string NetWorth, int NotConverted);

    private sealed record NetWorthBody(DateOnly From, DateOnly To, string Currency, List<Point> Items);

    private sealed record Problem(Dictionary<string, string[]> Errors);
}
