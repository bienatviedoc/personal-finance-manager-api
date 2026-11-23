using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>What quick add starts a new transaction from (spec TXN-8): the account used last, and the category a payee usually takes.</summary>
[Collection(ApiCollection.Name)]
public class QuickAddTests(PfmApi api)
{
    private const string Transactions = "/api/v1/transactions";
    private const string QuickAdd = "/api/v1/transactions/quick-add";

    [Fact]
    public async Task Quick_add_starts_from_the_account_the_last_transaction_was_recorded_on()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var cash = await AddAccountAsync(browser, "Cash");
        var groceries = await CategoryAsync(browser, "Groceries");

        Assert.Null((await QuickAddAsync(browser)).LastAccountId);

        // Recorded last, though dated earliest: what counts is when it was recorded.
        await RecordAsync(browser, everyday, groceries, date: "2026-11-10");
        await RecordAsync(browser, cash, groceries, date: "2026-11-01");

        Assert.Equal(cash, (await QuickAddAsync(browser)).LastAccountId);
    }

    [Fact]
    public async Task An_archived_account_is_passed_over_for_the_one_used_before_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var closed = await AddAccountAsync(browser, "Closed");
        var groceries = await CategoryAsync(browser, "Groceries");
        await RecordAsync(browser, everyday, groceries);
        await RecordAsync(browser, closed, groceries);

        (await browser.DeleteAsync($"/api/v1/accounts/{closed}")).EnsureSuccessStatusCode();

        Assert.Equal(everyday, (await QuickAddAsync(browser)).LastAccountId);
    }

    [Fact]
    public async Task A_deleted_transaction_no_longer_says_which_account_was_used_last()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var cash = await AddAccountAsync(browser, "Cash");
        var groceries = await CategoryAsync(browser, "Groceries");
        await RecordAsync(browser, everyday, groceries);
        var mistake = await RecordAsync(browser, cash, groceries);

        (await browser.DeleteAsync($"{Transactions}/{mistake}")).EnsureSuccessStatusCode();

        Assert.Equal(everyday, (await QuickAddAsync(browser)).LastAccountId);
    }

    [Fact]
    public async Task A_payee_used_before_suggests_the_category_most_often_used_for_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await CategoryAsync(browser, "Groceries");
        var eatingOut = await CategoryAsync(browser, "Eating out");
        var transport = await CategoryAsync(browser, "Transport");
        await RecordAsync(browser, everyday, groceries, payee: "Co.opmart");
        await RecordAsync(browser, everyday, groceries, payee: "Co.opmart");
        await RecordAsync(browser, everyday, eatingOut, payee: "Co.opmart");
        await RecordAsync(browser, everyday, transport, payee: "Grab");
        await RecordAsync(browser, everyday, transport, payee: "Grab");
        await RecordAsync(browser, everyday, transport, payee: "Grab");

        // However it is typed: a payee is the same in any case, with or without spaces around it.
        Assert.Equal(groceries, (await QuickAddAsync(browser, "?type=expense&payee=Co.opmart")).SuggestedCategoryId);
        Assert.Equal(groceries, (await QuickAddAsync(browser, "?type=expense&payee=%20CO.OPMART%20")).SuggestedCategoryId);
        Assert.Equal(transport, (await QuickAddAsync(browser, "?type=expense&payee=grab")).SuggestedCategoryId);
    }

    [Fact]
    public async Task Nothing_is_suggested_for_a_payee_never_used_or_for_part_of_one_or_without_a_payee()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await RecordAsync(browser, everyday, await CategoryAsync(browser, "Groceries"), payee: "Co.opmart");

        Assert.Null((await QuickAddAsync(browser, "?type=expense&payee=Big%20C")).SuggestedCategoryId);
        Assert.Null((await QuickAddAsync(browser, "?type=expense&payee=Co")).SuggestedCategoryId);
        // The characters a pattern would read as "anything" are only themselves.
        Assert.Null((await QuickAddAsync(browser, "?type=expense&payee=Co%25")).SuggestedCategoryId);
        Assert.Null((await QuickAddAsync(browser, "?type=expense&payee=Co_opmart")).SuggestedCategoryId);
        Assert.Null((await QuickAddAsync(browser, "?type=expense")).SuggestedCategoryId);
        Assert.Null((await QuickAddAsync(browser, "?type=expense&payee=%20")).SuggestedCategoryId);
    }

    [Fact]
    public async Task Categories_used_equally_often_suggest_the_one_used_last()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await CategoryAsync(browser, "Groceries");
        var eatingOut = await CategoryAsync(browser, "Eating out");
        await RecordAsync(browser, everyday, eatingOut, payee: "Co.opmart");
        await RecordAsync(browser, everyday, groceries, payee: "Co.opmart");

        Assert.Equal(groceries, (await QuickAddAsync(browser, "?type=expense&payee=Co.opmart")).SuggestedCategoryId);
    }

    [Fact]
    public async Task The_suggestion_is_a_category_of_the_kind_the_type_takes()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var salary = await CategoryAsync(browser, "Salary");
        var shopping = await CategoryAsync(browser, "Shopping");
        await RecordAsync(browser, everyday, shopping, payee: "Acme");
        await RecordAsync(browser, everyday, shopping, payee: "Acme");
        await RecordAsync(browser, everyday, salary, payee: "Acme", type: "income");

        Assert.Equal(shopping, (await QuickAddAsync(browser, "?type=expense&payee=Acme")).SuggestedCategoryId);
        Assert.Equal(salary, (await QuickAddAsync(browser, "?type=income&payee=Acme")).SuggestedCategoryId);
        // With no type, whichever was used most.
        Assert.Equal(shopping, (await QuickAddAsync(browser, "?payee=Acme")).SuggestedCategoryId);
    }

    [Fact]
    public async Task An_archived_category_and_a_deleted_transaction_suggest_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await CategoryAsync(browser, "Groceries");
        var eatingOut = await CategoryAsync(browser, "Eating out");
        var transport = await CategoryAsync(browser, "Transport");
        await RecordAsync(browser, everyday, eatingOut, payee: "Co.opmart");
        await RecordAsync(browser, everyday, groceries, payee: "Co.opmart");
        await RecordAsync(browser, everyday, groceries, payee: "Co.opmart");
        var first = await RecordAsync(browser, everyday, transport, payee: "Co.opmart");
        var second = await RecordAsync(browser, everyday, transport, payee: "Co.opmart");
        var third = await RecordAsync(browser, everyday, transport, payee: "Co.opmart");

        foreach (var deleted in new[] { first, second, third })
        {
            (await browser.DeleteAsync($"{Transactions}/{deleted}")).EnsureSuccessStatusCode();
        }

        Assert.Equal(groceries, (await QuickAddAsync(browser, "?type=expense&payee=Co.opmart")).SuggestedCategoryId);

        (await browser.DeleteAsync($"/api/v1/categories/{groceries}")).EnsureSuccessStatusCode();

        Assert.Equal(eatingOut, (await QuickAddAsync(browser, "?type=expense&payee=Co.opmart")).SuggestedCategoryId);
    }

    [Fact]
    public async Task Each_line_of_a_split_counts_as_a_use_of_its_category()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await CategoryAsync(browser, "Groceries");
        var eatingOut = await CategoryAsync(browser, "Eating out");
        var household = await CategoryAsync(browser, "Household");
        await RecordSplitAsync(browser, everyday, "Co.opmart", groceries, household);
        await RecordSplitAsync(browser, everyday, "Co.opmart", eatingOut, household);

        Assert.Equal(household, (await QuickAddAsync(browser, "?type=expense&payee=Co.opmart")).SuggestedCategoryId);
    }

    [Fact]
    public async Task A_refund_is_money_coming_back_and_says_nothing_about_what_a_payee_is_for()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await CategoryAsync(browser, "Groceries");
        var household = await CategoryAsync(browser, "Household");
        await RecordAsync(browser, everyday, groceries, payee: "Co.opmart");
        await RecordAsync(browser, everyday, household, payee: "Co.opmart", isRefund: true);
        await RecordAsync(browser, everyday, household, payee: "Co.opmart", isRefund: true);

        Assert.Equal(groceries, (await QuickAddAsync(browser, "?type=expense&payee=Co.opmart")).SuggestedCategoryId);
    }

    [Fact]
    public async Task What_another_user_recorded_is_never_suggested()
    {
        var (alice, _) = await api.SignedInAsync();
        await RecordAsync(alice, await AddAccountAsync(alice, "Everyday"), await CategoryAsync(alice, "Groceries"), payee: "Co.opmart");
        var (bob, _) = await api.SignedInAsync();
        await AddAccountAsync(bob, "Everyday");

        Assert.Equal(new QuickAddBody(null, null), await QuickAddAsync(bob, "?type=expense&payee=Co.opmart"));
    }

    [Theory]
    [InlineData("?type=transfer", "type")]
    [InlineData("?type=1", "type")]
    [InlineData("?payee=", "payee", 201)]
    public async Task A_type_the_app_does_not_know_or_a_payee_longer_than_one_can_be_is_refused_by_name(string query, string field, int payeeLength = 0)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync(QuickAdd + query + new string('p', payeeLength));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_is_told_nothing()
    {
        var response = await api.CreateBrowser().GetAsync(QuickAdd);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<QuickAddBody> QuickAddAsync(Browser browser, string query = "")
    {
        var response = await browser.GetAsync(QuickAdd + query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QuickAddBody>())!;
    }

    /// <summary>Records a transaction and answers its id.</summary>
    private static async Task<Guid> RecordAsync(
        Browser browser, Guid accountId, Guid categoryId, string? payee = null, string type = "expense", string date = "2026-11-02",
        bool isRefund = false)
    {
        var response = await browser.PostAsync(Transactions, new { type, isRefund, accountId, amount = "100", date, categoryId, payee });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    /// <summary>Records an expense split evenly across two categories.</summary>
    private static async Task RecordSplitAsync(Browser browser, Guid accountId, string payee, Guid first, Guid second)
    {
        var response = await browser.PostAsync(Transactions, new
        {
            type = "expense", accountId, amount = "100", date = "2026-11-02", payee,
            lines = new object[] { new { categoryId = first, amount = "60" }, new { categoryId = second, amount = "40" } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private sealed record Identified(Guid Id);

    private sealed record QuickAddBody(Guid? LastAccountId, Guid? SuggestedCategoryId);
}
