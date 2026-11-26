using System.Net;
using System.Net.Http.Json;
using Pfm.Infrastructure.SampleData;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Bulk edit and bulk delete: one action on the transactions a user selects (spec TXN-7, section 11).</summary>
[Collection(ApiCollection.Name)]
public class BulkEditTests(PfmApi api)
{
    private const string Bulk = "/api/v1/transactions/bulk";
    private static readonly DateOnly November2 = new(2026, 11, 2);

    [Fact]
    public async Task Selected_expenses_are_given_one_category_in_one_action()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var first = await SpendAsync(browser, everyday, "Groceries", "1000", November2);
        var second = await SpendAsync(browser, everyday, "Transport", "2000", November2);
        var untouched = await SpendAsync(browser, everyday, "Groceries", "3000", November2);
        var dining = await CategoryAsync(browser, "Eating out");

        var response = await browser.PostAsync(Bulk, new { action = "setCategory", transactionIds = new[] { first, second }, categoryId = dining });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<BulkBody>())!.Count);
        Assert.Equal(dining, (await GetAsync(browser, first)).CategoryId);
        Assert.Equal(dining, (await GetAsync(browser, second)).CategoryId);
        Assert.Equal(await CategoryAsync(browser, "Groceries"), (await GetAsync(browser, untouched)).CategoryId);
    }

    [Fact]
    public async Task A_category_of_the_wrong_kind_changes_nothing_and_names_each_row_it_cannot_be_given_to()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        var groceries = await SpendAsync(browser, everyday, "Groceries", "1000", November2);
        var salary = await RecordAsync(browser, "income", everyday, "Salary", "5000", November2);
        var split = await SplitAsync(browser, everyday, "3000", November2, ("Groceries", "1000"), ("Transport", "2000"));
        await TransferAsync(browser, everyday, savings, "500", November2);
        var leg = (await ListAsync(browser, $"?type=transferOut")).Single().Id;

        var response = await browser.PostAsync(
            Bulk, new { action = "setCategory", transactionIds = new[] { groceries, salary, split, leg }, categoryId = await CategoryAsync(browser, "Eating out") });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("urn:pfm:problem:bulk-action-refused", await response.ProblemTypeAsync());
        var refused = (await response.Content.ReadFromJsonAsync<RefusedBody>())!.Refused;
        Assert.Equal(
            [(salary, "categoryKind"), (split, "split"), (leg, "transferLeg")],
            refused.Select(row => (row.TransactionId, row.Reason)).OrderBy(row => row.Reason));
        Assert.All(refused, row => Assert.False(string.IsNullOrWhiteSpace(row.Message)));
        Assert.Equal(await CategoryAsync(browser, "Groceries"), (await GetAsync(browser, groceries)).CategoryId);
    }

    [Fact]
    public async Task A_tag_is_added_to_or_removed_from_every_selected_transaction()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var first = await SpendAsync(browser, everyday, "Groceries", "1000", November2);
        var second = await SpendAsync(browser, everyday, "Groceries", "2000", November2);

        var added = await browser.PostAsync(Bulk, new { action = "addTag", transactionIds = new[] { first, second }, tag = " Japan-Trip " });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(["japan-trip"], (await GetAsync(browser, first)).Tags);
        Assert.Equal(["japan-trip"], (await GetAsync(browser, second)).Tags);

        var removed = await browser.PostAsync(Bulk, new { action = "removeTag", transactionIds = new[] { first }, tag = "japan-trip" });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Empty((await GetAsync(browser, first)).Tags);
        Assert.Equal(["japan-trip"], (await GetAsync(browser, second)).Tags);
    }

    [Fact]
    public async Task A_transaction_with_twenty_tags_already_is_refused_another()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var response = await browser.PostAsync("/api/v1/transactions", new
        {
            type = "expense", accountId = everyday, amount = "1000", date = "2026-11-02", categoryId = await CategoryAsync(browser, "Groceries"),
            tags = Enumerable.Range(1, 20).Select(number => $"tag-{number}").ToArray(),
        });
        var full = (await response.Content.ReadFromJsonAsync<TransactionBody>())!.Id;

        var refused = await browser.PostAsync(Bulk, new { action = "addTag", transactionIds = new[] { full }, tag = "one-more" });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("tooManyTags", Assert.Single((await refused.Content.ReadFromJsonAsync<RefusedBody>())!.Refused).Reason);
    }

    [Fact]
    public async Task Selected_transactions_move_to_another_account_and_both_balances_follow()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        var groceries = await SpendAsync(browser, everyday, "Groceries", "1000", November2);
        var split = await SplitAsync(browser, everyday, "3000", November2, ("Groceries", "1000"), ("Transport", "2000"));

        var response = await browser.PostAsync(Bulk, new { action = "moveToAccount", transactionIds = new[] { groceries, split }, accountId = savings });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("0.00", await BalanceAsync(browser, everyday));
        Assert.Equal("-4000.00", await BalanceAsync(browser, savings));
        Assert.Equal(2, (await GetAsync(browser, split)).Lines.Length);
    }

    [Fact]
    public async Task A_move_is_refused_for_a_transaction_in_another_currency_a_leg_of_a_transfer_or_a_reconciled_one()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        var dollars = await AddAccountAsync(browser, "Dollars", "USD");
        var inDollars = await SpendAsync(browser, dollars, "Groceries", "10.00", November2);
        var reconciled = await SpendAsync(browser, everyday, "Groceries", "1000", November2);
        await ReconcileAsync(browser, everyday, "-1000", reconciled);
        await TransferAsync(browser, savings, everyday, "500", November2);
        var leg = (await ListAsync(browser, "?type=transferIn")).Single().Id;

        var response = await browser.PostAsync(Bulk, new { action = "moveToAccount", transactionIds = new[] { inDollars, reconciled, leg }, accountId = savings });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            [(inDollars, "otherCurrency"), (reconciled, "reconciled"), (leg, "transferLeg")],
            (await response.Content.ReadFromJsonAsync<RefusedBody>())!.Refused.Select(row => (row.TransactionId, row.Reason)).OrderBy(row => row.Reason));
        Assert.Equal(dollars, (await GetAsync(browser, inDollars)).AccountId);
    }

    [Fact]
    public async Task Selected_transactions_are_deleted_together_a_leg_taking_its_transfer_with_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", openingBalance: "10000");
        var savings = await AddAccountAsync(browser, "Savings");
        var groceries = await SpendAsync(browser, everyday, "Groceries", "1000", November2);
        await TransferAsync(browser, everyday, savings, "500", November2);
        var leg = (await ListAsync(browser, "?type=transferOut")).Single().Id;
        var kept = await SpendAsync(browser, everyday, "Groceries", "2000", November2);

        var response = await browser.PostAsync(Bulk, new { action = "delete", transactionIds = new[] { groceries, leg } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([kept], (await ListAsync(browser)).Select(transaction => transaction.Id));
        Assert.Equal("8000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("0.00", await BalanceAsync(browser, savings));
    }

    [Fact]
    public async Task A_delete_with_a_reconciled_transaction_among_them_deletes_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var reconciled = await SpendAsync(browser, everyday, "Groceries", "1000", November2);
        await ReconcileAsync(browser, everyday, "-1000", reconciled);
        var other = await SpendAsync(browser, everyday, "Groceries", "2000", November2);

        var response = await browser.PostAsync(Bulk, new { action = "delete", transactionIds = new[] { reconciled, other } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var refused = Assert.Single((await response.Content.ReadFromJsonAsync<RefusedBody>())!.Refused);
        Assert.Equal((reconciled, "reconciled"), (refused.TransactionId, refused.Reason));
        Assert.Equal(2, (await ListAsync(browser)).Length);
    }

    [Fact]
    public async Task Every_transaction_that_passes_a_filter_can_be_selected_at_once()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        var first = await SpendAsync(browser, everyday, "Groceries", "1000", November2, payee: "Co.op Mart");
        var second = await SpendAsync(browser, everyday, "Groceries", "2000", November2, payee: "Co.op Mart");
        var otherPayee = await SpendAsync(browser, everyday, "Groceries", "3000", November2, payee: "Big C");
        var otherAccount = await SpendAsync(browser, savings, "Groceries", "4000", November2, payee: "Co.op Mart");

        var response = await browser.PostAsync(Bulk, new { action = "addTag", filter = new { accountId = everyday, q = "co.op" }, tag = "groceries-run" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<BulkBody>())!.Count);
        Assert.Equal(
            [first, second], (await ListAsync(browser, "?tag=groceries-run")).Select(transaction => transaction.Id).Order());
        Assert.Empty((await GetAsync(browser, otherPayee)).Tags);
        Assert.Empty((await GetAsync(browser, otherAccount)).Tags);
    }

    [Fact]
    public async Task A_filter_that_more_than_a_thousand_transactions_pass_is_refused_as_too_many_to_select_and_nothing_changes()
    {
        var email = PfmApi.NewEmail();
        await api.Services.SeedSampleDataAsync(new SampleDataOptions(email, PfmApi.Password, Transactions: 1_001));
        var browser = api.CreateBrowser();
        (await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password })).EnsureSuccessStatusCode();

        var response = await browser.PostAsync(Bulk, new { action = "addTag", filter = new { }, tag = "everything" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("urn:pfm:problem:bulk-selection-too-large", await response.ProblemTypeAsync());
        Assert.Equal(1_000, (await response.Content.ReadFromJsonAsync<TooLargeBody>())!.Max);
        Assert.Empty(await ListAsync(browser, "?tag=everything"));
    }

    [Fact]
    public async Task A_bulk_action_names_what_it_selects_and_what_it_does_or_is_refused_on_that_field()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await SpendAsync(browser, everyday, "Groceries", "1000", November2);
        var ids = new[] { groceries };

        Assert.Equal(["transactionIds"], await (await browser.PostAsync(Bulk, new { action = "delete" })).ErrorFieldsAsync());
        Assert.Equal(["transactionIds"], await (await browser.PostAsync(Bulk, new { action = "delete", transactionIds = Array.Empty<Guid>() })).ErrorFieldsAsync());
        Assert.Equal(
            ["transactionIds"],
            await (await browser.PostAsync(Bulk, new { action = "delete", transactionIds = ids, filter = new { accountId = everyday } })).ErrorFieldsAsync());
        Assert.Equal(["action"], await (await browser.PostAsync(Bulk, new { action = "paint", transactionIds = ids })).ErrorFieldsAsync());
        Assert.Equal(["categoryId"], await (await browser.PostAsync(Bulk, new { action = "setCategory", transactionIds = ids })).ErrorFieldsAsync());
        Assert.Equal(["categoryId"], await (await browser.PostAsync(Bulk, new { action = "setCategory", transactionIds = ids, categoryId = Guid.NewGuid() })).ErrorFieldsAsync());
        Assert.Equal(["accountId"], await (await browser.PostAsync(Bulk, new { action = "moveToAccount", transactionIds = ids, accountId = Guid.NewGuid() })).ErrorFieldsAsync());
        Assert.Equal(["tag"], await (await browser.PostAsync(Bulk, new { action = "addTag", transactionIds = ids, tag = " " })).ErrorFieldsAsync());
        Assert.Equal(["filter.min"], await (await browser.PostAsync(Bulk, new { action = "delete", filter = new { min = "lots" } })).ErrorFieldsAsync());
        Assert.Equal(
            ["transactionIds"],
            await (await browser.PostAsync(Bulk, new { action = "delete", transactionIds = Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToArray() })).ErrorFieldsAsync());
        Assert.Single(await ListAsync(browser));
    }

    [Fact]
    public async Task Another_users_transaction_is_not_found_and_nothing_changes()
    {
        var (owner, _) = await api.SignedInAsync();
        var theirs = await SpendAsync(owner, await AddAccountAsync(owner, "Theirs"), "Groceries", "1000", November2);
        var (browser, _) = await api.SignedInAsync();
        var mine = await SpendAsync(browser, await AddAccountAsync(browser, "Mine"), "Groceries", "1000", November2);

        var response = await browser.PostAsync(Bulk, new { action = "delete", transactionIds = new[] { mine, theirs } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var refused = Assert.Single((await response.Content.ReadFromJsonAsync<RefusedBody>())!.Refused);
        Assert.Equal((theirs, "notFound"), (refused.TransactionId, refused.Reason));
        Assert.Single(await ListAsync(browser));
        Assert.Single(await ListAsync(owner));
    }

    private static async Task ReconcileAsync(Browser browser, Guid accountId, string statementBalance, params Guid[] ticked)
    {
        var started = await browser.PostAsync($"/api/v1/accounts/{accountId}/reconciliations", new { statementDate = "2026-11-02", statementBalance });
        var reconciliation = (await started.Content.ReadFromJsonAsync<Identified>())!.Id;
        foreach (var transaction in ticked)
        {
            (await browser.PutAsync($"/api/v1/reconciliations/{reconciliation}/transactions/{transaction}", new { })).EnsureSuccessStatusCode();
        }

        (await browser.PostAsync($"/api/v1/reconciliations/{reconciliation}/complete")).EnsureSuccessStatusCode();
    }

    private static async Task<TransactionBody> GetAsync(Browser browser, Guid id) =>
        (await browser.Http.GetFromJsonAsync<TransactionBody>($"/api/v1/transactions/{id}"))!;

    private static async Task<TransactionBody[]> ListAsync(Browser browser, string query = "") =>
        (await browser.Http.GetFromJsonAsync<TransactionList>("/api/v1/transactions" + query))!.Items;

    private sealed record Identified(Guid Id);

    private sealed record TransactionList(TransactionBody[] Items);

    private sealed record TransactionBody(Guid Id, Guid AccountId, Guid? CategoryId, string[] Tags, LineBody[] Lines);

    private sealed record LineBody(Guid CategoryId, string Amount);

    private sealed record BulkBody(int Count);

    private sealed record TooLargeBody(int Max);

    private sealed record RefusedBody(RefusedRow[] Refused);

    private sealed record RefusedRow(Guid TransactionId, string Reason, string Message);
}
