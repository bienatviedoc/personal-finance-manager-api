using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ReconciliationTests(PfmApi api)
{
    private const string Reconciliations = "/api/v1/reconciliations";
    private static readonly DateOnly October5 = new(2026, 10, 5);
    private static readonly DateOnly October20 = new(2026, 10, 20);
    private static readonly DateOnly StatementDate = new(2026, 10, 31);

    [Fact]
    public async Task A_reconciliation_is_started_on_an_account_with_a_statement_date_and_closing_balance()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");

        var response = await browser.PostAsync($"/api/v1/accounts/{everyday}/reconciliations", new { statementDate = "2026-10-31", statementBalance = "4500000" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var started = (await response.Content.ReadFromJsonAsync<ReconciliationBody>())!;
        Assert.Equal(
            new ReconciliationBody(started.Id, everyday, "2026-10-31", "4500000.00", "VND", "inProgress", null, "1000000.00", "0.00", "3500000.00", 0),
            started);
        Assert.Equal($"{Reconciliations}/{started.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(started, await browser.Http.GetFromJsonAsync<ReconciliationBody>($"{Reconciliations}/{started.Id}"));
        var listed = (await browser.Http.GetFromJsonAsync<ReconciliationList>($"/api/v1/accounts/{everyday}/reconciliations"))!;
        Assert.Equal([started], listed.Items);
    }

    [Fact]
    public async Task A_statement_needs_a_date_not_in_the_future_and_a_balance_in_the_accounts_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "USD");

        var missing = await browser.PostAsync($"/api/v1/accounts/{everyday}/reconciliations", new { });
        var future = await browser.PostAsync($"/api/v1/accounts/{everyday}/reconciliations", new { statementDate = "2999-01-01", statementBalance = "10.00" });
        var tooManyDecimals = await browser.PostAsync($"/api/v1/accounts/{everyday}/reconciliations", new { statementDate = "2026-10-31", statementBalance = "10.005" });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(["statementBalance", "statementDate"], await missing.ErrorFieldsAsync());
        Assert.Equal(["statementDate"], await future.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.BadRequest, tooManyDecimals.StatusCode);
        Assert.Equal(["statementBalance"], await tooManyDecimals.ErrorFieldsAsync());
    }

    [Fact]
    public async Task An_account_that_is_not_the_users_has_no_reconciliation()
    {
        var (owner, _) = await api.SignedInAsync();
        var (stranger, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(owner, "Everyday");
        var reconciliation = await StartAsync(owner, everyday, "0");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/v1/accounts/{everyday}/reconciliations", new { statementDate = "2026-10-31", statementBalance = "0" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"{Reconciliations}/{reconciliation}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"{Reconciliations}/{reconciliation}/complete")).StatusCode);
    }

    [Fact]
    public async Task Ticking_transactions_updates_the_difference_between_the_statement_balance_and_the_ticked_total()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var salary = await RecordAsync(browser, "income", everyday, "Salary", "5000000", October5);
        var groceries = await SpendAsync(browser, everyday, "Groceries", "500000", October20);
        var reconciliation = await StartAsync(browser, everyday, "5500000");

        var afterSalary = await TickAsync(browser, reconciliation, salary);
        var afterGroceries = await TickAsync(browser, reconciliation, groceries);

        Assert.Equal(("5000000.00", "-500000.00", 1), (afterSalary.TickedTotal, afterSalary.Difference, afterSalary.TickedCount));
        Assert.Equal(("4500000.00", "0.00", 2), (afterGroceries.TickedTotal, afterGroceries.Difference, afterGroceries.TickedCount));

        var unticked = (await (await browser.DeleteAsync($"{Reconciliations}/{reconciliation}/transactions/{groceries}")).Content.ReadFromJsonAsync<ReconciliationBody>())!;
        Assert.Equal(("5000000.00", "-500000.00"), (unticked.TickedTotal, unticked.Difference));
    }

    [Fact]
    public async Task The_transactions_to_tick_are_the_accounts_own_up_to_the_statement_date_that_are_not_yet_reconciled()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var savings = await AddAccountAsync(browser, "Savings");
        var salary = await RecordAsync(browser, "income", everyday, "Salary", "5000000", October5);
        var groceries = await SpendAsync(browser, everyday, "Groceries", "500000", October20, payee: "Co.op Mart");
        await SpendAsync(browser, everyday, "Groceries", "1000", StatementDate.AddDays(1));
        await SpendAsync(browser, savings, "Groceries", "2000", October5);
        var reconciliation = await StartAsync(browser, everyday, "5500000");
        await TickAsync(browser, reconciliation, groceries);

        var listed = (await browser.Http.GetFromJsonAsync<TransactionsToTick>($"{Reconciliations}/{reconciliation}/transactions"))!.Items;

        Assert.Equal(
            [
                new TransactionToTick(salary, "income", "2026-10-05", "5000000.00", "VND", false, null, false),
                new TransactionToTick(groceries, "expense", "2026-10-20", "500000.00", "VND", false, "Co.op Mart", true),
            ],
            listed);
    }

    [Fact]
    public async Task Only_a_transaction_on_the_account_dated_on_or_before_the_statement_date_can_be_ticked()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        var elsewhere = await SpendAsync(browser, savings, "Groceries", "1000", October5);
        var later = await SpendAsync(browser, everyday, "Groceries", "1000", StatementDate.AddDays(1));
        var reconciliation = await StartAsync(browser, everyday, "0");

        var onAnotherAccount = await browser.PutAsync($"{Reconciliations}/{reconciliation}/transactions/{elsewhere}", new { });
        var afterTheStatement = await browser.PutAsync($"{Reconciliations}/{reconciliation}/transactions/{later}", new { });
        var unknown = await browser.PutAsync($"{Reconciliations}/{reconciliation}/transactions/{Guid.CreateVersion7()}", new { });

        Assert.Equal(HttpStatusCode.BadRequest, onAnotherAccount.StatusCode);
        Assert.Equal(["transactionId"], await onAnotherAccount.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.BadRequest, afterTheStatement.StatusCode);
        Assert.Equal(["transactionId"], await afterTheStatement.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task It_completes_only_when_the_difference_is_exactly_zero_and_never_adds_an_adjustment()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var salary = await RecordAsync(browser, "income", everyday, "Salary", "5000000", October5);
        var reconciliation = await StartAsync(browser, everyday, "5500000");
        await TickAsync(browser, reconciliation, salary);

        var refused = await browser.PostAsync($"{Reconciliations}/{reconciliation}/complete");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("urn:pfm:problem:reconciliation-difference-not-zero", await refused.ProblemTypeAsync());
        Assert.Equal("inProgress", (await GetAsync(browser, reconciliation)).Status);
        Assert.Equal("6000000.00", await BalanceAsync(browser, everyday));
        Assert.Single((await browser.Http.GetFromJsonAsync<TransactionsToTick>($"{Reconciliations}/{reconciliation}/transactions"))!.Items);

        var groceries = await SpendAsync(browser, everyday, "Groceries", "500000", October20);
        await TickAsync(browser, reconciliation, groceries);
        var completed = await browser.PostAsync($"{Reconciliations}/{reconciliation}/complete");

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var body = (await completed.Content.ReadFromJsonAsync<ReconciliationBody>())!;
        Assert.Equal(("completed", "0.00"), (body.Status, body.Difference));
        Assert.NotNull(body.CompletedAt);
    }

    [Fact]
    public async Task Reconciled_transactions_are_locked_their_amount_date_and_account_cannot_change()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        var groceries = await SpendAsync(browser, everyday, "Groceries", "500000", October20);
        await ReconcileAsync(browser, everyday, "-500000", groceries);
        var category = await CategoryAsync(browser, "Groceries");
        var (transaction, version) = await TransactionAsync(browser, groceries);
        Assert.True(transaction.IsReconciled);

        var amount = await EditAsync(browser, groceries, version, new { type = "expense", accountId = everyday, amount = "600000", date = "2026-10-20", categoryId = category });
        var date = await EditAsync(browser, groceries, version, new { type = "expense", accountId = everyday, amount = "500000", date = "2026-10-21", categoryId = category });
        var account = await EditAsync(browser, groceries, version, new { type = "expense", accountId = savings, amount = "500000", date = "2026-10-20", categoryId = category });
        var deleted = await browser.DeleteAsync($"/api/v1/transactions/{groceries}");

        Assert.Equal(["amount"], await amount.ErrorFieldsAsync());
        Assert.Equal(["date"], await date.ErrorFieldsAsync());
        Assert.Equal(["accountId"], await account.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Equal("urn:pfm:problem:transaction-reconciled", await deleted.ProblemTypeAsync());
        Assert.Equal("-500000.00", await BalanceAsync(browser, everyday));

        var payee = await EditAsync(browser, groceries, version, new { type = "expense", accountId = everyday, amount = "500000", date = "2026-10-20", categoryId = category, payee = "Co.op Mart" });
        Assert.Equal(HttpStatusCode.OK, payee.StatusCode);
    }

    [Fact]
    public async Task A_transfer_is_locked_as_soon_as_either_of_its_legs_is_reconciled()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings");
        var created = await browser.PostAsync("/api/v1/transfers", new { fromAccountId = everyday, toAccountId = savings, amount = "1000000", date = "2026-10-20" });
        var transfer = (await created.Content.ReadFromJsonAsync<TransferBody>())!;
        await ReconcileAsync(browser, savings, "1000000", transfer.InTransactionId);

        var edited = await browser.PutAsync(
            $"/api/v1/transfers/{transfer.Id}", new { fromAccountId = everyday, toAccountId = savings, amount = "2000000", date = "2026-10-20" }, Quoted(transfer.RowVersion));
        var deleted = await browser.DeleteAsync($"/api/v1/transfers/{transfer.Id}");
        var outLegDeleted = await browser.DeleteAsync($"/api/v1/transactions/{transfer.OutTransactionId}");

        Assert.Equal(["amount"], await edited.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Equal("urn:pfm:problem:transaction-reconciled", await deleted.ProblemTypeAsync());
        Assert.Equal(HttpStatusCode.Conflict, outLegDeleted.StatusCode);
        Assert.Equal(("4000000.00", "1000000.00"), (await BalanceAsync(browser, everyday), await BalanceAsync(browser, savings)));
        Assert.True((await browser.Http.GetFromJsonAsync<TransferBody>($"/api/v1/transfers/{transfer.Id}"))!.IsReconciled);
    }

    [Fact]
    public async Task A_reconciled_transaction_cannot_be_marked_as_a_transfer_nor_a_reconciled_transfer_unmarked()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var visa = await AddAccountAsync(browser, "Visa");
        var payment = await SpendAsync(browser, everyday, "Groceries", "500000", October20);
        var received = await RecordAsync(browser, "income", visa, "Salary", "500000", October20);
        await ReconcileAsync(browser, everyday, "-500000", payment);

        var mark = await browser.PostAsync("/api/v1/transfers/mark", new { expenseId = payment, incomeId = received });

        Assert.Equal(HttpStatusCode.BadRequest, mark.StatusCode);
        Assert.Equal(["expenseId"], await mark.ErrorFieldsAsync());

        var created = await browser.PostAsync("/api/v1/transfers", new { fromAccountId = visa, toAccountId = everyday, amount = "1000", date = "2026-10-20" });
        var transfer = (await created.Content.ReadFromJsonAsync<TransferBody>())!;
        await ReconcileAsync(browser, visa, "499000", received, transfer.OutTransactionId);

        var unmark = await browser.PostAsync(
            $"/api/v1/transfers/{transfer.Id}/unmark",
            new { expenseCategoryId = await CategoryAsync(browser, "Groceries"), incomeCategoryId = await CategoryAsync(browser, "Salary") });

        Assert.Equal(HttpStatusCode.BadRequest, unmark.StatusCode);
        Assert.Equal(["id"], await unmark.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_completed_reconciliation_can_be_reopened_which_unlocks_its_transactions()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await SpendAsync(browser, everyday, "Groceries", "500000", October20);
        var reconciliation = await ReconcileAsync(browser, everyday, "-500000", groceries);

        var reopened = await browser.PostAsync($"{Reconciliations}/{reconciliation}/reopen");

        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        var body = (await reopened.Content.ReadFromJsonAsync<ReconciliationBody>())!;
        Assert.Equal(("inProgress", (string?)null, 1), (body.Status, body.CompletedAt, body.TickedCount));
        var (transaction, version) = await TransactionAsync(browser, groceries);
        Assert.False(transaction.IsReconciled);
        var edited = await EditAsync(
            browser, groceries, version, new { type = "expense", accountId = everyday, amount = "600000", date = "2026-10-20", categoryId = await CategoryAsync(browser, "Groceries") });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("100000.00", (await GetAsync(browser, reconciliation)).Difference);
    }

    [Fact]
    public async Task The_next_statement_is_matched_from_what_is_already_reconciled()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var october = await SpendAsync(browser, everyday, "Groceries", "500000", October20);
        var first = await ReconcileAsync(browser, everyday, "500000", october);
        var november = await SpendAsync(browser, everyday, "Groceries", "100000", new DateOnly(2026, 11, 2));

        var second = await StartAsync(browser, everyday, "400000", "2026-11-15");
        var ticked = await TickAsync(browser, second, november);

        Assert.Equal(("500000.00", "-100000.00", "0.00"), (ticked.ReconciledBalance, ticked.TickedTotal, ticked.Difference));
        Assert.Equal(
            ["2026-11-15", "2026-10-31"],
            (await browser.Http.GetFromJsonAsync<ReconciliationList>($"/api/v1/accounts/{everyday}/reconciliations"))!.Items.Select(item => item.StatementDate));

        var reopenFirst = await browser.PostAsync($"{Reconciliations}/{first}/reopen");
        Assert.Equal(HttpStatusCode.Conflict, reopenFirst.StatusCode);
        Assert.Equal("urn:pfm:problem:reconciliation-not-latest", await reopenFirst.ProblemTypeAsync());
    }

    [Fact]
    public async Task An_account_has_one_reconciliation_in_progress_at_a_time_which_can_be_cancelled()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await SpendAsync(browser, everyday, "Groceries", "500000", October20);
        var first = await StartAsync(browser, everyday, "-500000");
        await TickAsync(browser, first, groceries);

        var second = await browser.PostAsync($"/api/v1/accounts/{everyday}/reconciliations", new { statementDate = "2026-10-31", statementBalance = "0" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("urn:pfm:problem:reconciliation-in-progress", await second.ProblemTypeAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"{Reconciliations}/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Reconciliations}/{first}")).StatusCode);
        var again = await StartAsync(browser, everyday, "-500000");
        Assert.Equal("0.00", (await TickAsync(browser, again, groceries)).Difference);
    }

    [Fact]
    public async Task A_completed_reconciliation_takes_no_ticks_and_cannot_be_cancelled_until_it_is_reopened()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await SpendAsync(browser, everyday, "Groceries", "500000", October20);
        var other = await SpendAsync(browser, everyday, "Groceries", "1000", October5);
        var reconciliation = await ReconcileAsync(browser, everyday, "-500000", groceries);

        var tick = await browser.PutAsync($"{Reconciliations}/{reconciliation}/transactions/{other}", new { });
        var cancel = await browser.DeleteAsync($"{Reconciliations}/{reconciliation}");

        Assert.Equal(HttpStatusCode.Conflict, tick.StatusCode);
        Assert.Equal("urn:pfm:problem:reconciliation-completed", await tick.ProblemTypeAsync());
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
    }

    private static async Task<Guid> StartAsync(Browser browser, Guid accountId, string statementBalance, string statementDate = "2026-10-31")
    {
        var response = await browser.PostAsync($"/api/v1/accounts/{accountId}/reconciliations", new { statementDate, statementBalance });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReconciliationBody>())!.Id;
    }

    private static async Task<ReconciliationBody> TickAsync(Browser browser, Guid reconciliation, Guid transaction)
    {
        var response = await browser.PutAsync($"{Reconciliations}/{reconciliation}/transactions/{transaction}", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReconciliationBody>())!;
    }

    /// <summary>Starts a reconciliation, ticks the transactions and completes it.</summary>
    private static async Task<Guid> ReconcileAsync(Browser browser, Guid accountId, string statementBalance, params Guid[] transactions)
    {
        var reconciliation = await StartAsync(browser, accountId, statementBalance);
        foreach (var transaction in transactions)
        {
            await TickAsync(browser, reconciliation, transaction);
        }

        var completed = await browser.PostAsync($"{Reconciliations}/{reconciliation}/complete");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        return reconciliation;
    }

    private static async Task<ReconciliationBody> GetAsync(Browser browser, Guid reconciliation) =>
        (await browser.Http.GetFromJsonAsync<ReconciliationBody>($"{Reconciliations}/{reconciliation}"))!;

    private static async Task<(TransactionBody Transaction, string Version)> TransactionAsync(Browser browser, Guid id)
    {
        var response = await browser.GetAsync($"/api/v1/transactions/{id}");
        return ((await response.Content.ReadFromJsonAsync<TransactionBody>())!, response.Headers.ETag!.Tag);
    }

    private static Task<HttpResponseMessage> EditAsync(Browser browser, Guid id, string version, object body) =>
        browser.PutAsync($"/api/v1/transactions/{id}", body, version);

    private static string Quoted(string rowVersion) => $"\"{rowVersion}\"";

    private sealed record ReconciliationBody(
        Guid Id, Guid AccountId, string StatementDate, string StatementBalance, string Currency, string Status, string? CompletedAt,
        string ReconciledBalance, string TickedTotal, string Difference, int TickedCount);

    private sealed record ReconciliationList(List<ReconciliationBody> Items, string? NextCursor);

    private sealed record TransactionToTick(Guid Id, string Type, string Date, string Amount, string Currency, bool IsRefund, string? Payee, bool IsTicked);

    private sealed record TransactionsToTick(List<TransactionToTick> Items);

    private sealed record TransactionBody(Guid Id, bool IsReconciled);

    private sealed record TransferBody(Guid Id, Guid OutTransactionId, Guid InTransactionId, string RowVersion, bool IsReconciled);
}
