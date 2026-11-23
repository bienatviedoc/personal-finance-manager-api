using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Infrastructure.Persistence;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Category budgets and what was spent against them (spec section 6: BUD-1, BUD-2, BUD-3; section 3, rules 6, 7 and 12).</summary>
[Collection(ApiCollection.Name)]
public class BudgetTests(PfmApi api) : IAsyncLifetime
{
    private const string Budgets = "/api/v1/budgets";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>The budget month the tests spend in: this one, so that an expense dated in it is never in the future.</summary>
    private static readonly string ThisMonth = Today.ToString("yyyy-MM");

    private static readonly DateOnly FirstOfThisMonth = new(Today.Year, Today.Month, 1);

    private static readonly string LastMonth = FirstOfThisMonth.AddMonths(-1).ToString("yyyy-MM");

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync()
    {
        api.Saves.Stop();
        return Task.CompletedTask;
    }

    // Setting, changing and removing (BUD-1).

    [Fact]
    public async Task A_user_sets_a_budget_for_an_expense_category_in_a_budget_month_in_their_base_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");

        var response = await browser.PutAsync($"{Budgets}/{ThisMonth}/{food}", new { amount = "3000000" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var budget = (await response.Content.ReadFromJsonAsync<BudgetBody>())!;
        Assert.Equal((food, "3000000.00", "0.00", "3000000.00", "onTrack", 0), (budget.CategoryId, budget.Budgeted, budget.Spent, budget.Remaining, budget.State, budget.PercentSpent));
        Assert.Equal(("Food & drink", (Guid?)null, false), (budget.Category.Name, budget.Category.ParentId, budget.Category.IsArchived));
        Assert.Equal($"\"{budget.RowVersion}\"", response.Headers.ETag?.Tag);

        var month = await MonthAsync(browser, ThisMonth);
        Assert.Equal((ThisMonth, "VND", 80), (month.Month, month.Currency, month.WarningPercent));
        Assert.Equal(budget, Assert.Single(month.Items));
    }

    [Fact]
    public async Task A_budget_is_for_one_month_only()
    {
        var (browser, _) = await api.SignedInAsync();
        await SetAsync(browser, LastMonth, await CategoryAsync(browser, "Food & drink"), "3000000");

        Assert.Empty((await MonthAsync(browser, ThisMonth)).Items);
        Assert.Single((await MonthAsync(browser, LastMonth)).Items);
    }

    [Fact]
    public async Task A_budget_can_be_set_for_a_month_still_to_come()
    {
        var (browser, _) = await api.SignedInAsync();
        var nextYear = $"{Today.Year + 1}-01";

        await SetAsync(browser, nextYear, await CategoryAsync(browser, "Transport"), "500000");

        Assert.Equal("500000.00", Assert.Single((await MonthAsync(browser, nextYear)).Items).Budgeted);
    }

    [Fact]
    public async Task Changing_a_budgets_amount_is_made_from_its_row_version()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var budget = await SetAsync(browser, ThisMonth, food, "3000000");

        var changed = await browser.PutAsync($"{Budgets}/{ThisMonth}/{food}", new { amount = "3500000" }, ifMatch: $"\"{budget.RowVersion}\"");

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var body = (await changed.Content.ReadFromJsonAsync<BudgetBody>())!;
        Assert.Equal("3500000.00", body.Budgeted);
        Assert.NotEqual(budget.RowVersion, body.RowVersion);
        Assert.Equal(body, Assert.Single((await MonthAsync(browser, ThisMonth)).Items));
    }

    [Fact]
    public async Task A_change_made_from_a_stale_row_version_is_refused_and_one_that_names_none_too()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var budget = await SetAsync(browser, ThisMonth, food, "3000000");
        await browser.PutAsync($"{Budgets}/{ThisMonth}/{food}", new { amount = "3500000" }, ifMatch: $"\"{budget.RowVersion}\"");

