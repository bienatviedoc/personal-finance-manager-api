using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Merging one category into another (spec CAT-3).</summary>
[Collection(ApiCollection.Name)]
public class CategoryMergeTests(PfmApi api)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly string ThisMonth = Today.ToString("yyyy-MM");

    private static readonly string LastMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1).ToString("yyyy-MM");

    [Fact]
    public async Task Merging_moves_every_transaction_line_to_the_category_merged_into_and_archives_the_merged_one()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Wallet");
        var (coffee, eatingOut) = (await CategoryAsync(browser, "Coffee"), await CategoryAsync(browser, "Eating out"));
        var latte = await SpendAsync(browser, account, "Coffee", "50000", Today);
        var lunch = await SpendAsync(browser, account, "Eating out", "120000", Today);
        var split = await SplitAsync(browser, account, "300000", Today, ("Groceries", "200000"), ("Coffee", "100000"));
        var deleted = await SpendAsync(browser, account, "Coffee", "40000", Today);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"/api/v1/transactions/{deleted}")).StatusCode);

        var merged = await MergeAsync(browser, coffee, eatingOut);

        Assert.Equal(new MergedBody(Transactions: 2, Budgets: 0), merged);
        Assert.Equal([latte, lunch, split], (await InCategoryAsync(browser, eatingOut)).Select(transaction => transaction.Id).Order());
        Assert.Empty(await InCategoryAsync(browser, coffee));
        var splitNow = await TransactionAsync(browser, split);
        Assert.Equal([(await CategoryAsync(browser, "Groceries"), "200000.00"), (eatingOut, "100000.00")], splitNow.Lines.Select(line => (line.CategoryId, line.Amount)));
        Assert.Equal(eatingOut, (await TransactionAsync(browser, latte)).CategoryId);

        var tree = await CategoriesAsync(browser, includeArchived: true);
        Assert.True(Find(tree, coffee).IsArchived);
        Assert.DoesNotContain(await CategoriesAsync(browser), category => category.Subcategories.Any(sub => sub.Id == coffee));
    }

    [Fact]
    public async Task Merging_moves_the_budgets_and_adds_the_amounts_where_both_have_one_in_the_same_month()
    {
        var (browser, _) = await api.SignedInAsync();
        var (coffee, eatingOut) = (await CategoryAsync(browser, "Coffee"), await CategoryAsync(browser, "Eating out"));
        await SetBudgetAsync(browser, ThisMonth, coffee, "500000");
        await SetBudgetAsync(browser, ThisMonth, eatingOut, "1500000");
        await SetBudgetAsync(browser, LastMonth, coffee, "400000");

        var merged = await MergeAsync(browser, coffee, eatingOut);

        Assert.Equal(new MergedBody(Transactions: 0, Budgets: 2), merged);
        Assert.Equal([(eatingOut, "2000000.00")], (await BudgetsAsync(browser, ThisMonth)).Select(budget => (budget.CategoryId, budget.Budgeted)));
        Assert.Equal([(eatingOut, "400000.00")], (await BudgetsAsync(browser, LastMonth)).Select(budget => (budget.CategoryId, budget.Budgeted)));
    }

    [Fact]
    public async Task Before_merging_the_user_sees_how_many_transactions_and_budgets_will_move()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Wallet");
        var coffee = await CategoryAsync(browser, "Coffee");
        await SpendAsync(browser, account, "Coffee", "50000", Today);
        await SplitAsync(browser, account, "300000", Today, ("Groceries", "200000"), ("Coffee", "100000"));
        await SpendAsync(browser, account, "Groceries", "70000", Today);
        await SetBudgetAsync(browser, ThisMonth, coffee, "500000");

        var response = await browser.GetAsync($"/api/v1/categories/{coffee}/merge-preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new MergedBody(Transactions: 2, Budgets: 1), await response.Content.ReadFromJsonAsync<MergedBody>());
        Assert.False(Find(await CategoriesAsync(browser), coffee).IsArchived);
    }

    [Fact]
    public async Task Only_categories_of_the_same_kind_can_be_merged()
    {
        var (browser, _) = await api.SignedInAsync();
        var (bonus, food) = (await CategoryAsync(browser, "Bonus"), await CategoryAsync(browser, "Food & drink"));

        var response = await browser.PostAsync($"/api/v1/categories/{bonus}/merge", new { targetId = food });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["targetId"], await response.ErrorFieldsAsync());
        Assert.False(Find(await CategoriesAsync(browser), bonus).IsArchived);
    }

    [Fact]
    public async Task The_subcategories_of_a_merged_category_move_under_the_category_merged_into()
    {
        var (browser, _) = await api.SignedInAsync();
        var (food, shopping) = (await CategoryAsync(browser, "Food & drink"), await CategoryAsync(browser, "Shopping"));

        await MergeAsync(browser, food, shopping);

        var tree = await CategoriesAsync(browser);
        Assert.DoesNotContain(tree, category => category.Id == food);
        Assert.Equal(
            ["Clothes", "Coffee", "Eating out", "Electronics", "Groceries", "Household"],
            tree.Single(category => category.Id == shopping).Subcategories.Select(sub => sub.Name).Order());
    }

    [Fact]
    public async Task A_category_with_subcategories_cannot_be_merged_into_a_subcategory_which_would_make_three_levels()
    {
        var (browser, _) = await api.SignedInAsync();
        var (food, taxi) = (await CategoryAsync(browser, "Food & drink"), await CategoryAsync(browser, "Taxi & ride-hailing"));

        var response = await browser.PostAsync($"/api/v1/categories/{food}/merge", new { targetId = taxi });

        Assert.Equal(["targetId"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_merge_that_would_budget_a_category_and_its_parent_in_the_same_month_is_refused_and_changes_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Wallet");
        var (personalCare, food, coffee) = (await CategoryAsync(browser, "Personal care"), await CategoryAsync(browser, "Food & drink"), await CategoryAsync(browser, "Coffee"));
        await SpendAsync(browser, account, "Personal care", "50000", Today);
        await SetBudgetAsync(browser, ThisMonth, personalCare, "500000");
        await SetBudgetAsync(browser, ThisMonth, food, "3000000");

        var response = await browser.PostAsync($"/api/v1/categories/{personalCare}/merge", new { targetId = coffee });

        Assert.Equal(["targetId"], await response.ErrorFieldsAsync());
        Assert.Single(await InCategoryAsync(browser, personalCare));
        Assert.Contains(await BudgetsAsync(browser, ThisMonth), budget => budget.CategoryId == personalCare);
        Assert.False(Find(await CategoriesAsync(browser), personalCare).IsArchived);
    }

    [Fact]
    public async Task A_recurring_rule_on_the_merged_category_goes_on_posting_on_the_one_merged_into()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Wallet");
        var (coffee, eatingOut) = (await CategoryAsync(browser, "Coffee"), await CategoryAsync(browser, "Eating out"));
        var created = await browser.PostAsync("/api/v1/recurring-rules", new
        {
            type = "expense", accountId = account, amount = "50000", categoryId = coffee, frequency = "monthly", startDate = "2030-01-31",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var rule = (await created.Content.ReadFromJsonAsync<RuleBody>())!;

        await MergeAsync(browser, coffee, eatingOut);

        Assert.Equal(eatingOut, (await browser.Http.GetFromJsonAsync<RuleBody>($"/api/v1/recurring-rules/{rule.Id}"))!.CategoryId);
    }

    [Fact]
    public async Task Another_users_categories_can_neither_be_merged_nor_merged_into()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        var (bobsCoffee, alicesCoffee, alicesEatingOut) = (await CategoryAsync(bob, "Coffee"), await CategoryAsync(alice, "Coffee"), await CategoryAsync(alice, "Eating out"));

        var merged = await alice.PostAsync($"/api/v1/categories/{bobsCoffee}/merge", new { targetId = alicesEatingOut });
        var mergedInto = await alice.PostAsync($"/api/v1/categories/{alicesCoffee}/merge", new { targetId = bobsCoffee });
        var previewed = await alice.GetAsync($"/api/v1/categories/{bobsCoffee}/merge-preview");

        Assert.Equal(HttpStatusCode.NotFound, merged.StatusCode);
        Assert.Equal(["targetId"], await mergedInto.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.NotFound, previewed.StatusCode);
        Assert.False(Find(await CategoriesAsync(bob), bobsCoffee).IsArchived);
        Assert.False(Find(await CategoriesAsync(alice), alicesCoffee).IsArchived);
    }

    private static async Task<MergedBody> MergeAsync(Browser browser, Guid categoryId, Guid targetId)
    {
        var response = await browser.PostAsync($"/api/v1/categories/{categoryId}/merge", new { targetId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MergedBody>())!;
    }

    private static async Task SetBudgetAsync(Browser browser, string month, Guid categoryId, string amount)
    {
        var response = await browser.PutAsync($"/api/v1/budgets/{month}/{categoryId}", new { amount });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<List<BudgetBody>> BudgetsAsync(Browser browser, string month) =>
        (await browser.Http.GetFromJsonAsync<BudgetMonthBody>($"/api/v1/budgets?month={month}"))!.Items;

    private static async Task<List<TransactionBody>> InCategoryAsync(Browser browser, Guid categoryId) =>
        (await browser.Http.GetFromJsonAsync<TransactionList>($"/api/v1/transactions?categoryId={categoryId}"))!.Items;

    private static async Task<TransactionBody> TransactionAsync(Browser browser, Guid id) =>
        (await browser.Http.GetFromJsonAsync<TransactionBody>($"/api/v1/transactions/{id}"))!;

    private static async Task<List<CategoryBody>> CategoriesAsync(Browser browser, bool includeArchived = false) =>
        (await browser.Http.GetFromJsonAsync<CategoryList>($"/api/v1/categories?includeArchived={includeArchived}"))!.Items;

    private static CategoryBody Find(List<CategoryBody> tree, Guid id) =>
        tree.Concat(tree.SelectMany(category => category.Subcategories)).Single(category => category.Id == id);

    private sealed record MergedBody(int Transactions, int Budgets);

    private sealed record CategoryBody(Guid Id, string Name, bool IsArchived, List<CategoryBody> Subcategories);

    private sealed record CategoryList(List<CategoryBody> Items);

    private sealed record LineBody(Guid CategoryId, string Amount);

    private sealed record TransactionBody(Guid Id, Guid? CategoryId, List<LineBody> Lines);

    private sealed record TransactionList(List<TransactionBody> Items);

    private sealed record BudgetBody(Guid CategoryId, string Budgeted);

    private sealed record BudgetMonthBody(List<BudgetBody> Items);

    private sealed record RuleBody(Guid Id, Guid CategoryId);
}
