using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Where the money went: expenses per category over a report period (spec section 7, RPT-2 and common behaviour).</summary>
[Collection(ApiCollection.Name)]
public class SpendingByCategoryTests(PfmApi api) : IAsyncLifetime
{
    private const string Report = "/api/v1/reports/spending-by-category";

    private static readonly DateOnly March = new(2026, 3, 1);

    private static readonly DateOnly February = new(2026, 2, 1);

    private const string InMarch = "from=2026-03-01&to=2026-03-31";

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_report_shows_the_expense_total_and_each_top_level_categorys_spending_and_share_ranked_in_the_base_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SpendAsync(browser, everyday, "Groceries", "500000", March);
        await SpendAsync(browser, everyday, "Coffee", "100000", March.AddDays(30));
        await SpendAsync(browser, everyday, "Transport", "200000", March.AddDays(10));
        await SpendAsync(browser, everyday, "Personal care", "200000", March.AddDays(5));
        await SpendAsync(browser, everyday, "Rent", "9000000", February);

        var report = await ReportAsync(browser, InMarch);

        Assert.Equal((new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "VND", "1000000.00", 0), (report.From, report.To, report.Currency, report.Total, report.NotConverted));
        Assert.Null(report.Parent);
        Assert.Null(report.Previous);
        Assert.Equal(
            [("Food & drink", "600000.00", 60.0m, "subcategories"), ("Personal care", "200000.00", 20.0m, "transactions"), ("Transport", "200000.00", 20.0m, "transactions")],
            report.Items.Select(item => (item.Category.Name, item.Spent, item.Share!.Value, item.DrillsInto)));
        var food = report.Items[0].Category;
        Assert.Equal((await CategoryAsync(browser, "Food & drink"), (Guid?)null, "utensils", "#f97316", false), (food.Id, food.ParentId, food.Icon, food.Color, food.IsArchived));
    }

    // Drilling down (RPT-2: categories, then subcategories, then transactions).

    [Fact]
    public async Task A_top_level_category_breaks_down_into_its_subcategories_and_what_was_spent_on_it_directly()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var food = await CategoryAsync(browser, "Food & drink");
        await SpendAsync(browser, everyday, "Groceries", "500000", March);
        await SpendAsync(browser, everyday, "Coffee", "100000", March);
        await SpendAsync(browser, everyday, "Food & drink", "400000", March);
        await SpendAsync(browser, everyday, "Fuel", "999000", March);

        var report = await ReportAsync(browser, $"{InMarch}&parentId={food}");

        Assert.Equal(("Food & drink", "1000000.00"), (report.Parent!.Name, report.Total));
        Assert.Equal(
            [("Groceries", "500000.00", 50.0m, "transactions"), ("Food & drink", "400000.00", 40.0m, "transactions"), ("Coffee", "100000.00", 10.0m, "transactions")],
            report.Items.Select(item => (item.Category.Name, item.Spent, item.Share!.Value, item.DrillsInto)));
        Assert.All(report.Items.Where(item => item.Category.Name != "Food & drink"), item => Assert.Equal(food, item.Category.ParentId));
    }

    [Fact]
    public async Task Breaking_down_a_category_the_user_does_not_have_is_not_found()
    {
        var (browser, _) = await api.SignedInAsync();
        var (stranger, _) = await api.SignedInAsync();
        var theirs = await CategoryAsync(stranger, "Food & drink");

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Report}?{InMarch}&parentId={theirs}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Report}?{InMarch}&parentId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task A_category_drills_down_to_the_transactions_that_make_up_its_spending_each_with_what_it_spent_there()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var groceries = await CategoryAsync(browser, "Groceries");
        var market = await SpendAsync(browser, everyday, "Groceries", "500000", March, payee: "Market");
        var split = await SplitAsync(browser, everyday, "150000", March.AddDays(2), ("Groceries", "100000"), ("Household", "50000"));
        var refund = await RefundAsync(browser, everyday, "Groceries", "30000", March.AddDays(2));
        var abroad = await SpendAsync(browser, dollars, "Groceries", "12.5", March.AddDays(1));
        await SpendAsync(browser, everyday, "Food & drink", "999000", March);
        await SpendAsync(browser, everyday, "Groceries", "999000", February);

        var list = await TransactionsAsync(browser, $"{InMarch}&categoryId={groceries}");

        Assert.Equal(("Groceries", "VND", "570000.00", 1), (list.Category.Name, list.Currency, list.Total, list.NotConverted));
        Assert.Equal(
            [
                (refund, new DateOnly(2026, 3, 3), "30000.00", "VND", true, false, (string?)"-30000.00"),
                (split, new DateOnly(2026, 3, 3), "100000.00", "VND", false, true, "100000.00"),
                (abroad, new DateOnly(2026, 3, 2), "12.50", "USD", false, false, null),
                (market, new DateOnly(2026, 3, 1), "500000.00", "VND", false, false, "500000.00"),
            ],
            list.Items.Select(item => (item.Id, item.Date, item.Amount, item.Currency, item.IsRefund, item.IsSplit, item.Spent)));
        Assert.Equal(("Market", everyday), (list.Items[3].Payee, list.Items[3].AccountId));
        Assert.Equal((await ReportAsync(browser, $"{InMarch}&parentId={await CategoryAsync(browser, "Food & drink")}")).Items.Single(item => item.Category.Id == groceries).Spent, list.Total);
    }

    [Fact]
    public async Task The_transactions_of_a_category_follow_the_account_filter_and_a_category_the_user_does_not_have_is_not_found()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var card = await AddAccountAsync(browser, "Card");
        var transport = await CategoryAsync(browser, "Transport");
        await SpendAsync(browser, everyday, "Transport", "100000", March);
        var onCard = await SpendAsync(browser, card, "Transport", "200000", March);

        var list = await TransactionsAsync(browser, $"{InMarch}&categoryId={transport}&accountId={card}");

        Assert.Equal((onCard, "200000.00"), (Assert.Single(list.Items).Id, list.Total));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Report}/transactions?{InMarch}&categoryId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync($"{Report}/transactions?{InMarch}")).StatusCode);
    }

    // What counts (spec section 3, rules 2, 3 and 5).

    [Fact]
    public async Task Transfers_and_income_never_count_refunds_lower_their_category_and_each_line_of_a_split_counts_in_its_own()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "50000000");
        var savings = await AddAccountAsync(browser, "Savings");
        await TransferAsync(browser, everyday, savings, "20000000", March);
        await RecordAsync(browser, "income", everyday, "Salary", "30000000", March);
        await SpendAsync(browser, everyday, "Clothes", "800000", March);
        await RefundAsync(browser, everyday, "Clothes", "300000", March);
        await SplitAsync(browser, everyday, "900000", March, ("Groceries", "500000"), ("Household", "250000"), ("Transport", "150000"));

        var report = await ReportAsync(browser, InMarch);

        Assert.Equal("1400000.00", report.Total);
        Assert.Equal(
            [("Shopping", "750000.00"), ("Food & drink", "500000.00"), ("Transport", "150000.00")],
            report.Items.Select(item => (item.Category.Name, item.Spent)));
    }

    [Fact]
    public async Task A_category_where_more_came_back_than_was_spent_shows_below_zero_and_with_nothing_spent_there_are_no_shares()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SpendAsync(browser, everyday, "Transport", "100000", March);
        await RefundAsync(browser, everyday, "Clothes", "300000", March);

        var report = await ReportAsync(browser, InMarch);

        Assert.Equal("-200000.00", report.Total);
        Assert.Equal([("Transport", "100000.00", (decimal?)null), ("Shopping", "-300000.00", null)], report.Items.Select(item => (item.Category.Name, item.Spent, item.Share)));
    }

    [Fact]
    public async Task An_expense_that_is_not_converted_is_flagged_and_left_out_of_the_total_until_a_rate_arrives()
    {
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SpendAsync(browser, everyday, "Groceries", "250000", March);
        await SpendAsync(browser, dollars, "Groceries", "40", March);
        await SplitAsync(browser, dollars, "10", March, ("Coffee", "4"), ("Transport", "6"));

        var waiting = await ReportAsync(browser, InMarch);

        Assert.Equal(("250000.00", 2), (waiting.Total, waiting.NotConverted));
        Assert.Equal([("Food & drink", "250000.00", 2), ("Transport", "0.00", 1)], waiting.Items.Select(item => (item.Category.Name, item.Spent, item.NotConverted)));

        api.ExchangeRateSource.Quote(March.AddDays(-1), 25000m);
        await api.FetchExchangeRatesAsync();

        var converted = await ReportAsync(browser, InMarch);
        Assert.Equal(("1500000.00", 0), (converted.Total, converted.NotConverted));
        Assert.Equal([("Food & drink", "1350000.00", 0), ("Transport", "150000.00", 0)], converted.Items.Select(item => (item.Category.Name, item.Spent, item.NotConverted)));
    }

    // Filters (spec section 7, common behaviour).

    [Fact]
    public async Task The_account_filter_counts_only_the_expenses_on_that_account()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var card = await AddAccountAsync(browser, "Card");
        await SpendAsync(browser, everyday, "Groceries", "500000", March);
        await SpendAsync(browser, card, "Transport", "200000", March);

        var report = await ReportAsync(browser, $"{InMarch}&accountId={card}");

        Assert.Equal(("200000.00", "Transport"), (report.Total, Assert.Single(report.Items).Category.Name));
        Assert.Empty((await ReportAsync(browser, $"{InMarch}&accountId={Guid.NewGuid()}")).Items);
    }

    [Fact]
    public async Task The_category_filter_counts_only_that_category_and_its_subcategories()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SpendAsync(browser, everyday, "Groceries", "500000", March);
        await SpendAsync(browser, everyday, "Coffee", "100000", March);
        await SpendAsync(browser, everyday, "Transport", "200000", March);

        var food = await ReportAsync(browser, $"{InMarch}&categoryId={await CategoryAsync(browser, "Food & drink")}");
        var coffee = await ReportAsync(browser, $"{InMarch}&categoryId={await CategoryAsync(browser, "Coffee")}");

        Assert.Equal([("Food & drink", "600000.00")], food.Items.Select(item => (item.Category.Name, item.Spent)));
        Assert.Equal([("Food & drink", "100000.00", "subcategories")], coffee.Items.Select(item => (item.Category.Name, item.Spent, item.DrillsInto)));
        Assert.Equal("100000.00", coffee.Total);
    }

    // Comparing with the previous period (spec section 7, common behaviour).

    [Fact]
    public async Task Compared_with_the_previous_period_each_category_says_what_it_spent_then_and_one_that_spent_only_then_is_listed_at_zero()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SpendAsync(browser, everyday, "Groceries", "500000", March);
        await SpendAsync(browser, everyday, "Groceries", "400000", February);
        await SpendAsync(browser, everyday, "Transport", "300000", February.AddDays(27));
        await SpendAsync(browser, everyday, "Transport", "999000", February.AddDays(-1));

        var report = await ReportAsync(browser, $"{InMarch}&compare=true");

        Assert.Equal(new PreviousBody(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), "700000.00", 0), report.Previous);
        Assert.Equal(
            [("Food & drink", "500000.00", "400000.00"), ("Transport", "0.00", "300000.00")],
            report.Items.Select(item => (item.Category.Name, item.Spent, item.PreviousSpent!)));
        Assert.Equal(0m, report.Items[1].Share);
    }

    [Fact]
    public async Task A_period_that_is_not_whole_months_is_compared_with_as_many_days_before_it()
    {
        var (browser, _) = await api.SignedInAsync();

        var report = await ReportAsync(browser, "from=2026-03-05&to=2026-03-14&compare=true");

        Assert.Equal((new DateOnly(2026, 2, 23), new DateOnly(2026, 3, 4)), (report.Previous!.From, report.Previous.To));
    }

    // What it refuses.

    [Theory]
    [InlineData("to=2026-03-31", "from")]
    [InlineData("from=2026-03-01", "to")]
    [InlineData("from=2026-3-1&to=2026-03-31", "from")]
    [InlineData("from=2026-03-31&to=2026-03-01", "to")]
    [InlineData("from=1899-12-31&to=2026-03-01", "from")]
    [InlineData("from=2026-03-01&to=2026-03-31&accountId=everyday", "accountId")]
    [InlineData("from=2026-03-01&to=2026-03-31&categoryId=food", "categoryId")]
    [InlineData("from=2026-03-01&to=2026-03-31&parentId=food", "parentId")]
    public async Task A_report_asked_for_with_a_parameter_it_cannot_read_is_a_validation_problem_naming_it(string query, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Report}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemBody>())!;
        Assert.Equal([field], problem.Errors.Keys);
    }

    [Fact]
    public async Task The_report_needs_a_signed_in_user()
    {
        var response = await api.CreateBrowser().GetAsync($"{Report}?{InMarch}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record ValidationProblemBody(Dictionary<string, string[]> Errors);

    private static async Task<ReportBody> ReportAsync(Browser browser, string query)
    {
        var response = await browser.GetAsync($"{Report}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReportBody>())!;
    }

    private static async Task<TransactionsBody> TransactionsAsync(Browser browser, string query)
    {
        var response = await browser.GetAsync($"{Report}/transactions?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionsBody>())!;
    }

    private sealed record TransactionBody(
        Guid Id, DateOnly Date, Guid AccountId, string? Payee, string? Note, string Amount, string Currency, bool IsRefund, bool IsSplit, string? Spent);

    private sealed record TransactionsBody(DateOnly From, DateOnly To, string Currency, CategoryBody Category, string Total, int NotConverted, List<TransactionBody> Items);

    private sealed record CategoryBody(Guid Id, Guid? ParentId, string Name, string Icon, string Color, bool IsArchived);

    private sealed record ItemBody(CategoryBody Category, string Spent, decimal? Share, int NotConverted, string DrillsInto, string? PreviousSpent);

    private sealed record PreviousBody(DateOnly From, DateOnly To, string Total, int NotConverted);

    private sealed record ReportBody(
        DateOnly From, DateOnly To, string Currency, CategoryBody? Parent, string Total, int NotConverted, List<ItemBody> Items, PreviousBody? Previous);
}