        var stale = await browser.PutAsync($"{Budgets}/{ThisMonth}/{food}", new { amount = "1" }, ifMatch: $"\"{budget.RowVersion}\"");
        var unnamed = await browser.PutAsync($"{Budgets}/{ThisMonth}/{food}", new { amount = "1" });
        var notAVersion = await browser.PutAsync($"{Budgets}/{ThisMonth}/{food}", new { amount = "1" }, ifMatch: "latest");

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionRequired, unnamed.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, notAVersion.StatusCode);
        Assert.Equal("3500000.00", Assert.Single((await MonthAsync(browser, ThisMonth)).Items).Budgeted);
    }

    [Fact]
    public async Task A_row_version_sent_for_a_budget_that_is_no_longer_there_is_stale()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var budget = await SetAsync(browser, ThisMonth, food, "3000000");
        await browser.DeleteAsync($"{Budgets}/{ThisMonth}/{food}");

        var response = await browser.PutAsync($"{Budgets}/{ThisMonth}/{food}", new { amount = "1" }, ifMatch: $"\"{budget.RowVersion}\"");

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Empty((await MonthAsync(browser, ThisMonth)).Items);
    }

    [Fact]
    public async Task When_two_requests_set_the_same_first_budget_at_once_one_is_set_and_the_other_is_stale()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Budgets.Budget>(async () => await SetAsync(browser, ThisMonth, food, "1000000"));

        var response = await browser.PutAsync($"{Budgets}/{ThisMonth}/{food}", new { amount = "2000000" });

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("1000000.00", Assert.Single((await MonthAsync(browser, ThisMonth)).Items).Budgeted);
    }

    [Fact]
    public async Task A_user_removes_a_budget()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var transport = await CategoryAsync(browser, "Transport");
        await SetAsync(browser, ThisMonth, food, "3000000");
        await SetAsync(browser, ThisMonth, transport, "500000");

        var removed = await browser.DeleteAsync($"{Budgets}/{ThisMonth}/{food}");
        var again = await browser.DeleteAsync($"{Budgets}/{ThisMonth}/{food}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(transport, Assert.Single((await MonthAsync(browser, ThisMonth)).Items).CategoryId);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1000.5")]
    [InlineData("1,000")]
    [InlineData("")]
    [InlineData("lots")]
    [InlineData("1000000000000000")]
    public async Task An_amount_that_is_not_one_the_base_currency_takes_is_refused(string amount)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PutAsync($"{Budgets}/{ThisMonth}/{await CategoryAsync(browser, "Food & drink")}", new { amount });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["amount"], await response.ErrorFieldsAsync());
        Assert.Empty((await MonthAsync(browser, ThisMonth)).Items);
    }

    [Fact]
    public async Task A_budget_follows_the_decimals_of_the_base_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        await browser.PatchAsync("/api/v1/me", new { baseCurrency = "USD" });

        var budget = await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Food & drink"), "250.75");

        Assert.Equal("250.75", budget.Budgeted);
        Assert.Equal("USD", (await MonthAsync(browser, ThisMonth)).Currency);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("november")]
    [InlineData("2026-11-01")]
    public async Task A_month_that_is_not_a_budget_month_is_refused(string month)
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");

        var set = await browser.PutAsync($"{Budgets}/{month}/{food}", new { amount = "1" });
        var removed = await browser.DeleteAsync($"{Budgets}/{month}/{food}");
        var listed = await browser.GetAsync($"{Budgets}?month={month}");

        Assert.Equal(["month"], await set.ErrorFieldsAsync());
        Assert.Equal(["month"], await removed.ErrorFieldsAsync());
        Assert.Equal(["month"], await listed.ErrorFieldsAsync());
    }

    [Fact]
    public async Task An_income_category_takes_no_budget()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PutAsync($"{Budgets}/{ThisMonth}/{await CategoryAsync(browser, "Salary")}", new { amount = "1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["categoryId"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task An_archived_category_takes_no_new_budget_and_keeps_the_ones_it_has()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var budget = await SetAsync(browser, LastMonth, food, "3000000");
        await browser.DeleteAsync($"/api/v1/categories/{food}");

        var newBudget = await browser.PutAsync($"{Budgets}/{ThisMonth}/{food}", new { amount = "1" });
        var changed = await browser.PutAsync($"{Budgets}/{LastMonth}/{food}", new { amount = "2000000" }, ifMatch: $"\"{budget.RowVersion}\"");

        Assert.Equal(["categoryId"], await newBudget.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var kept = Assert.Single((await MonthAsync(browser, LastMonth)).Items);
        Assert.Equal(("2000000.00", true), (kept.Budgeted, kept.Category.IsArchived));
    }

    [Fact]
    public async Task A_category_the_user_does_not_have_is_not_found()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        var bobsFood = await CategoryAsync(bob, "Food & drink");
        await SetAsync(bob, ThisMonth, bobsFood, "3000000");

        var set = await alice.PutAsync($"{Budgets}/{ThisMonth}/{bobsFood}", new { amount = "1" });
        var removed = await alice.DeleteAsync($"{Budgets}/{ThisMonth}/{bobsFood}");
        var unknown = await alice.PutAsync($"{Budgets}/{ThisMonth}/{Guid.NewGuid()}", new { amount = "1" });

        Assert.Equal(HttpStatusCode.NotFound, set.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Empty((await MonthAsync(alice, ThisMonth)).Items);
        Assert.Equal("3000000.00", Assert.Single((await MonthAsync(bob, ThisMonth)).Items).Budgeted);
    }

    [Fact]
    public async Task Budgets_need_a_signed_in_user()
    {
        var visitor = api.CreateBrowser();

        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(Budgets)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PutAsync($"{Budgets}/{ThisMonth}/{Guid.NewGuid()}", new { amount = "1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.DeleteAsync($"{Budgets}/{ThisMonth}/{Guid.NewGuid()}")).StatusCode);
    }

    // Parent and subcategory (rule 6).

    [Fact]
    public async Task A_budget_cannot_be_set_on_both_a_parent_and_its_subcategory_for_the_same_month()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var groceries = await CategoryAsync(browser, "Groceries");
        var fuel = await CategoryAsync(browser, "Fuel");
        var transport = await CategoryAsync(browser, "Transport");
        await SetAsync(browser, ThisMonth, food, "3000000");
        await SetAsync(browser, ThisMonth, fuel, "400000");

        var onSubcategory = await browser.PutAsync($"{Budgets}/{ThisMonth}/{groceries}", new { amount = "1" });
        var onParent = await browser.PutAsync($"{Budgets}/{ThisMonth}/{transport}", new { amount = "1" });

        Assert.Equal(["categoryId"], await onSubcategory.ErrorFieldsAsync());
        Assert.Equal(["categoryId"], await onParent.ErrorFieldsAsync());
        Assert.Equal(2, (await MonthAsync(browser, ThisMonth)).Items.Count);

        // Another month, and the parent's other subcategories, are free.
        await SetAsync(browser, LastMonth, groceries, "1000000");
        await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Parking"), "100000");
    }

    [Fact]
    public async Task When_a_parent_and_its_subcategory_are_budgeted_at_the_same_moment_they_do_not_both_keep_a_budget()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var groceries = await CategoryAsync(browser, "Groceries");
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Budgets.Budget>(async () => await SetAsync(browser, ThisMonth, groceries, "1000000"));

        var response = await browser.PutAsync($"{Budgets}/{ThisMonth}/{food}", new { amount = "3000000" });

        Assert.Equal(["categoryId"], await response.ErrorFieldsAsync());
        Assert.Equal(groceries, Assert.Single((await MonthAsync(browser, ThisMonth)).Items).CategoryId);
    }

    // Spent (the calculation of section 6).

    [Fact]
    public async Task Spent_is_what_the_months_expenses_in_the_category_come_to_and_follows_every_change_to_them()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var food = await CategoryAsync(browser, "Food & drink");
        await SetAsync(browser, ThisMonth, food, "3000000");

        var lunch = await SpendAsync(browser, everyday, "Food & drink", "120000", Today);
        await SpendAsync(browser, everyday, "Food & drink", "80000", FirstOfThisMonth);
        await SpendAsync(browser, everyday, "Food & drink", "999000", FirstOfThisMonth.AddDays(-1));
        await SpendAsync(browser, everyday, "Transport", "50000", Today);
        Assert.Equal(("200000.00", "2800000.00", 6), Progress(await BudgetAsync(browser, ThisMonth, food)));

        await browser.DeleteAsync($"/api/v1/transactions/{lunch}");
        Assert.Equal(("80000.00", "2920000.00", 2), Progress(await BudgetAsync(browser, ThisMonth, food)));
    }

    [Fact]
    public async Task A_transfer_never_counts_as_spent_from_either_account()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings");
        var food = await CategoryAsync(browser, "Food & drink");
        await SetAsync(browser, ThisMonth, food, "3000000");
        await SpendAsync(browser, everyday, "Food & drink", "120000", Today);

        var transfer = await browser.PostAsync(
            "/api/v1/transfers", new { fromAccountId = everyday, toAccountId = savings, amount = "2000000", date = Today.ToString("yyyy-MM-dd") });

        Assert.Equal(HttpStatusCode.Created, transfer.StatusCode);
        Assert.Equal(("120000.00", "2880000.00", 4), Progress(await BudgetAsync(browser, ThisMonth, food)));
        Assert.Equal("120000.00", (await MonthAsync(browser, ThisMonth)).Totals.Spent);
    }

    [Fact]
    public async Task Income_never_counts_as_spent()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var food = await CategoryAsync(browser, "Food & drink");
        await SetAsync(browser, ThisMonth, food, "3000000");

        await RecordAsync(browser, "income", everyday, "Salary", "20000000", Today);

        Assert.Equal("0.00", (await BudgetAsync(browser, ThisMonth, food)).Spent);
    }

    [Fact]
    public async Task A_refund_lowers_spent_in_its_category_which_can_go_below_zero_and_is_never_clamped()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var shopping = await CategoryAsync(browser, "Shopping");
        var transport = await CategoryAsync(browser, "Transport");
        await SetAsync(browser, ThisMonth, shopping, "1000000");
        await SetAsync(browser, ThisMonth, transport, "500000");
        await SpendAsync(browser, everyday, "Clothes", "800000", Today);

        await RefundAsync(browser, everyday, "Clothes", "300000", Today);
        Assert.Equal(("500000.00", "500000.00", 50), Progress(await BudgetAsync(browser, ThisMonth, shopping)));

        await RefundAsync(browser, everyday, "Shopping", "700000", Today);
        await RefundAsync(browser, everyday, "Transport", "50000", Today);
        var month = await MonthAsync(browser, ThisMonth);
        Assert.Equal(("-200000.00", "1200000.00", -20), Progress(month.Items.Single(budget => budget.CategoryId == shopping)));
        Assert.Equal(("-50000.00", "550000.00", "onTrack"), (month.Items.Single(budget => budget.CategoryId == transport).Spent, month.Items.Single(budget => budget.CategoryId == transport).Remaining, month.Items.Single(budget => budget.CategoryId == transport).State));
        Assert.Equal(new TotalsBody("1500000.00", "-250000.00", "1750000.00", 0), month.Totals);
    }

    [Fact]
    public async Task A_split_expense_counts_each_line_in_its_own_category()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var food = await CategoryAsync(browser, "Food & drink");
        var household = await CategoryAsync(browser, "Household");
        await SetAsync(browser, ThisMonth, food, "3000000");
        await SetAsync(browser, ThisMonth, household, "1000000");

        await SplitAsync(browser, everyday, "900000", Today, ("Groceries", "500000"), ("Coffee", "100000"), ("Household", "250000"), ("Transport", "50000"));

        var month = await MonthAsync(browser, ThisMonth);
        Assert.Equal("600000.00", month.Items.Single(budget => budget.CategoryId == food).Spent);
        Assert.Equal("250000.00", month.Items.Single(budget => budget.CategoryId == household).Spent);
        Assert.Equal("850000.00", month.Totals.Spent);
    }

    [Fact]
    public async Task A_split_expense_in_another_currency_shares_its_converted_amount_among_its_lines_and_is_one_expense_while_not_converted()
    {
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var food = await CategoryAsync(browser, "Food & drink");
        var transport = await CategoryAsync(browser, "Transport");
        await SetAsync(browser, ThisMonth, food, "3000000");
        await SetAsync(browser, ThisMonth, transport, "1000000");
        await SplitAsync(browser, dollars, "10", FirstOfThisMonth, ("Groceries", "3.33"), ("Coffee", "3.33"), ("Transport", "3.34"));

        var waiting = await MonthAsync(browser, ThisMonth);
        Assert.Equal([1, 1], waiting.Items.Select(budget => budget.NotConverted));
        Assert.Equal(("0.00", 1), (waiting.Totals.Spent, waiting.Totals.NotConverted));

        // 10 USD at 25,001 is 250,010 VND: 83,253 for each of the first two lines, and the last takes what is left.
        await RatesAreAsync((FirstOfThisMonth.AddDays(-1), 25001m));

        var converted = await MonthAsync(browser, ThisMonth);
        Assert.Equal("166506.00", converted.Items.Single(budget => budget.CategoryId == food).Spent);
        Assert.Equal("83504.00", converted.Items.Single(budget => budget.CategoryId == transport).Spent);
        Assert.Equal(("250010.00", 0), (converted.Totals.Spent, converted.Totals.NotConverted));
    }

    [Fact]
    public async Task Spending_in_a_subcategory_counts_toward_a_budget_on_its_parent()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var food = await CategoryAsync(browser, "Food & drink");
        var fuel = await CategoryAsync(browser, "Fuel");
        await SetAsync(browser, ThisMonth, food, "3000000");
        await SetAsync(browser, ThisMonth, fuel, "400000");

        await SpendAsync(browser, everyday, "Food & drink", "100000", Today);
        await SpendAsync(browser, everyday, "Groceries", "1250000", Today);
        await SpendAsync(browser, everyday, "Coffee", "45000", Today);
        await SpendAsync(browser, everyday, "Fuel", "150000", Today);
        await SpendAsync(browser, everyday, "Parking", "20000", Today);
        await SpendAsync(browser, everyday, "Transport", "70000", Today);

        Assert.Equal("1395000.00", (await BudgetAsync(browser, ThisMonth, food)).Spent);
        Assert.Equal("150000.00", (await BudgetAsync(browser, ThisMonth, fuel)).Spent);
    }

    [Fact]
    public async Task An_expense_in_another_currency_counts_as_converted_at_the_rate_of_its_own_date_and_is_not_revalued()
    {
        await RatesAreAsync((FirstOfThisMonth.AddDays(-2), 24000m), (FirstOfThisMonth, 25000m));
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var everyday = await AddAccountAsync(browser, "Everyday");
        var food = await CategoryAsync(browser, "Food & drink");
        await SetAsync(browser, ThisMonth, food, "5000000");
        await SetAsync(browser, LastMonth, food, "5000000");

        await SpendAsync(browser, dollars, "Groceries", "40", FirstOfThisMonth);
        await SpendAsync(browser, everyday, "Groceries", "250000", FirstOfThisMonth);
        await SpendAsync(browser, dollars, "Groceries", "10", FirstOfThisMonth.AddDays(-1));

        Assert.Equal(("1250000.00", 0), ((await BudgetAsync(browser, ThisMonth, food)).Spent, (await BudgetAsync(browser, ThisMonth, food)).NotConverted));
        Assert.Equal("240000.00", (await BudgetAsync(browser, LastMonth, food)).Spent);

        if (Today > FirstOfThisMonth)
        {
            await RatesAreAsync((Today, 30000m));
            Assert.Equal("1250000.00", (await BudgetAsync(browser, ThisMonth, food)).Spent);
        }
    }

    [Fact]
    public async Task An_expense_that_is_not_converted_is_left_out_of_spent_and_counted_until_a_rate_arrives()
    {
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var everyday = await AddAccountAsync(browser, "Everyday");
        var food = await CategoryAsync(browser, "Food & drink");
        await SetAsync(browser, ThisMonth, food, "5000000");
        await SpendAsync(browser, dollars, "Groceries", "40", FirstOfThisMonth);
        await SpendAsync(browser, everyday, "Groceries", "250000", FirstOfThisMonth);

        var waiting = await MonthAsync(browser, ThisMonth);
        Assert.Equal(("250000.00", 1), (waiting.Items[0].Spent, waiting.Items[0].NotConverted));
        Assert.Equal(("250000.00", 1), (waiting.Totals.Spent, waiting.Totals.NotConverted));

        await RatesAreAsync((FirstOfThisMonth.AddDays(-1), 25000m));

        var converted = await BudgetAsync(browser, ThisMonth, food);
        Assert.Equal(("1250000.00", 0), (converted.Spent, converted.NotConverted));
    }

    // States and totals (BUD-2, BUD-3).

    [Fact]
    public async Task Each_budget_is_on_track_a_warning_at_or_above_80_percent_or_over_above_100_percent_and_the_month_has_totals()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Food & drink"), "1000000");
        await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Transport"), "1000000");
        await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Housing"), "1000000");
        await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Shopping"), "1000000");
        await SpendAsync(browser, everyday, "Food & drink", "799999", Today);
        await SpendAsync(browser, everyday, "Transport", "800000", Today);
        await SpendAsync(browser, everyday, "Housing", "1000000", Today);
        await SpendAsync(browser, everyday, "Shopping", "1000001", Today);

        var month = await MonthAsync(browser, ThisMonth);

        Assert.Equal(
            [("Food & drink", "onTrack", 79), ("Housing", "warning", 100), ("Shopping", "over", 100), ("Transport", "warning", 80)],
            month.Items.Select(budget => (budget.Category.Name, budget.State, budget.PercentSpent!.Value)));
        Assert.Equal(new TotalsBody("4000000.00", "3600000.00", "400000.00", 0), month.Totals);
        Assert.Equal("-1.00", month.Items.Single(budget => budget.Category.Name == "Shopping").Remaining);
    }

    [Fact]
    public async Task The_warning_starts_at_the_users_own_threshold()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var food = await CategoryAsync(browser, "Food & drink");
        await SetAsync(browser, ThisMonth, food, "1000000");
        await SpendAsync(browser, everyday, "Food & drink", "600000", Today);
        Assert.Equal("onTrack", (await BudgetAsync(browser, ThisMonth, food)).State);

        await browser.PatchAsync("/api/v1/me", new { budgetWarningPercent = 60 });

        var month = await MonthAsync(browser, ThisMonth);
        Assert.Equal((60, "warning"), (month.WarningPercent, month.Items[0].State));
    }

    [Fact]
    public async Task Budgets_are_listed_as_the_category_tree_is_a_parent_by_name_then_its_subcategories()
    {
        var (browser, _) = await api.SignedInAsync();
        foreach (var name in new[] { "Transport", "Groceries", "Housing", "Coffee" })
        {
            await SetAsync(browser, ThisMonth, await CategoryAsync(browser, name), "1000");
        }

        var month = await MonthAsync(browser, ThisMonth);

        Assert.Equal(["Coffee", "Groceries", "Housing", "Transport"], month.Items.Select(budget => budget.Category.Name));
        Assert.Equal(await CategoryAsync(browser, "Food & drink"), month.Items[0].Category.ParentId);
    }

    [Fact]
    public async Task A_month_with_no_budgets_answers_none_and_zero_totals()
    {
        var (browser, _) = await api.SignedInAsync();

        var month = await MonthAsync(browser, ThisMonth);

        Assert.Empty(month.Items);
        Assert.Equal(new TotalsBody("0.00", "0.00", "0.00", 0), month.Totals);
    }

    [Fact]
    public async Task Without_a_month_the_budgets_are_those_of_the_current_month_in_the_users_time_zone()
    {
        var (browser, _) = await api.SignedInAsync();
        await browser.PatchAsync("/api/v1/me", new { timeZone = "Pacific/Kiritimati" });
        var there = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati"));

        var month = (await browser.Http.GetFromJsonAsync<MonthBody>(Budgets))!;

        Assert.Equal(there.ToString("yyyy-MM"), month.Month);
    }

    // The base currency locks (rule 12; decision record 0002).

    [Fact]
    public async Task The_base_currency_is_locked_once_the_users_first_budget_exists()
    {
        var (browser, _) = await api.SignedInAsync();
        Assert.False((await ProfileAsync(browser)).BaseCurrencyLocked);
        Assert.Equal(HttpStatusCode.OK, (await browser.PatchAsync("/api/v1/me", new { baseCurrency = "EUR" })).StatusCode);

        var budget = await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Food & drink"), "300");
        var change = await browser.PatchAsync("/api/v1/me", new { baseCurrency = "USD", timeZone = "Europe/Berlin" });

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Equal(["baseCurrency"], await change.ErrorFieldsAsync());
        Assert.Equal(new ProfileBody("EUR", "Asia/Ho_Chi_Minh", true), await ProfileAsync(browser));
        Assert.Equal("300.00", budget.Budgeted);
    }

    [Fact]
    public async Task A_locked_base_currency_still_lets_every_other_setting_change_and_may_be_sent_as_it_is()
    {
        var (browser, _) = await api.SignedInAsync();
        await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Food & drink"), "3000000");

        var change = await browser.PatchAsync("/api/v1/me", new { baseCurrency = "VND", timeZone = "Europe/Berlin" });

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(new ProfileBody("VND", "Europe/Berlin", true), await change.Content.ReadFromJsonAsync<ProfileBody>());
    }

    [Fact]
    public async Task One_users_budget_does_not_lock_another_users_base_currency()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        await SetAsync(alice, ThisMonth, await CategoryAsync(alice, "Food & drink"), "3000000");

        Assert.Equal(HttpStatusCode.OK, (await bob.PatchAsync("/api/v1/me", new { baseCurrency = "USD" })).StatusCode);
    }

    [Fact]
    public async Task A_base_currency_change_that_meets_the_users_first_budget_set_at_the_same_moment_is_refused()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        api.Saves.BeforeNextSaveOf<Pfm.Infrastructure.Identity.User>(async () => await SetAsync(browser, ThisMonth, food, "3000000"));

        var change = await browser.PatchAsync("/api/v1/me", new { baseCurrency = "USD" });

        Assert.Equal(["baseCurrency"], await change.ErrorFieldsAsync());
        Assert.Equal("VND", (await ProfileAsync(browser)).BaseCurrency);
        Assert.Equal("VND", (await MonthAsync(browser, ThisMonth)).Currency);
    }

    [Fact]
    public async Task A_first_budget_that_meets_a_base_currency_change_made_at_the_same_moment_is_set_in_the_new_base_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Budgets.Budget>(async () =>
            Assert.Equal(HttpStatusCode.OK, (await browser.PatchAsync("/api/v1/me", new { baseCurrency = "USD" })).StatusCode));

        var budget = await SetAsync(browser, ThisMonth, food, "300");

        Assert.Equal("300.00", budget.Budgeted);
        Assert.Equal(new ProfileBody("USD", "Asia/Ho_Chi_Minh", true), await ProfileAsync(browser));
        Assert.Equal(Pfm.Domain.Currency.USD, await SavedCurrencyAsync(food));
    }

    // A budgeted category stays an expense category.

    [Fact]
    public async Task A_category_with_a_budget_or_with_a_budgeted_subcategory_cannot_become_an_income_category()
    {
        var (browser, _) = await api.SignedInAsync();
        await SetAsync(browser, LastMonth, await CategoryAsync(browser, "Groceries"), "1000000");
        await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Transport"), "1000000");

        var food = await MakeIncomeAsync(browser, "Food & drink");
        var transport = await MakeIncomeAsync(browser, "Transport");
        var housing = await MakeIncomeAsync(browser, "Housing");

        Assert.Equal(["kind"], await food.ErrorFieldsAsync());
        Assert.Equal(["kind"], await transport.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.OK, housing.StatusCode);
    }

    [Fact]
    public async Task A_change_of_kind_that_meets_a_budget_set_on_the_category_at_the_same_moment_is_refused()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Transactions.Category>(async () => await SetAsync(browser, ThisMonth, food, "3000000"));

        var response = await MakeIncomeAsync(browser, "Food & drink");

        Assert.Equal(["kind"], await response.ErrorFieldsAsync());
        Assert.Single((await MonthAsync(browser, ThisMonth)).Items);
    }

    private static async Task<HttpResponseMessage> MakeIncomeAsync(Browser browser, string category) =>
        await browser.PutAsync($"/api/v1/categories/{await CategoryAsync(browser, category)}", new { name = category, kind = "income", icon = "utensils", color = "#f97316" });

    private static async Task<ProfileBody> ProfileAsync(Browser browser) => (await browser.Http.GetFromJsonAsync<ProfileBody>("/api/v1/me"))!;

    /// <summary>The currency the saved budget of a category holds.</summary>
    private async Task<Pfm.Domain.Currency> SavedCurrencyAsync(Guid categoryId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PfmDbContext>().Set<Pfm.Domain.Budgets.Budget>()
            .IgnoreQueryFilters().Where(budget => budget.CategoryId == categoryId).Select(budget => budget.Currency).SingleAsync();
    }

    private sealed record ProfileBody(string BaseCurrency, string TimeZone, bool BaseCurrencyLocked);

    private static (string Spent, string Remaining, int? PercentSpent) Progress(BudgetBody budget) => (budget.Spent, budget.Remaining, budget.PercentSpent);

    private async Task RatesAreAsync(params (DateOnly Date, decimal Dong)[] days)
    {
        foreach (var (date, dong) in days)
        {
            api.ExchangeRateSource.Quote(date, dong);
        }

        await api.FetchExchangeRatesAsync();
    }

    private static async Task<BudgetBody> SetAsync(Browser browser, string month, Guid categoryId, string amount)
    {
        var response = await browser.PutAsync($"{Budgets}/{month}/{categoryId}", new { amount });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BudgetBody>())!;
    }

    private static async Task<MonthBody> MonthAsync(Browser browser, string month) =>
        (await browser.Http.GetFromJsonAsync<MonthBody>($"{Budgets}?month={month}"))!;

    private static async Task<BudgetBody> BudgetAsync(Browser browser, string month, Guid categoryId) =>
        (await MonthAsync(browser, month)).Items.Single(budget => budget.CategoryId == categoryId);

    private static Task<Guid> SpendAsync(Browser browser, Guid accountId, string category, string amount, DateOnly date) =>
        RecordAsync(browser, "expense", accountId, category, amount, date);

    private static Task<Guid> RefundAsync(Browser browser, Guid accountId, string category, string amount, DateOnly date) =>
        RecordAsync(browser, "expense", accountId, category, amount, date, isRefund: true);

    /// <summary>Records one expense split across categories, each line with its part of the amount.</summary>
    private static async Task SplitAsync(Browser browser, Guid accountId, string amount, DateOnly date, params (string Category, string Amount)[] lines)
    {
        var parts = new List<object>();
        foreach (var line in lines)
        {
            parts.Add(new { categoryId = await CategoryAsync(browser, line.Category), amount = line.Amount });
        }

        var response = await browser.PostAsync("/api/v1/transactions", new { type = "expense", accountId, amount, date = date.ToString("yyyy-MM-dd"), lines = parts });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<Guid> RecordAsync(Browser browser, string type, Guid accountId, string category, string amount, DateOnly date, bool isRefund = false)
    {
        var response = await browser.PostAsync("/api/v1/transactions", new
        {
            type, accountId, amount, date = date.ToString("yyyy-MM-dd"), categoryId = await CategoryAsync(browser, category), isRefund,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    private sealed record Identified(Guid Id);

    private sealed record CategoryBody(Guid Id, Guid? ParentId, string Name, string Icon, string Color, bool IsArchived);

    private sealed record BudgetBody(
        Guid CategoryId, CategoryBody Category, string Budgeted, string Spent, string Remaining, int? PercentSpent, string State, int NotConverted,
        string RowVersion);

    private sealed record TotalsBody(string Budgeted, string Spent, string Remaining, int NotConverted);

    private sealed record MonthBody(string Month, string Currency, int WarningPercent, List<BudgetBody> Items, TotalsBody Totals);
}
