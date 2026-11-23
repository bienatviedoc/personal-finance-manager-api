using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Refunds: an expense flagged as money coming back (spec section 3, rule 2).</summary>
[Collection(ApiCollection.Name)]
public class RefundTests(PfmApi api)
{
    private const string Transactions = "/api/v1/transactions";

    [Fact]
    public async Task An_expense_flagged_as_a_refund_raises_the_account_balance()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var shopping = await CategoryAsync(browser, "Shopping");
        await RecordAsync(browser, new { type = "expense", accountId = everyday, amount = "800000", date = "2026-11-02", categoryId = shopping });

        var response = await browser.PostAsync(
            Transactions, new { type = "expense", isRefund = true, accountId = everyday, amount = "300000", date = "2026-11-09", categoryId = shopping });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var refund = (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
        Assert.Equal(("expense", true, "300000.00"), (refund.Type, refund.IsRefund, refund.Amount));
        Assert.Equal(refund, await GetAsync(browser, refund.Id));
        Assert.Equal("500000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task An_expense_is_not_a_refund_unless_it_says_so_and_the_list_tells_the_two_apart()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var shopping = await CategoryAsync(browser, "Shopping");
        await RecordAsync(browser, new { type = "expense", accountId = everyday, amount = "800000", date = "2026-11-02", categoryId = shopping });
        await RecordAsync(browser, new { type = "expense", isRefund = true, accountId = everyday, amount = "300000", date = "2026-11-09", categoryId = shopping });
        await RecordAsync(browser, new { type = "expense", isRefund = false, accountId = everyday, amount = "1", date = "2026-11-01", categoryId = shopping });

        var listed = (await browser.Http.GetFromJsonAsync<TransactionList>(Transactions))!.Items;

        Assert.Equal([("300000.00", true), ("800000.00", false), ("1.00", false)], listed.Select(transaction => (transaction.Amount, transaction.IsRefund)));
    }

    [Fact]
    public async Task Only_an_expense_can_be_a_refund()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var salary = await CategoryAsync(browser, "Salary");
        var income = await RecordAsync(browser, new { type = "income", accountId = everyday, amount = "500", date = "2026-11-02", categoryId = salary });

        var recorded = await browser.PostAsync(
            Transactions, new { type = "income", isRefund = true, accountId = everyday, amount = "300", date = "2026-11-09", categoryId = salary });
        var edited = await browser.PutAsync(
            $"{Transactions}/{income.Id}", new { type = "income", isRefund = true, accountId = everyday, amount = "500", date = "2026-11-02", categoryId = salary },
            ifMatch: Quoted(income.RowVersion));

        Assert.Equal(HttpStatusCode.BadRequest, recorded.StatusCode);
        Assert.Equal(["isRefund"], await recorded.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.BadRequest, edited.StatusCode);
        Assert.Equal(["isRefund"], await edited.ErrorFieldsAsync());
        Assert.Equal("1500.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task Flagging_an_expense_as_a_refund_or_taking_the_flag_away_moves_the_balance_at_once()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var shopping = await CategoryAsync(browser, "Shopping");
        var expense = await RecordAsync(browser, new { type = "expense", accountId = everyday, amount = "250", date = "2026-11-02", categoryId = shopping });

        var flagged = await EditAsync(browser, expense, new { type = "expense", isRefund = true, accountId = everyday, amount = "250", date = "2026-11-02", categoryId = shopping });
        var balanceAsARefund = await BalanceAsync(browser, everyday);
        var unflagged = await EditAsync(browser, flagged, new { type = "expense", accountId = everyday, amount = "250", date = "2026-11-02", categoryId = shopping });

        Assert.True(flagged.IsRefund);
        Assert.Equal("1250.00", balanceAsARefund);
        Assert.False(unflagged.IsRefund);
        Assert.Equal("750.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task A_deleted_refund_leaves_the_balance()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var shopping = await CategoryAsync(browser, "Shopping");
        var refund = await RecordAsync(browser, new { type = "expense", isRefund = true, accountId = everyday, amount = "250", date = "2026-11-02", categoryId = shopping });

        var response = await browser.DeleteAsync($"{Transactions}/{refund.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("1000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task A_refund_is_not_the_same_request_as_the_expense_it_would_be_without_the_flag()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var shopping = await CategoryAsync(browser, "Shopping");
        var key = Guid.NewGuid().ToString();
        var refund = new { type = "expense", isRefund = true, accountId = everyday, amount = "250", date = "2026-11-02", categoryId = shopping };

        var first = await browser.PostAsync(Transactions, refund, idempotencyKey: key);
        var retry = await browser.PostAsync(Transactions, refund, idempotencyKey: key);
        var asAnExpense = await browser.PostAsync(
            Transactions, new { type = "expense", accountId = everyday, amount = "250", date = "2026-11-02", categoryId = shopping }, idempotencyKey: key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(await first.Content.ReadFromJsonAsync<TransactionBody>(), await retry.Content.ReadFromJsonAsync<TransactionBody>());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, asAnExpense.StatusCode);
        Assert.Equal("1250.00", await BalanceAsync(browser, everyday));
    }

    private static async Task<TransactionBody> RecordAsync(Browser browser, object transaction)
    {
        var response = await browser.PostAsync(Transactions, transaction);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    private static async Task<TransactionBody> EditAsync(Browser browser, TransactionBody from, object transaction)
    {
        var response = await browser.PutAsync($"{Transactions}/{from.Id}", transaction, ifMatch: Quoted(from.RowVersion));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    private static async Task<TransactionBody> GetAsync(Browser browser, Guid id)
    {
        var response = await browser.GetAsync($"{Transactions}/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    private static string Quoted(string rowVersion) => $"\"{rowVersion}\"";

    private sealed record TransactionList(TransactionBody[] Items);

    private sealed record TransactionBody(Guid Id, string Type, bool IsRefund, string Amount, string RowVersion);
}
