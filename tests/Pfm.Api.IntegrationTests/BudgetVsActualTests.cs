using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Budgeted against spent per category, for one budget month or summed across several (spec section 7, RPT-6).</summary>
[Collection(ApiCollection.Name)]
public class BudgetVsActualTests(PfmApi api) : IAsyncLifetime
{
    private const string Report = "/api/v1/reports/budget-vs-actual";

    private static readonly DateOnly March = new(2026, 3, 1);

    private static readonly DateOnly February = new(2026, 2, 1);

    private const string InMarch = "from=2026-03-01&to=2026-03-31";

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_month_shows_each_budget_against_what_was_spent_in_its_category_and_subcategories_with_totals()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await BudgetAsync(browser, "2026-03", "Food & drink", "3000000");
        await BudgetAsync(browser, "2026-03", "Transport", "500000");
        await SpendAsync(browser, everyday, "Groceries", "1200000", March);
        await SpendAsync(browser, everyday, "Food & drink", "300000", March.AddDays(4));
        await SpendAsync(browser, everyday, "Transport", "450000", March.AddDays(30));
        await SpendAsync(browser, everyday, "Transport", "999000", February);

        var report = await ReportAsync(browser, InMarch);

        Assert.Equal((new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 1, "VND", 80), (report.From, report.To, report.Months, report.Currency, report.WarningPercent));
        Assert.Equal(
            [("Food & drink", 1, "3000000.00", "1500000.00", "1500000.00", (int?)50, "onTrack"), ("Transport", 1, "500000.00", "450000.00", "50000.00", 90, "warning")],
            report.Items.Select(item => (item.Category.Name, item.Months, item.Budgeted, item.Spent, item.Remaining, item.PercentSpent, item.State)));
        Assert.Equal(new TotalsBody("3500000.00", "1950000.00", "1550000.00", 0), report.Totals);
        Assert.Empty(report.Unbudgeted);
        Assert.Equal(new UnbudgetedTotalBody("0.00", 0), report.UnbudgetedTotal);
    }

    [Fact]
    public async Task The_overall_budget_is_not_a_category_and_covers_no_categorys_spending()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await BudgetAsync(browser, "2026-03", "Transport", "500000");
        (await browser.PutAsync($"/api/v1/budgets/2026-03/overall", new { amount = "10000000" })).EnsureSuccessStatusCode();
        await SpendAsync(browser, everyday, "Transport", "100000", March);
        await SpendAsync(browser, everyday, "Coffee", "150000", March);

        var report = await ReportAsync(browser, InMarch);

        Assert.Equal(["Transport"], report.Items.Select(item => item.Category.Name));
        Assert.Equal(new TotalsBody("500000.00", "100000.00", "400000.00", 0), report.Totals);
        Assert.Equal(["Coffee"], report.Unbudgeted.Select(item => item.Category.Name));
    }

    [Fact]
    public async Task Across_a_range_of_months_each_categorys_budgets_and_spent_are_summed()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await BudgetAsync(browser, "2026-02", "Transport", "500000");
        await BudgetAsync(browser, "2026-03", "Transport", "500000");
        await SpendAsync(browser, everyday, "Transport", "700000", February);
        await SpendAsync(browser, everyday, "Transport", "400000", March);

        var report = await ReportAsync(browser, "from=2026-02-01&to=2026-03-31");

        Assert.Equal(2, report.Months);
        Assert.Equal([("Transport", 2, "1000000.00", "1100000.00", "-100000.00", (int?)110, "over")],
            report.Items.Select(item => (item.Category.Name, item.Months, item.Budgeted, item.Spent, item.Remaining, item.PercentSpent, item.State)));
    }

    [Fact]
    public async Task A_period_covers_the_whole_budget_months_it_touches()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await BudgetAsync(browser, "2026-03", "Transport", "500000");
        await SpendAsync(browser, everyday, "Transport", "100000", March);

        var report = await ReportAsync(browser, "from=2026-03-10&to=2026-03-20");

        Assert.Equal((new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)), (report.From, report.To));
        Assert.Equal("100000.00", Assert.Single(report.Items).Spent);
    }

    [Fact]
    public async Task Categories_with_spending_and_no_budget_are_listed_separately()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await BudgetAsync(browser, "2026-03", "Groceries", "2000000");
        await BudgetAsync(browser, "2026-02", "Transport", "500000");
        await SpendAsync(browser, everyday, "Groceries", "800000", March);
        await SpendAsync(browser, everyday, "Coffee", "150000", March);
        await SpendAsync(browser, everyday, "Transport", "300000", February);
        await SpendAsync(browser, everyday, "Transport", "200000", March);
        await RecordAsync(browser, "income", everyday, "Salary", "30000000", March);

        var report = await ReportAsync(browser, "from=2026-02-01&to=2026-03-31");

        Assert.Equal([("Groceries", "800000.00"), ("Transport", "300000.00")], report.Items.Select(item => (item.Category.Name, item.Spent)));
        Assert.Equal([("Transport", "200000.00"), ("Coffee", "150000.00")], report.Unbudgeted.Select(item => (item.Category.Name, item.Spent)));
        Assert.Equal(new UnbudgetedTotalBody("350000.00", 0), report.UnbudgetedTotal);
    }

    [Fact]
    public async Task Expenses_that_are_not_converted_are_counted_and_left_out_of_spent()
    {
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        await BudgetAsync(browser, "2026-03", "Food & drink", "3000000");
        await SplitAsync(browser, dollars, "10", March, ("Groceries", "4"), ("Transport", "6"));

        var report = await ReportAsync(browser, InMarch);

        Assert.Equal(("0.00", 1), (report.Items[0].Spent, report.Items[0].NotConverted));
        Assert.Equal(1, report.Totals.NotConverted);
        Assert.Equal(("Transport", 1), (report.Unbudgeted[0].Category.Name, report.Unbudgeted[0].NotConverted));
        Assert.Equal(1, report.UnbudgetedTotal.NotConverted);
    }

    // Filters (spec section 7, common behaviour).

    [Fact]
    public async Task The_account_filter_counts_only_the_spending_on_that_account()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var card = await AddAccountAsync(browser, "Card");
        await BudgetAsync(browser, "2026-03", "Transport", "500000");
        await SpendAsync(browser, everyday, "Transport", "100000", March);
        await SpendAsync(browser, card, "Transport", "200000", March);

        var report = await ReportAsync(browser, $"{InMarch}&accountId={card}");

        Assert.Equal(("500000.00", "200000.00"), (report.Items[0].Budgeted, report.Items[0].Spent));
    }

    [Fact]
    public async Task The_category_filter_shows_only_that_category_and_its_subcategories()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await BudgetAsync(browser, "2026-03", "Groceries", "2000000");
        await BudgetAsync(browser, "2026-03", "Transport", "500000");
        await SpendAsync(browser, everyday, "Coffee", "150000", March);
        await SpendAsync(browser, everyday, "Fuel", "100000", March);

        var report = await ReportAsync(browser, $"{InMarch}&categoryId={await CategoryAsync(browser, "Food & drink")}");

        Assert.Equal(["Groceries"], report.Items.Select(item => item.Category.Name));
        Assert.Equal(["Coffee"], report.Unbudgeted.Select(item => item.Category.Name));
        Assert.Equal("2000000.00", report.Totals.Budgeted);
    }

    [Fact]
    public async Task The_category_filter_on_a_subcategory_shows_the_budget_of_the_category_it_sits_under()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await BudgetAsync(browser, "2026-03", "Food & drink", "3000000");
        await BudgetAsync(browser, "2026-03", "Transport", "500000");
        await SpendAsync(browser, everyday, "Groceries", "1200000", March);
        await SpendAsync(browser, everyday, "Fuel", "100000", March);

        var report = await ReportAsync(browser, $"{InMarch}&categoryId={await CategoryAsync(browser, "Groceries")}");

        Assert.Equal([("Food & drink", "3000000.00", "1200000.00")], report.Items.Select(item => (item.Category.Name, item.Budgeted, item.Spent)));
        Assert.Empty(report.Unbudgeted);
    }

    // What it refuses.

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
    public async Task The_report_needs_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateBrowser().GetAsync($"{Report}?{InMarch}")).StatusCode);
    }

    private static async Task BudgetAsync(Browser browser, string month, string category, string amount)
    {
        var response = await browser.PutAsync($"/api/v1/budgets/{month}/{await CategoryAsync(browser, category)}", new { amount });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<ReportBody> ReportAsync(Browser browser, string query)
    {
        var response = await browser.GetAsync($"{Report}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReportBody>())!;
    }

    private sealed record ValidationProblemBody(Dictionary<string, string[]> Errors);

    private sealed record CategoryBody(Guid Id, Guid? ParentId, string Name, string Icon, string Color, bool IsArchived);

    private sealed record ItemBody(CategoryBody Category, int Months, string Budgeted, string Spent, string Remaining, int? PercentSpent, string State, int NotConverted);

    private sealed record UnbudgetedBody(CategoryBody Category, string Spent, int NotConverted);

    private sealed record TotalsBody(string Budgeted, string Spent, string Remaining, int NotConverted);

    private sealed record UnbudgetedTotalBody(string Spent, int NotConverted);

    private sealed record ReportBody(
        DateOnly From, DateOnly To, int Months, string Currency, int WarningPercent, List<ItemBody> Items, TotalsBody Totals, List<UnbudgetedBody> Unbudgeted,
        UnbudgetedTotalBody UnbudgetedTotal);
}
