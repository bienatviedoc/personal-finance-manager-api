using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Cash flow over time: the running balance of one account or of all of them (spec RPT-4).</summary>
[Collection(ApiCollection.Name)]
public class CashFlowTests(PfmApi api) : IAsyncLifetime
{
    private const string Report = "/api/v1/reports/cash-flow";

    private static readonly DateOnly March = new(2026, 3, 1);

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task One_accounts_daily_running_balance_is_its_opening_balance_plus_its_transactions_up_to_each_day_in_its_own_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var savings = await AddAccountAsync(browser, "Savings");
        await SpendAsync(browser, everyday, "Groceries", "100000", March.AddDays(-5));
        await RecordAsync(browser, "income", everyday, "Salary", "5000000", March.AddDays(1));
        await SpendAsync(browser, everyday, "Transport", "200000", March.AddDays(2));
        await RefundAsync(browser, everyday, "Clothes", "50000", March.AddDays(2));
        await TransferAsync(browser, everyday, savings, "1000000", March.AddDays(3));
        await SpendAsync(browser, everyday, "Rent", "9000000", March.AddDays(4));

        var report = await ReportAsync(browser, $"from=2026-03-01&to=2026-03-04&accountId={everyday}");

        Assert.Equal((March, March.AddDays(3), "day", "VND", (Guid?)everyday), (report.From, report.To, report.Interval, report.Currency, report.AccountId));
        Assert.Equal(
            [(March, "900000.00"), (March.AddDays(1), "5900000.00"), (March.AddDays(2), "5750000.00"), (March.AddDays(3), "4750000.00")],
            report.Points.Select(point => (point.Date, point.Balance)));
        Assert.All(report.Points, point => Assert.Empty(point.NotConverted));
    }

    [Fact]
    public async Task A_weekly_running_balance_is_the_balance_at_the_end_of_each_week_and_of_the_period()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        await SpendAsync(browser, everyday, "Transport", "100000", new DateOnly(2026, 3, 3));
        await SpendAsync(browser, everyday, "Transport", "200000", new DateOnly(2026, 3, 10));

        var report = await ReportAsync(browser, $"from=2026-03-02&to=2026-03-18&accountId={everyday}&interval=week");

        Assert.Equal("week", report.Interval);
        Assert.Equal(
            [(new DateOnly(2026, 3, 8), "900000.00"), (new DateOnly(2026, 3, 15), "700000.00"), (new DateOnly(2026, 3, 18), "700000.00")],
            report.Points.Select(point => (point.Date, point.Balance)));
    }

    [Fact]
    public async Task Across_all_accounts_archived_ones_included_the_balance_is_in_the_base_currency_at_each_days_exchange_rate()
    {
        api.ExchangeRateSource.Quote(March, dong: 25000m);
        api.ExchangeRateSource.Quote(March.AddDays(2), dong: 26000m);
        await api.FetchExchangeRatesAsync();
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var savings = await AddAccountAsync(browser, "Savings", "VND", "2000000");
        await AddAccountAsync(browser, "Dollars", "USD", "10");
        await TransferAsync(browser, everyday, savings, "500000", March.AddDays(1));
        await SpendAsync(browser, everyday, "Transport", "100000", March.AddDays(3));
        (await browser.DeleteAsync($"/api/v1/accounts/{savings}")).EnsureSuccessStatusCode();

        var report = await ReportAsync(browser, "from=2026-03-01&to=2026-03-04");

        // 10 USD is 250,000 VND until the rate of the 3rd, then 260,000 VND.
        Assert.Equal(("VND", (Guid?)null), (report.Currency, report.AccountId));
        Assert.Equal(
            [(March, "3250000.00"), (March.AddDays(1), "3250000.00"), (March.AddDays(2), "3260000.00"), (March.AddDays(3), "3160000.00")],
            report.Points.Select(point => (point.Date, point.Balance)));
        Assert.All(report.Points, point => Assert.Empty(point.NotConverted));
    }

    [Fact]
    public async Task A_balance_with_no_exchange_rate_on_or_before_the_day_is_left_out_and_listed_as_not_converted()
    {
        api.ExchangeRateSource.Quote(March.AddDays(1), dong: 25000m);
        await api.FetchExchangeRatesAsync();
        var (browser, _) = await api.SignedInAsync();
        await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        await AddAccountAsync(browser, "Dollars", "USD", "10");
        await AddAccountAsync(browser, "Euros", "EUR", "2.50");

        var report = await ReportAsync(browser, "from=2026-03-01&to=2026-03-02");

        Assert.Equal(
            [
                (March, "1000000.00", new[] { ("USD", "10.00"), ("EUR", "2.50") }),
                (March.AddDays(1), "1250000.00", new[] { ("EUR", "2.50") }),
            ],
            report.Points.Select(point => (point.Date, point.Balance, point.NotConverted.Select(amount => (amount.Currency, amount.Balance)).ToArray())),
            (one, other) => (one.Item1, one.Item2) == (other.Item1, other.Item2) && one.Item3.SequenceEqual(other.Item3));
    }

    [Fact]
    public async Task An_account_opened_during_the_period_counts_its_opening_balance_from_its_opening_date()
    {
        var (browser, _) = await api.SignedInAsync();
        var response = await browser.PostAsync("/api/v1/accounts", new { name = "New", type = "cash", currency = "VND", openingBalance = "300000", openingDate = "2026-03-02" });
        response.EnsureSuccessStatusCode();

        var report = await ReportAsync(browser, "from=2026-03-01&to=2026-03-02");

        Assert.Equal(["0.00", "300000.00"], report.Points.Select(point => point.Balance));
    }

    [Fact]
    public async Task An_account_the_user_does_not_have_reports_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var (stranger, _) = await api.SignedInAsync();
        await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var theirs = await AddAccountAsync(stranger, "Theirs", "USD", "500");

        var report = await ReportAsync(browser, $"from=2026-03-01&to=2026-03-02&accountId={theirs}");

        Assert.Equal("VND", report.Currency);
        Assert.Equal(["0.00", "0.00"], report.Points.Select(point => point.Balance));
    }

    [Theory]
    [InlineData("to=2026-03-31", "from")]
    [InlineData("from=2026-03-31&to=2026-03-01", "to")]
    [InlineData("from=2026-03-01&to=2026-03-31&accountId=everyday", "accountId")]
    [InlineData("from=2026-03-01&to=2026-03-31&interval=month", "interval")]
    public async Task A_report_asked_for_with_a_parameter_it_cannot_read_is_a_validation_problem_naming_it(string query, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Report}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], (await response.Content.ReadFromJsonAsync<ValidationProblemBody>())!.Errors.Keys);
    }

    [Fact]
    public async Task The_report_needs_a_signed_in_user() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateBrowser().GetAsync($"{Report}?from=2026-03-01&to=2026-03-31")).StatusCode);

    private static async Task<CashFlowBody> ReportAsync(Browser browser, string query)
    {
        var response = await browser.GetAsync($"{Report}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CashFlowBody>())!;
    }

    private sealed record ValidationProblemBody(Dictionary<string, string[]> Errors);

    private sealed record AmountBody(string Currency, string Balance);

    private sealed record PointBody(DateOnly Date, string Balance, List<AmountBody> NotConverted);

    private sealed record CashFlowBody(DateOnly From, DateOnly To, string Interval, string Currency, Guid? AccountId, List<PointBody> Points);
}
