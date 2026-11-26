using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Am I saving: income, expenses and net per month over a report period (spec section 7, RPT-3 and common behaviour).</summary>
[Collection(ApiCollection.Name)]
public class IncomeVsExpensesTests(PfmApi api) : IAsyncLifetime
{
    private const string Report = "/api/v1/reports/income-vs-expenses";

    private static readonly DateOnly January = new(2026, 1, 1);

    private static readonly DateOnly February = new(2026, 2, 1);

    private static readonly DateOnly March = new(2026, 3, 1);

    private const string FirstQuarter = "from=2026-01-01&to=2026-03-31";

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Each_month_shows_its_income_expenses_net_and_savings_rate_and_so_does_the_whole_period()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await RecordAsync(browser, "income", everyday, "Salary", "20000000", January);
        await SpendAsync(browser, everyday, "Rent", "15000000", January.AddDays(4));
        await RecordAsync(browser, "income", everyday, "Salary", "10000000", March.AddDays(30));
        await SpendAsync(browser, everyday, "Groceries", "12500000", March);
        await RecordAsync(browser, "income", everyday, "Salary", "99000000", March.AddMonths(1));

        var report = await ReportAsync(browser, FirstQuarter);

        Assert.Equal((January, new DateOnly(2026, 3, 31), "VND"), (report.From, report.To, report.Currency));
        Assert.Equal(
            [
                (January, "20000000.00", "15000000.00", "5000000.00", (decimal?)25.0m),
                (February, "0.00", "0.00", "0.00", null),
                (March, "10000000.00", "12500000.00", "-2500000.00", -25.0m),
            ],
            report.Months.Select(month => (month.Month, month.Income, month.Expenses, month.Net, month.SavingsRate)));
        Assert.Equal(new FlowBody("30000000.00", "27500000.00", "2500000.00", 8.3m, 0), report.Total);
        Assert.Null(report.Previous);
    }

    [Fact]
    public async Task Transfers_never_count_refunds_lower_expenses_and_a_partial_month_covers_only_its_days_in_the_period()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "50000000");
        var savings = await AddAccountAsync(browser, "Savings");
        await TransferAsync(browser, everyday, savings, "20000000", March.AddDays(9));
        await RecordAsync(browser, "income", everyday, "Salary", "10000000", March.AddDays(9));
        await SpendAsync(browser, everyday, "Clothes", "800000", March.AddDays(10));
        await RefundAsync(browser, everyday, "Clothes", "300000", March.AddDays(11));
        await SpendAsync(browser, everyday, "Clothes", "999000", March);

        var report = await ReportAsync(browser, "from=2026-03-05&to=2026-03-20");

        var month = Assert.Single(report.Months);
        Assert.Equal((March, new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 20)), (month.Month, month.From, month.To));
        Assert.Equal(("10000000.00", "500000.00", "9500000.00", (decimal?)95.0m), (month.Income, month.Expenses, month.Net, month.SavingsRate));
    }

    [Fact]
    public async Task What_is_not_converted_is_counted_and_left_out_until_a_rate_arrives()
    {
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var everyday = await AddAccountAsync(browser, "Everyday");
        await RecordAsync(browser, "income", dollars, "Salary", "1000", March);
        await SpendAsync(browser, everyday, "Groceries", "5000000", March);

        var waiting = await ReportAsync(browser, "from=2026-03-01&to=2026-03-31");

        Assert.Equal(new FlowBody("0.00", "5000000.00", "-5000000.00", null, 1), waiting.Total);
        Assert.Equal(1, Assert.Single(waiting.Months).NotConverted);

        api.ExchangeRateSource.Quote(March.AddDays(-1), 25000m);
        await api.FetchExchangeRatesAsync();

        var converted = await ReportAsync(browser, "from=2026-03-01&to=2026-03-31");
        Assert.Equal(new FlowBody("25000000.00", "5000000.00", "20000000.00", 80.0m, 0), converted.Total);
    }

    [Fact]
    public async Task The_account_filter_counts_only_that_account_and_the_category_filter_only_that_category_and_its_subcategories()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var card = await AddAccountAsync(browser, "Card");
        await RecordAsync(browser, "income", everyday, "Salary", "20000000", March);
        await SpendAsync(browser, card, "Transport", "200000", March);
        await SplitAsync(browser, everyday, "900000", March, ("Groceries", "500000"), ("Coffee", "100000"), ("Household", "300000"));

        var onCard = await ReportAsync(browser, $"from=2026-03-01&to=2026-03-31&accountId={card}");
        var food = await ReportAsync(browser, $"from=2026-03-01&to=2026-03-31&categoryId={await CategoryAsync(browser, "Food & drink")}");
        var salary = await ReportAsync(browser, $"from=2026-03-01&to=2026-03-31&categoryId={await CategoryAsync(browser, "Salary")}");

        Assert.Equal(("0.00", "200000.00"), (onCard.Total.Income, onCard.Total.Expenses));
        Assert.Equal(("0.00", "600000.00"), (food.Total.Income, food.Total.Expenses));
        Assert.Equal(("20000000.00", "0.00"), (salary.Total.Income, salary.Total.Expenses));
        Assert.Equal("0.00", (await ReportAsync(browser, $"from=2026-03-01&to=2026-03-31&accountId={Guid.NewGuid()}")).Total.Income);
    }

    [Fact]
    public async Task Compared_with_the_previous_period_it_shows_that_periods_months_and_total()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await RecordAsync(browser, "income", everyday, "Salary", "10000000", March);
        await RecordAsync(browser, "income", everyday, "Salary", "8000000", February);
        await SpendAsync(browser, everyday, "Rent", "6000000", February.AddDays(27));
        await SpendAsync(browser, everyday, "Rent", "999000", January);

        var report = await ReportAsync(browser, "from=2026-03-01&to=2026-03-31&compare=true");

        var previous = report.Previous!;
        Assert.Equal((February, new DateOnly(2026, 2, 28)), (previous.From, previous.To));
        Assert.Equal(new FlowBody("8000000.00", "6000000.00", "2000000.00", 25.0m, 0), previous.Total);
        Assert.Equal([February], previous.Months.Select(month => month.Month));
    }

    [Theory]
    [InlineData("to=2026-03-31", "from")]
    [InlineData("from=2026-03-31&to=2026-03-01", "to")]
    [InlineData("from=2026-03-01&to=2026-03-31&accountId=everyday", "accountId")]
    [InlineData("from=2026-03-01&to=2026-03-31&categoryId=food", "categoryId")]
    public async Task A_report_asked_for_with_a_parameter_it_cannot_read_is_a_validation_problem_naming_it(string query, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Report}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], (await response.Content.ReadFromJsonAsync<ValidationProblemBody>())!.Errors.Keys);
    }

    [Fact]
    public async Task The_report_needs_a_signed_in_user() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateBrowser().GetAsync($"{Report}?{FirstQuarter}")).StatusCode);

    private static async Task<ReportBody> ReportAsync(Browser browser, string query)
    {
        var response = await browser.GetAsync($"{Report}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReportBody>())!;
    }

    private sealed record ValidationProblemBody(Dictionary<string, string[]> Errors);

    private sealed record FlowBody(string Income, string Expenses, string Net, decimal? SavingsRate, int NotConverted);

    private sealed record MonthBody(DateOnly Month, DateOnly From, DateOnly To, string Income, string Expenses, string Net, decimal? SavingsRate, int NotConverted);

    private sealed record PreviousBody(DateOnly From, DateOnly To, FlowBody Total, List<MonthBody> Months);

    private sealed record ReportBody(DateOnly From, DateOnly To, string Currency, FlowBody Total, List<MonthBody> Months, PreviousBody? Previous);
}
