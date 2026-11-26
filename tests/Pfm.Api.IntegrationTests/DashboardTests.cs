using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>The state of the user's money on one screen (spec section 7, RPT-1), answered by one aggregate endpoint.</summary>
[Collection(ApiCollection.Name)]
public class DashboardTests(PfmApi api) : IAsyncLifetime
{
    private const string Dashboard = "/api/v1/reports/dashboard";

    /// <summary>Today where a new user is, in the time zone they start with (Asia/Ho_Chi_Minh), not in UTC.</summary>
    private static DateOnly Today => TodayIn("Asia/Ho_Chi_Minh");

    private static DateOnly ThisMonth => new(Today.Year, Today.Month, 1);

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_new_user_has_no_accounts_and_nothing_to_show_for_this_month()
    {
        var (browser, _) = await api.SignedInAsync();

        var dashboard = await DashboardAsync(browser);

        Assert.False(dashboard.HasAccounts);
        Assert.Equal((ThisMonth, ThisMonth.AddMonths(1).AddDays(-1), "VND"), (dashboard.From, dashboard.To, dashboard.Currency));
        Assert.Equal(new BalanceBody("0.00", []), dashboard.Balance);
        Assert.Equal(new FlowBody("0.00", "0.00", "0.00", null, 0), dashboard.ThisMonth);
        Assert.Empty(dashboard.TopCategories);
        Assert.Empty(dashboard.RecentTransactions);
        Assert.Empty(dashboard.Upcoming);
    }

    [Fact]
    public async Task This_month_is_the_month_it_is_in_the_users_time_zone()
    {
        var (browser, _) = await api.SignedInAsync();
        (await browser.PatchAsync("/api/v1/me", new { timeZone = "Pacific/Kiritimati" })).EnsureSuccessStatusCode();
        var today = TodayIn("Pacific/Kiritimati");
        var first = new DateOnly(today.Year, today.Month, 1);

        var dashboard = await DashboardAsync(browser);

        Assert.Equal((first, first.AddMonths(1).AddDays(-1)), (dashboard.From, dashboard.To));
    }

    [Fact]
    public async Task It_shows_the_balance_of_every_account_not_archived_and_this_months_income_expenses_and_net()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        await AddAccountAsync(browser, "Savings", "VND", "5000000");
        var closed = await AddAccountAsync(browser, "Closed", "VND", "700000");
        (await browser.DeleteAsync($"/api/v1/accounts/{closed}")).EnsureSuccessStatusCode();
        await RecordAsync(browser, "income", everyday, "Salary", "20000000", ThisMonth);
        await SpendAsync(browser, everyday, "Groceries", "5000000", ThisMonth);
        await SpendAsync(browser, everyday, "Rent", "9000000", ThisMonth.AddDays(-1));

        var dashboard = await DashboardAsync(browser);

