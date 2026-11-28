using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Infrastructure.Persistence;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Category budgets and what was spent against them (spec section 6: BUD-1 to BUD-4; section 3, rules 6, 7 and 12).</summary>
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

    // Copying last month's budgets (BUD-4).

    [Fact]
    public async Task A_user_copies_every_budget_of_last_month_into_this_month_in_one_action()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var fuel = await CategoryAsync(browser, "Fuel");
        await SetAsync(browser, LastMonth, food, "3000000");
        await SetAsync(browser, LastMonth, fuel, "400000");

        var response = await CopyAsync(browser, ThisMonth, LastMonth);

        Assert.Equal(2, await CopiedAsync(response));
        var copied = (await MonthAsync(browser, ThisMonth)).Items;
        Assert.Equal(new[] { (food, "3000000.00"), (fuel, "400000.00") }.Order(), copied.Select(budget => (budget.CategoryId, budget.Budgeted)).Order());
        Assert.Equal(2, (await MonthAsync(browser, LastMonth)).Items.Count);
    }

    [Fact]
    public async Task Copying_leaves_alone_a_category_already_budgeted_in_the_month_and_says_how_many_were_copied()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var fuel = await CategoryAsync(browser, "Fuel");
        await SetAsync(browser, LastMonth, food, "3000000");
        await SetAsync(browser, LastMonth, fuel, "400000");
        var kept = await SetAsync(browser, ThisMonth, food, "3500000");

        var response = await CopyAsync(browser, ThisMonth, LastMonth);

        Assert.Equal(1, await CopiedAsync(response));
        Assert.Equal(kept, await BudgetAsync(browser, ThisMonth, food));
        Assert.Equal("400000.00", (await BudgetAsync(browser, ThisMonth, fuel)).Budgeted);
    }

    [Fact]
    public async Task Copying_again_copies_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        await SetAsync(browser, LastMonth, await CategoryAsync(browser, "Food & drink"), "3000000");
        await CopyAsync(browser, ThisMonth, LastMonth);

        var again = await CopyAsync(browser, ThisMonth, LastMonth);

        Assert.Equal(0, await CopiedAsync(again));
        Assert.Single((await MonthAsync(browser, ThisMonth)).Items);
    }

    [Fact]
    public async Task A_budget_on_an_archived_category_is_not_copied()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var fuel = await CategoryAsync(browser, "Fuel");
        await SetAsync(browser, LastMonth, food, "3000000");
        await SetAsync(browser, LastMonth, fuel, "400000");
        await browser.DeleteAsync($"/api/v1/categories/{food}");

        var response = await CopyAsync(browser, ThisMonth, LastMonth);

        Assert.Equal(1, await CopiedAsync(response));
        Assert.Equal(fuel, Assert.Single((await MonthAsync(browser, ThisMonth)).Items).CategoryId);
    }

    [Fact]
    public async Task A_budget_whose_parent_or_subcategory_is_budgeted_in_the_month_is_not_copied()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var groceries = await CategoryAsync(browser, "Groceries");
        var transport = await CategoryAsync(browser, "Transport");
        var fuel = await CategoryAsync(browser, "Fuel");
        await SetAsync(browser, LastMonth, food, "3000000");
        await SetAsync(browser, LastMonth, fuel, "400000");
        await SetAsync(browser, ThisMonth, groceries, "1000000");
        await SetAsync(browser, ThisMonth, transport, "800000");

        var response = await CopyAsync(browser, ThisMonth, LastMonth);

        Assert.Equal(0, await CopiedAsync(response));
        Assert.Equal(new[] { groceries, transport }.Order(), (await MonthAsync(browser, ThisMonth)).Items.Select(budget => budget.CategoryId).Order());
    }

    [Theory]
    [InlineData("2026-13", "2026-10", "month")]
    [InlineData("2026-11", "october", "sourceMonth")]
    [InlineData("2026-11", "2026-11", "sourceMonth")]
    public async Task A_copy_needs_two_different_budget_months(string month, string sourceMonth, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await CopyAsync(browser, month, sourceMonth);

        Assert.Equal([field], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_user_copies_only_their_own_budgets_and_needs_to_be_signed_in()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        await SetAsync(bob, LastMonth, await CategoryAsync(bob, "Food & drink"), "3000000");

        Assert.Equal(0, await CopiedAsync(await CopyAsync(alice, ThisMonth, LastMonth)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await CopyAsync(api.CreateBrowser(), ThisMonth, LastMonth)).StatusCode);
        Assert.Empty((await MonthAsync(bob, ThisMonth)).Items);
    }

    [Fact]
    public async Task A_budget_set_in_the_month_while_copying_is_kept_and_the_rest_are_copied()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var fuel = await CategoryAsync(browser, "Fuel");
        await SetAsync(browser, LastMonth, food, "3000000");
        await SetAsync(browser, LastMonth, fuel, "400000");
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Budgets.Budget>(async () => await SetAsync(browser, ThisMonth, food, "1000000"));

        var response = await CopyAsync(browser, ThisMonth, LastMonth);

        Assert.Equal(1, await CopiedAsync(response));
        Assert.Equal("1000000.00", (await BudgetAsync(browser, ThisMonth, food)).Budgeted);
        Assert.Equal("400000.00", (await BudgetAsync(browser, ThisMonth, fuel)).Budgeted);
    }

    [Fact]
    public async Task A_budget_copied_onto_a_parent_whose_subcategory_is_budgeted_while_copying_is_taken_back()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        var groceries = await CategoryAsync(browser, "Groceries");
        var fuel = await CategoryAsync(browser, "Fuel");
        await SetAsync(browser, LastMonth, food, "3000000");
        await SetAsync(browser, LastMonth, fuel, "400000");
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Budgets.Budget>(async () => await SetAsync(browser, ThisMonth, groceries, "1000000"));

        var response = await CopyAsync(browser, ThisMonth, LastMonth);

        Assert.Equal(1, await CopiedAsync(response));
        Assert.Equal(new[] { fuel, groceries }.Order(), (await MonthAsync(browser, ThisMonth)).Items.Select(budget => budget.CategoryId).Order());
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

    // Drilling down (BUD-8).

    [Fact]
    public async Task The_transactions_behind_a_budget_are_those_of_its_category_and_subcategories_in_its_month_and_their_spent_is_the_budgets()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var food = await CategoryAsync(browser, "Food & drink");
        await SetAsync(browser, ThisMonth, food, "3000000");
        await SpendAsync(browser, everyday, "Food & drink", "100000", FirstOfThisMonth);
        await SpendAsync(browser, everyday, "Groceries", "250000", Today);
        await RefundAsync(browser, everyday, "Coffee", "30000", Today);
        await SplitAsync(browser, dollars, "10", FirstOfThisMonth, ("Groceries", "3.33"), ("Coffee", "3.33"), ("Transport", "3.34"));
        await SpendAsync(browser, everyday, "Groceries", "999", FirstOfThisMonth.AddDays(-1));
        await SpendAsync(browser, everyday, "Transport", "70000", Today);
        await RatesAreAsync((FirstOfThisMonth.AddDays(-1), 25001m));

        var budget = await BudgetAsync(browser, ThisMonth, food);
        var behind = await TransactionsBehindAsync(browser, food);

        // 100,000 + 250,000 - 30,000, and the split's two lines on food: 166,506 of the 250,010 its 10 USD came to.
        Assert.Equal(("486506.00", 0), (budget.Spent, budget.NotConverted));
        Assert.Equal(new[] { "10.00", "30000.00", "100000.00", "250000.00" }.Order(), behind.Items.Select(transaction => transaction.Amount).Order());
        Assert.Equal(new SpentBody("VND", budget.Spent, budget.NotConverted), behind.Total.Spent);
    }

    [Fact]
    public async Task An_expense_behind_a_budget_that_is_not_converted_is_listed_and_counted_as_the_budget_counts_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var food = await CategoryAsync(browser, "Food & drink");
        await SetAsync(browser, ThisMonth, food, "3000000");
        await SpendAsync(browser, everyday, "Groceries", "250000", Today);
        await SpendAsync(browser, dollars, "Groceries", "40", FirstOfThisMonth);

        var budget = await BudgetAsync(browser, ThisMonth, food);
        var behind = await TransactionsBehindAsync(browser, food);

        Assert.Equal(("250000.00", 1), (budget.Spent, budget.NotConverted));
        Assert.Equal(2, behind.Items.Length);
        Assert.Equal(new SpentBody("VND", "250000.00", 1), behind.Total.Spent);
    }

    [Fact]
    public async Task A_list_not_narrowed_to_a_category_says_nothing_of_spent()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SpendAsync(browser, everyday, "Groceries", "250000", Today);

        var list = (await browser.Http.GetFromJsonAsync<DrillDownBody>("/api/v1/transactions"))!;

        Assert.Null(list.Total.Spent);
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

    // The overall budget (BUD-5).

    [Fact]
    public async Task A_user_sets_an_overall_budget_for_a_month_which_tracks_every_expense_whether_or_not_its_category_has_a_budget()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Food & drink"), "3000000");
        await SpendAsync(browser, everyday, "Groceries", "1200000", Today);
        await SpendAsync(browser, everyday, "Transport", "500000", Today);
        await RefundAsync(browser, everyday, "Shopping", "100000", Today);
        await SpendAsync(browser, everyday, "Housing", "4000000", FirstOfThisMonth.AddDays(-1));
        await RecordAsync(browser, "income", everyday, "Salary", "20000000", Today);
        await browser.PostAsync("/api/v1/transfers", new { fromAccountId = everyday, toAccountId = savings, amount = "2000000", date = Today.ToString("yyyy-MM-dd") });

        var response = await browser.PutAsync($"{Budgets}/{ThisMonth}/overall", new { amount = "10000000" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var overall = (await response.Content.ReadFromJsonAsync<OverallBody>())!;
        Assert.Equal(("10000000.00", "1600000.00", "8400000.00", 16, "onTrack", 0), (overall.Budgeted, overall.Spent, overall.Remaining, overall.PercentSpent!.Value, overall.State, overall.NotConverted));
        Assert.Equal($"\"{overall.RowVersion}\"", response.Headers.ETag?.Tag);

        var month = await MonthAsync(browser, ThisMonth);
        Assert.Equal(overall, month.Overall);
        Assert.Equal(new TotalsBody("3000000.00", "1200000.00", "1800000.00", 0), month.Totals);
        Assert.Single(month.Items);
    }

    [Fact]
    public async Task A_month_without_an_overall_budget_answers_none_and_an_overall_budget_is_for_one_month_only()
    {
        var (browser, _) = await api.SignedInAsync();
        await SetOverallAsync(browser, LastMonth, "10000000");

        Assert.Null((await MonthAsync(browser, ThisMonth)).Overall);
        Assert.Equal("10000000.00", (await MonthAsync(browser, LastMonth)).Overall!.Budgeted);
    }

    [Fact]
    public async Task The_overall_budget_has_the_same_states_and_counts_what_is_not_converted()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var dollars = await AddAccountAsync(browser, "Dollars", "USD");
        await SetOverallAsync(browser, ThisMonth, "1000000");
        await SpendAsync(browser, everyday, "Transport", "1000001", Today);
        await SpendAsync(browser, dollars, "Shopping", "10", Today);

        var overall = (await MonthAsync(browser, ThisMonth)).Overall!;

        Assert.Equal(("1000001.00", "-1.00", "over", 1), (overall.Spent, overall.Remaining, overall.State, overall.NotConverted));
    }

    [Fact]
    public async Task Changing_the_overall_budget_is_made_from_its_row_version_and_removing_it_leaves_the_category_budgets()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        await SetAsync(browser, ThisMonth, food, "3000000");
        var overall = await SetOverallAsync(browser, ThisMonth, "10000000");

        var unnamed = await browser.PutAsync($"{Budgets}/{ThisMonth}/overall", new { amount = "1" });
        var changed = await browser.PutAsync($"{Budgets}/{ThisMonth}/overall", new { amount = "12000000" }, ifMatch: $"\"{overall.RowVersion}\"");
        var stale = await browser.PutAsync($"{Budgets}/{ThisMonth}/overall", new { amount = "1" }, ifMatch: $"\"{overall.RowVersion}\"");

        Assert.Equal(HttpStatusCode.PreconditionRequired, unnamed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("12000000.00", (await MonthAsync(browser, ThisMonth)).Overall!.Budgeted);

        var removed = await browser.DeleteAsync($"{Budgets}/{ThisMonth}/overall");
        var again = await browser.DeleteAsync($"{Budgets}/{ThisMonth}/overall");

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound), (removed.StatusCode, again.StatusCode));
        var month = await MonthAsync(browser, ThisMonth);
        Assert.Null(month.Overall);
        Assert.Equal(food, Assert.Single(month.Items).CategoryId);
    }

    [Fact]
    public async Task When_two_requests_set_the_same_overall_budget_at_once_one_is_set_and_the_other_is_stale()
    {
        var (browser, _) = await api.SignedInAsync();
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Budgets.Budget>(async () => await SetOverallAsync(browser, ThisMonth, "1000000"));

        var response = await browser.PutAsync($"{Budgets}/{ThisMonth}/overall", new { amount = "2000000" });

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("1000000.00", (await MonthAsync(browser, ThisMonth)).Overall!.Budgeted);
    }

    [Theory]
    [InlineData("2026-13", "1", "month")]
    [InlineData("november", "1", "month")]
    [InlineData(null, "-1", "amount")]
    [InlineData(null, "lots", "amount")]
    public async Task An_overall_budget_takes_a_budget_month_and_an_amount_the_base_currency_takes(string? month, string amount, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PutAsync($"{Budgets}/{month ?? ThisMonth}/overall", new { amount });

        Assert.Equal([field], await response.ErrorFieldsAsync());
        Assert.Null((await MonthAsync(browser, ThisMonth)).Overall);
    }

    [Fact]
    public async Task A_users_overall_budget_is_their_own_and_needs_them_signed_in()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        await SetOverallAsync(bob, ThisMonth, "5000000");
        var visitor = api.CreateBrowser();

        Assert.Null((await MonthAsync(alice, ThisMonth)).Overall);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"{Budgets}/{ThisMonth}/overall")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PutAsync($"{Budgets}/{ThisMonth}/overall", new { amount = "1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.DeleteAsync($"{Budgets}/{ThisMonth}/overall")).StatusCode);
        Assert.Equal("5000000.00", (await MonthAsync(bob, ThisMonth)).Overall!.Budgeted);
    }

    [Fact]
    public async Task An_overall_budget_alone_locks_the_base_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        await SetOverallAsync(browser, ThisMonth, "10000000");

        var change = await browser.PatchAsync("/api/v1/me", new { baseCurrency = "USD" });

        Assert.Equal(["baseCurrency"], await change.ErrorFieldsAsync());
        await browser.DeleteAsync($"{Budgets}/{ThisMonth}/overall");
        Assert.False((await ProfileAsync(browser)).BaseCurrencyLocked);
    }

    [Fact]
    public async Task Copying_last_months_budgets_copies_its_overall_budget_unless_the_month_has_one()
    {
        var (browser, _) = await api.SignedInAsync();
        await SetAsync(browser, LastMonth, await CategoryAsync(browser, "Transport"), "500000");
        await SetOverallAsync(browser, LastMonth, "10000000");
        var nextMonth = FirstOfThisMonth.AddMonths(1).ToString("yyyy-MM");
        await SetOverallAsync(browser, nextMonth, "8000000");

        Assert.Equal(2, await CopiedAsync(await CopyAsync(browser, ThisMonth, LastMonth)));
        Assert.Equal(1, await CopiedAsync(await CopyAsync(browser, nextMonth, LastMonth)));
        Assert.Equal("10000000.00", (await MonthAsync(browser, ThisMonth)).Overall!.Budgeted);
        Assert.Equal("8000000.00", (await MonthAsync(browser, nextMonth)).Overall!.Budgeted);
    }

    // The pace indicator (BUD-6).

    [Fact]
    public async Task In_the_current_month_each_budget_and_the_overall_budget_show_their_pace_against_the_share_of_the_month_elapsed()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Food & drink"), "1000000");
        await SetAsync(browser, ThisMonth, await CategoryAsync(browser, "Transport"), "1000000");
        await SetOverallAsync(browser, ThisMonth, "1000000");
        await SpendAsync(browser, everyday, "Food & drink", "1500000", Today);

        var month = await MonthAsync(browser, ThisMonth);

        Assert.Equal([("Food & drink", "aheadOfPace"), ("Transport", "onPace")], month.Items.Select(budget => (budget.Category.Name, budget.Pace!)));
        Assert.Equal("aheadOfPace", month.Overall!.Pace);
        Assert.InRange(month.PercentOfMonthElapsed!.Value, 3, 100);
    }

    [Fact]
    public async Task A_past_month_shows_its_final_result_with_no_pace()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SetAsync(browser, LastMonth, await CategoryAsync(browser, "Food & drink"), "1000000");
        await SetOverallAsync(browser, LastMonth, "1000000");
        await SpendAsync(browser, everyday, "Food & drink", "1500000", FirstOfThisMonth.AddDays(-1));

        var month = await MonthAsync(browser, LastMonth);

        var food = Assert.Single(month.Items);
        Assert.Equal(("1500000.00", "over", (string?)null), (food.Spent, food.State, food.Pace));
        Assert.Equal(("1500000.00", "over", (string?)null), (month.Overall!.Spent, month.Overall.State, month.Overall.Pace));
        Assert.Null(month.PercentOfMonthElapsed);
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
    public async Task The_base_currency_stays_locked_until_the_last_budget_is_removed_and_can_then_change_again()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await CategoryAsync(browser, "Food & drink");
        await SetAsync(browser, ThisMonth, food, "3000000");
        await SetOverallAsync(browser, LastMonth, "10000000");

        await browser.DeleteAsync($"{Budgets}/{ThisMonth}/{food}");
        var whileOneRemains = await browser.PatchAsync("/api/v1/me", new { baseCurrency = "USD" });
        await browser.DeleteAsync($"{Budgets}/{LastMonth}/overall");
        var afterTheLast = await browser.PatchAsync("/api/v1/me", new { baseCurrency = "USD" });

        Assert.Equal(["baseCurrency"], await whileOneRemains.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.OK, afterTheLast.StatusCode);
        Assert.Equal(new ProfileBody("USD", "Asia/Ho_Chi_Minh", false), await ProfileAsync(browser));
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

    private static async Task<OverallBody> SetOverallAsync(Browser browser, string month, string amount)
    {
        var response = await browser.PutAsync($"{Budgets}/{month}/overall", new { amount });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OverallBody>())!;
    }

    private static Task<HttpResponseMessage> CopyAsync(Browser browser, string month, string sourceMonth) =>
        browser.PostAsync($"{Budgets}/{month}/copy-from/{sourceMonth}");

    /// <summary>How many budgets a copy answers it copied, once it answered 200.</summary>
    private static async Task<int> CopiedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CopiedBody>())!.Copied;
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

    /// <summary>The transaction list as a budget row opens it: its category, with its subcategories, in the month.</summary>
    private static async Task<DrillDownBody> TransactionsBehindAsync(Browser browser, Guid categoryId)
    {
        var lastOfThisMonth = FirstOfThisMonth.AddMonths(1).AddDays(-1);
        return (await browser.Http.GetFromJsonAsync<DrillDownBody>(
            $"/api/v1/transactions?categoryId={categoryId}&from={FirstOfThisMonth:yyyy-MM-dd}&to={lastOfThisMonth:yyyy-MM-dd}"))!;
    }

    private sealed record DrillDownBody(ListedBody[] Items, DrillDownTotalBody Total);

    private sealed record ListedBody(Guid Id, string Amount);

    private sealed record DrillDownTotalBody(int Count, SpentBody? Spent);

    private sealed record SpentBody(string Currency, string Amount, int NotConverted);

    private sealed record Identified(Guid Id);

    private sealed record CategoryBody(Guid Id, Guid? ParentId, string Name, string Icon, string Color, bool IsArchived);

    private sealed record BudgetBody(
        Guid CategoryId, CategoryBody Category, string Budgeted, string Spent, string Remaining, int? PercentSpent, string State, int NotConverted,
        string RowVersion, string? Pace = null);

    private sealed record OverallBody(
        string Budgeted, string Spent, string Remaining, int? PercentSpent, string State, string? Pace, int NotConverted, string RowVersion);

    private sealed record CopiedBody(int Copied);

    private sealed record TotalsBody(string Budgeted, string Spent, string Remaining, int NotConverted);

    private sealed record MonthBody(string Month, string Currency, int WarningPercent, List<BudgetBody> Items, TotalsBody Totals, OverallBody? Overall = null, int? PercentOfMonthElapsed = null);
}
