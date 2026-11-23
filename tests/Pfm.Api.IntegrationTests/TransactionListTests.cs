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
