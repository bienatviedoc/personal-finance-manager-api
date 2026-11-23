using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Finding transactions: the filters, search, sorting and totals of <c>GET /transactions</c> (spec TXN-2, TXN-3).</summary>
[Collection(ApiCollection.Name)]
public class TransactionListTests(PfmApi api)
{
    private const string Transactions = "/api/v1/transactions";

    [Fact]
    public async Task The_list_can_be_narrowed_to_a_date_range_that_includes_both_of_its_ends()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await AddAsync(browser, "expense", everyday, "1", "2026-10-31");
        await AddAsync(browser, "expense", everyday, "2", "2026-11-01");
        await AddAsync(browser, "expense", everyday, "3", "2026-11-15");
        await AddAsync(browser, "expense", everyday, "4", "2026-11-16");

        Assert.Equal(["3.00", "2.00"], await AmountsAsync(browser, "?from=2026-11-01&to=2026-11-15"));
        Assert.Equal(["4.00", "3.00"], await AmountsAsync(browser, "?from=2026-11-15"));
        Assert.Equal(["1.00"], await AmountsAsync(browser, "?to=2026-10-31"));
    }

    [Fact]
    public async Task The_list_can_be_narrowed_to_an_account_a_type_and_a_range_of_amounts_that_includes_both_of_its_ends()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "USD");
        var savings = await AddAccountAsync(browser, "Savings", "USD");
        await AddAsync(browser, "expense", everyday, "100");
        await AddAsync(browser, "income", everyday, "200");
        await AddAsync(browser, "expense", savings, "300");
        await AddAsync(browser, "income", savings, "400.5");

        Assert.Equal(["400.50", "300.00"], await AmountsAsync(browser, $"?accountId={savings}"));
        Assert.Equal(["400.50", "200.00"], await AmountsAsync(browser, "?type=income"));
        Assert.Equal(["300.00", "200.00"], await AmountsAsync(browser, "?min=200&max=300.00"));
        Assert.Equal(["400.50"], await AmountsAsync(browser, "?min=300.01"));
        Assert.Equal(["100.00"], await AmountsAsync(browser, "?max=199.99"));
        Assert.Equal(["300.00"], await AmountsAsync(browser, $"?accountId={savings}&type=expense&min=1&max=1000&from=2026-11-02&to=2026-11-02"));
        Assert.Empty(await AmountsAsync(browser, $"?accountId={Guid.NewGuid()}"));
    }

    [Fact]
    public async Task Narrowing_to_a_category_lists_its_subcategories_transactions_too()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var food = await CategoryAsync(browser, "Food & drink");
        var groceries = await CategoryAsync(browser, "Groceries");
        await AddAsync(browser, "expense", everyday, "1", category: "Food & drink");
        await AddAsync(browser, "expense", everyday, "2", category: "Groceries");
        await AddAsync(browser, "expense", everyday, "3", category: "Transport");
        await AddAsync(browser, "income", everyday, "4", category: "Salary");

        Assert.Equal(["2.00", "1.00"], await AmountsAsync(browser, $"?categoryId={food}"));
        Assert.Equal(["2.00"], await AmountsAsync(browser, $"?categoryId={groceries}"));
    }

    [Fact]
    public async Task Searching_finds_the_text_anywhere_in_the_payee_or_the_note_whatever_its_case()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await AddAsync(browser, "expense", everyday, "1", payee: "Co.opmart Nguyễn Đình Chiểu");
        await AddAsync(browser, "expense", everyday, "2", payee: "Grab", note: "ride to the co.opmart");
        await AddAsync(browser, "expense", everyday, "3", payee: "Highlands Coffee", note: "50% off");
        await AddAsync(browser, "expense", everyday, "4");

        Assert.Equal(["2.00", "1.00"], await AmountsAsync(browser, "?q=OPMART"));
        Assert.Equal(["1.00"], await AmountsAsync(browser, $"?q={Uri.EscapeDataString("nguyễn đình")}"));
        Assert.Equal(["3.00"], await AmountsAsync(browser, "?q=%20coffee%20"));
        Assert.Equal(["3.00"], await AmountsAsync(browser, "?q=50%25"));
        Assert.Empty(await AmountsAsync(browser, "?q=co_opmart"));
        Assert.Equal(["2.00"], await AmountsAsync(browser, "?q=co.op&min=2"));
        Assert.Equal(4, (await AmountsAsync(browser, "?q=")).Length);
    }

    private static async Task<TransactionBody> AddAsync(
        Browser browser, string type, Guid accountId, string amount, string date = "2026-11-02", string? category = null, string? payee = null,
        string? note = null)
    {
        var categoryId = await CategoryAsync(browser, category ?? (type == "income" ? "Salary" : "Groceries"));
        var response = await browser.PostAsync(Transactions, new { type, accountId, amount, date, categoryId, payee, note });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    private static async Task<TransactionList> ListAsync(Browser browser, string query = "")
    {
        var response = await browser.GetAsync(Transactions + query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionList>())!;
    }

    /// <summary>The amounts of the transactions the list answers, in its order: what tells them apart in these tests.</summary>
    private static async Task<string[]> AmountsAsync(Browser browser, string query) =>
        [.. (await ListAsync(browser, query)).Items.Select(transaction => transaction.Amount)];

    private sealed record TransactionList(TransactionBody[] Items, string? NextCursor);

    private sealed record TransactionBody(
        Guid Id, string Type, Guid AccountId, string Amount, string Currency, string Date, Guid CategoryId, string? Payee, string? Note, string RowVersion);
}