        Assert.True(dashboard.HasAccounts);
        Assert.Equal(new BalanceBody("12000000.00", []), dashboard.Balance);
        Assert.Equal(new FlowBody("20000000.00", "5000000.00", "15000000.00", 75.0m, 0), dashboard.ThisMonth);
    }

    [Fact]
    public async Task A_balance_with_no_exchange_rate_to_convert_it_is_left_out_of_the_balance_and_named()
    {
        var (browser, _) = await api.SignedInAsync();
        await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        await AddAccountAsync(browser, "In USD", "USD", "250");

        Assert.Equal(new BalanceBody("1000000.00", [new NotConvertedBody("USD", "250.00")]), (await DashboardAsync(browser)).Balance);
    }

    [Fact]
    public async Task Budget_status_adds_up_this_months_budgets_counts_each_state_and_puts_the_ones_over_and_at_warning_first()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var month = ThisMonth.ToString("yyyy-MM");
        await BudgetAsync(browser, month, "Food & drink", "1000000");
        await BudgetAsync(browser, month, "Transport", "1000000");
        await BudgetAsync(browser, month, "Shopping", "1000000");
        await BudgetAsync(browser, month, "Health", "1000000");
        await BudgetAsync(browser, ThisMonth.AddMonths(-1).ToString("yyyy-MM"), "Education", "1000000");
        await SpendAsync(browser, everyday, "Groceries", "850000", ThisMonth);
        await SpendAsync(browser, everyday, "Transport", "1200000", ThisMonth);
        await SpendAsync(browser, everyday, "Clothes", "900000", ThisMonth);
        await SpendAsync(browser, everyday, "Health", "100000", ThisMonth);

        var budgets = (await DashboardAsync(browser)).Budgets!;

        Assert.Equal(("4000000.00", "3050000.00", "950000.00", (int?)76), (budgets.Budgeted, budgets.Spent, budgets.Remaining, budgets.PercentSpent));
        Assert.Equal((1, 2, 1, 0), (budgets.OnTrack, budgets.Warning, budgets.Over, budgets.NotConverted));
        Assert.Equal(
            [("Transport", "over", (int?)120), ("Shopping", "warning", 90), ("Food & drink", "warning", 85)],
            budgets.NeedingAttention.Select(item => (item.Category.Name, item.State, item.PercentSpent)));
    }

    [Fact]
    public async Task With_no_budget_this_month_there_is_no_budget_status()
    {
        var (browser, _) = await api.SignedInAsync();
        await BudgetAsync(browser, ThisMonth.AddMonths(-1).ToString("yyyy-MM"), "Transport", "1000000");

        Assert.Null((await DashboardAsync(browser)).Budgets);
    }

    [Fact]
    public async Task The_top_five_top_level_categories_are_those_that_spent_most_this_month()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SpendAsync(browser, everyday, "Groceries", "600000", ThisMonth);
        await SpendAsync(browser, everyday, "Coffee", "100000", ThisMonth);
        await SpendAsync(browser, everyday, "Transport", "500000", ThisMonth);
        await SpendAsync(browser, everyday, "Rent", "4000000", ThisMonth);
        await SpendAsync(browser, everyday, "Clothes", "300000", ThisMonth);
        await SpendAsync(browser, everyday, "Health", "200000", ThisMonth);
        await SpendAsync(browser, everyday, "Personal care", "100000", ThisMonth);
        await SpendAsync(browser, everyday, "Education", "9000000", ThisMonth.AddMonths(-1));

        var top = (await DashboardAsync(browser)).TopCategories;

        Assert.Equal(
            [("Housing", "4000000.00", (decimal?)69.0m), ("Food & drink", "700000.00", 12.1m), ("Transport", "500000.00", 8.6m), ("Shopping", "300000.00", 5.2m), ("Health", "200000.00", 3.4m)],
            top.Select(item => (item.Category.Name, item.Spent, item.Share)));
    }

    [Fact]
    public async Task The_ten_latest_transactions_are_listed_newest_first_with_their_account_and_category()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        for (var day = 1; day <= 10; day++)
        {
            await SpendAsync(browser, everyday, "Coffee", $"{day}000", new DateOnly(2026, 1, day), payee: $"Cafe {day}");
        }

        await TransferAsync(browser, everyday, savings, "500000", new DateOnly(2026, 2, 1));

        var recent = (await DashboardAsync(browser)).RecentTransactions;

        Assert.Equal(10, recent.Count);
        // The two legs of a transfer are recorded together, so either may come first.
        Assert.Equal(
            [("transferIn", "Savings"), ("transferOut", "Everyday")],
            recent.Take(2).Select(leg => (leg.Type, leg.Account.Name)).Order());
        Assert.All(recent.Take(2), leg => Assert.Equal((new DateOnly(2026, 2, 1), "500000.00", "VND", (CategoryBody?)null), (leg.Date, leg.Amount, leg.Currency, leg.Category)));
        Assert.All(recent.Take(2), leg => Assert.NotNull(leg.TransferId));
        Assert.Equal("expense", recent[2].Type);
        Assert.Equal((new DateOnly(2026, 1, 10), "Everyday", "Coffee", "Cafe 10", "10000.00"), (recent[2].Date, recent[2].Account.Name, recent[2].Category?.Name, recent[2].Payee, recent[2].Amount));
        Assert.Equal(new DateOnly(2026, 1, 3), recent[^1].Date);
    }

    [Fact]
    public async Task What_is_coming_up_is_each_occurrence_of_a_recurring_rule_in_the_next_30_days_earliest_first()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await AddRuleAsync(browser, everyday, "Rent", "9000000", Today.AddDays(3), "remind", "Landlord");
        await AddRuleAsync(browser, everyday, "Coffee", "50000", Today.AddDays(1), "autoPost", "Cafe", frequency: "weekly");
        await AddRuleAsync(browser, everyday, "Salary", "20000000", Today.AddDays(31), "autoPost", "Employer", type: "income");

        var upcoming = (await DashboardAsync(browser)).Upcoming;

        Assert.Equal(
            [
                (Today.AddDays(1), "expense", "Cafe", "Coffee", "Everyday", "50000.00", false),
                (Today.AddDays(3), "expense", "Landlord", "Rent", "Everyday", "9000000.00", true),
                (Today.AddDays(8), "expense", "Cafe", "Coffee", "Everyday", "50000.00", false),
                (Today.AddDays(15), "expense", "Cafe", "Coffee", "Everyday", "50000.00", false),
                (Today.AddDays(22), "expense", "Cafe", "Coffee", "Everyday", "50000.00", false),
                (Today.AddDays(29), "expense", "Cafe", "Coffee", "Everyday", "50000.00", false),
            ],
            upcoming.Select(occurrence => (occurrence.DueDate, occurrence.Type, occurrence.Payee, occurrence.Category.Name, occurrence.Account.Name, occurrence.Amount, occurrence.IsReminder)));
    }

    [Fact]
    public async Task Another_users_money_is_never_on_the_dashboard()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        var bobs = await AddAccountAsync(bob, "Bob's", "VND", "1000000");
        await SpendAsync(bob, bobs, "Groceries", "100000", ThisMonth);

        var dashboard = await DashboardAsync(alice);

        Assert.False(dashboard.HasAccounts);
        Assert.Equal("0.00", dashboard.Balance.Balance);
        Assert.Empty(dashboard.TopCategories);
        Assert.Empty(dashboard.RecentTransactions);
    }

    [Fact]
    public async Task The_dashboard_needs_a_signed_in_user() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateBrowser().GetAsync(Dashboard)).StatusCode);

    private static async Task BudgetAsync(Browser browser, string month, string category, string amount)
    {
        var response = await browser.PutAsync($"/api/v1/budgets/{month}/{await CategoryAsync(browser, category)}", new { amount });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task AddRuleAsync(
        Browser browser, Guid accountId, string category, string amount, DateOnly startDate, string mode, string payee, string type = "expense", string frequency = "monthly")
    {
        var response = await browser.PostAsync("/api/v1/recurring-rules", new
        {
            type, accountId, amount, categoryId = await CategoryAsync(browser, category), payee, frequency, startDate = startDate.ToString("yyyy-MM-dd"), mode,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static DateOnly TodayIn(string timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime);

    private static async Task<DashboardBody> DashboardAsync(Browser browser)
    {
        var response = await browser.GetAsync(Dashboard);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DashboardBody>())!;
    }

    private sealed record NotConvertedBody(string Currency, string Balance);

    private sealed record BalanceBody(string Balance, List<NotConvertedBody> NotConverted)
    {
        public bool Equals(BalanceBody? other) => other is not null && Balance == other.Balance && NotConverted.SequenceEqual(other.NotConverted);

        public override int GetHashCode() => Balance.GetHashCode(StringComparison.Ordinal);
    }

    private sealed record FlowBody(string Income, string Expenses, string Net, decimal? SavingsRate, int NotConverted);

    private sealed record CategoryBody(Guid Id, string Name);

    private sealed record TopCategoryBody(CategoryBody Category, string Spent, decimal? Share, int NotConverted);

    private sealed record BudgetItemBody(CategoryBody Category, string Budgeted, string Spent, int? PercentSpent, string State);

    private sealed record BudgetsBody(string Budgeted, string Spent, string Remaining, int? PercentSpent, int OnTrack, int Warning, int Over, int NotConverted, List<BudgetItemBody> NeedingAttention);

    private sealed record AccountBody(Guid Id, string Name);

    private sealed record RecentBody(
        Guid Id, DateOnly Date, string Type, AccountBody Account, string Amount, string Currency, CategoryBody? Category, string? Payee, bool IsRefund, bool IsSplit, Guid? TransferId);

    private sealed record UpcomingBody(
        Guid RecurringRuleId, DateOnly DueDate, string Type, AccountBody Account, string Amount, string Currency, CategoryBody Category, string? Payee, bool IsReminder);

    private sealed record DashboardBody(
        DateOnly From, DateOnly To, string Currency, bool HasAccounts, BalanceBody Balance, FlowBody ThisMonth, BudgetsBody? Budgets,
        List<TopCategoryBody> TopCategories, List<RecentBody> RecentTransactions, List<UpcomingBody> Upcoming);
}
