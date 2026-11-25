using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>One category or one payee over time, with period-over-period change (spec section 7, RPT-7 and common behaviour).</summary>
[Collection(ApiCollection.Name)]
public class TrendsTests(PfmApi api) : IAsyncLifetime
{
    private const string Report = "/api/v1/reports/trends";

    private static readonly DateOnly January = new(2026, 1, 1);

    private static readonly DateOnly February = new(2026, 2, 1);

    private static readonly DateOnly March = new(2026, 3, 1);

    private const string FirstQuarter = "from=2026-01-01&to=2026-03-31";

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_category_shows_what_it_spent_each_month_with_its_subcategories_and_how_each_month_changed_from_the_one_before()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "50000000");
        var savings = await AddAccountAsync(browser, "Savings");
        var food = await CategoryAsync(browser, "Food & drink");
        await SpendAsync(browser, everyday, "Groceries", "400000", January);
        await SpendAsync(browser, everyday, "Coffee", "100000", January.AddDays(30));
        await SplitAsync(browser, everyday, "900000", March, ("Groceries", "600000"), ("Transport", "300000"));
        await RefundAsync(browser, everyday, "Food & drink", "200000", March.AddDays(4));
        await SpendAsync(browser, everyday, "Transport", "999000", February);
        await SpendAsync(browser, everyday, "Groceries", "999000", March.AddMonths(1));
        await TransferAsync(browser, everyday, savings, "999000", February);

        var trend = await TrendAsync(browser, $"{FirstQuarter}&categoryId={food}");

        Assert.Equal((January, March.AddMonths(1).AddDays(-1), "VND", "Food & drink", (string?)null, "900000.00", 0),
            (trend.From, trend.To, trend.Currency, trend.Category!.Name, trend.Payee, trend.Total, trend.NotConverted));
        Assert.Equal(
            [
                (January, new DateOnly(2026, 1, 31), "500000.00", (ChangeBody?)null),
                (February, new DateOnly(2026, 2, 28), "0.00", new ChangeBody("-500000.00", -100.0m)),
                (March, new DateOnly(2026, 3, 31), "400000.00", new ChangeBody("400000.00", null)),
            ],
            trend.Months.Select(month => (month.From, month.To, month.Total, month.Change)));
        Assert.Null(trend.Previous);
        Assert.Null(trend.Change);
    }

    [Fact]
    public async Task An_income_category_shows_the_income_received_in_it_each_month()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await RecordAsync(browser, "income", everyday, "Salary", "20000000", January);
        await RecordAsync(browser, "income", everyday, "Salary", "25000000", February);
        await SpendAsync(browser, everyday, "Groceries", "999000", February);

        var trend = await TrendAsync(browser, $"{FirstQuarter}&categoryId={await CategoryAsync(browser, "Salary")}");

        Assert.Equal(("45000000.00", true), (trend.Total, trend.IsIncome));
        Assert.Equal(
            [("20000000.00", (ChangeBody?)null), ("25000000.00", new ChangeBody("5000000.00", 25.0m)), ("0.00", new ChangeBody("-25000000.00", -100.0m))],
            trend.Months.Select(month => (month.Total, month.Change)));
    }

    [Fact]
    public async Task A_payee_shows_what_was_spent_with_it_each_month_in_any_category_and_in_any_case()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SpendAsync(browser, everyday, "Groceries", "300000", January, payee: "Big Market");
        await SpendAsync(browser, everyday, "Household", "100000", January.AddDays(3), payee: "big market");
        await SpendAsync(browser, everyday, "Groceries", "200000", February, payee: "BIG MARKET");
        await RecordAsync(browser, "expense", everyday, "Groceries", "50000", February.AddDays(1), isRefund: true, payee: "Big Market");
        await RecordAsync(browser, "income", everyday, "Salary", "999000", February, payee: "Big Market");
        await SpendAsync(browser, everyday, "Groceries", "999000", February, payee: "Big Market Mall");
        await SpendAsync(browser, everyday, "Groceries", "999000", February);

        var trend = await TrendAsync(browser, $"{FirstQuarter}&payee={Uri.EscapeDataString(" Big Market ")}");

        Assert.Equal(("Big Market", (object?)null, "550000.00"), (trend.Payee, (object?)trend.Category, trend.Total));
        Assert.Equal(["400000.00", "150000.00", "0.00"], trend.Months.Select(month => month.Total));
        Assert.Equal(new ChangeBody("-250000.00", -62.5m), trend.Months[1].Change);
    }

    [Fact]
    public async Task A_payee_with_the_category_filter_shows_only_what_was_spent_with_it_in_that_category()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SpendAsync(browser, everyday, "Groceries", "300000", January, payee: "Big Market");
        await SpendAsync(browser, everyday, "Household", "100000", January, payee: "Big Market");
        await SplitAsync(browser, everyday, "500000", February, ("Household", "200000"), ("Groceries", "300000"));

        var trend = await TrendAsync(browser, $"{FirstQuarter}&payee=Big%20Market&categoryId={await CategoryAsync(browser, "Household")}");

        Assert.Equal(("Big Market", "Household", "100000.00"), (trend.Payee, trend.Category!.Name, trend.Total));
    }

    [Fact]
    public async Task The_account_filter_counts_only_what_happened_on_that_account()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var card = await AddAccountAsync(browser, "Card");
        var transport = await CategoryAsync(browser, "Transport");
        await SpendAsync(browser, everyday, "Transport", "100000", January);
        await SpendAsync(browser, card, "Transport", "200000", January);

        Assert.Equal("200000.00", (await TrendAsync(browser, $"{FirstQuarter}&categoryId={transport}&accountId={card}")).Total);
        Assert.Equal("0.00", (await TrendAsync(browser, $"{FirstQuarter}&categoryId={transport}&accountId={Guid.NewGuid()}")).Total);
    }

    [Fact]
    public async Task A_transaction_that_is_not_converted_is_flagged_in_its_month_and_left_out_until_a_rate_arrives()
    {
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var transport = await CategoryAsync(browser, "Transport");
        await SpendAsync(browser, dollars, "Transport", "40", February);

        var waiting = await TrendAsync(browser, $"{FirstQuarter}&categoryId={transport}");

        Assert.Equal(("0.00", 1), (waiting.Total, waiting.NotConverted));
        Assert.Equal([0, 1, 0], waiting.Months.Select(month => month.NotConverted));

        api.ExchangeRateSource.Quote(January, 25000m);
        await api.FetchExchangeRatesAsync();

        var converted = await TrendAsync(browser, $"{FirstQuarter}&categoryId={transport}");
        Assert.Equal(("1000000.00", 0), (converted.Total, converted.NotConverted));
    }

    [Fact]
    public async Task A_period_that_does_not_start_on_the_first_starts_with_the_rest_of_that_month()
    {
        var (browser, _) = await api.SignedInAsync();

        var trend = await TrendAsync(browser, $"from=2026-01-15&to=2026-02-10&categoryId={await CategoryAsync(browser, "Transport")}");

        Assert.Equal(
            [(new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 31)), (February, new DateOnly(2026, 2, 10))],
            trend.Months.Select(month => (month.From, month.To)));
    }

    [Fact]
    public async Task Compared_with_the_previous_period_the_trend_shows_that_period_month_by_month_and_how_the_total_changed()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var transport = await CategoryAsync(browser, "Transport");
        await SpendAsync(browser, everyday, "Transport", "300000", March);
        await SpendAsync(browser, everyday, "Transport", "100000", February.AddDays(3));
        await SpendAsync(browser, everyday, "Transport", "300000", new DateOnly(2025, 12, 31));
        await SpendAsync(browser, everyday, "Transport", "999000", new DateOnly(2025, 11, 30));

        var trend = await TrendAsync(browser, $"from=2026-02-01&to=2026-03-31&categoryId={transport}&compare=true");

        Assert.Equal((new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 31), "300000.00", 0), (trend.Previous!.From, trend.Previous.To, trend.Previous.Total, trend.Previous.NotConverted));
        Assert.Equal(["300000.00", "0.00"], trend.Previous.Months.Select(month => month.Total));
        Assert.Equal(new ChangeBody("100000.00", 33.3m), trend.Change);
    }

    // What it refuses.

    [Theory]
    [InlineData("from=2026-01-01&to=2026-03-31", "categoryId")]
    [InlineData("to=2026-03-31&payee=Shop", "from")]
    [InlineData("from=2026-03-31&to=2026-03-01&payee=Shop", "to")]
    [InlineData("from=2026-01-01&to=2026-03-31&categoryId=food", "categoryId")]
    [InlineData("from=2026-01-01&to=2026-03-31&payee=Shop&accountId=everyday", "accountId")]
    [InlineData("from=2026-01-01&to=2026-03-31&payee=%20%20", "categoryId")]
    public async Task A_trend_asked_for_without_a_category_or_a_payee_or_with_a_parameter_it_cannot_read_is_a_validation_problem_naming_it(string query, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Report}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], (await response.Content.ReadFromJsonAsync<ValidationProblemBody>())!.Errors.Keys);
    }

    [Fact]
    public async Task A_payee_longer_than_any_transaction_can_have_is_refused()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Report}?{FirstQuarter}&payee={new string('a', 201)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["payee"], (await response.Content.ReadFromJsonAsync<ValidationProblemBody>())!.Errors.Keys);
    }

    [Fact]
    public async Task The_trend_of_a_category_the_user_does_not_have_is_not_found()
    {
        var (browser, _) = await api.SignedInAsync();
        var (stranger, _) = await api.SignedInAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Report}?{FirstQuarter}&categoryId={await CategoryAsync(stranger, "Transport")}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Report}?{FirstQuarter}&payee=Shop&categoryId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task The_report_needs_a_signed_in_user()
    {
        var response = await api.CreateBrowser().GetAsync($"{Report}?{FirstQuarter}&payee=Shop");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<TrendBody> TrendAsync(Browser browser, string query)
    {
        var response = await browser.GetAsync($"{Report}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TrendBody>())!;
    }

    private sealed record ValidationProblemBody(Dictionary<string, string[]> Errors);

    private sealed record CategoryBody(Guid Id, Guid? ParentId, string Name, string Icon, string Color, bool IsArchived);

    private sealed record ChangeBody(string Amount, decimal? Percent);

    private sealed record MonthBody(DateOnly From, DateOnly To, string Total, int NotConverted, ChangeBody? Change);

    private sealed record PreviousBody(DateOnly From, DateOnly To, string Total, int NotConverted, List<MonthBody> Months);

    private sealed record TrendBody(
        DateOnly From, DateOnly To, string Currency, CategoryBody? Category, string? Payee, bool IsIncome, string Total, int NotConverted, List<MonthBody> Months,
        PreviousBody? Previous, ChangeBody? Change);
}
