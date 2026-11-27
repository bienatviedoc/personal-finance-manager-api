using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Imports;
using Pfm.Domain.Imports;
using Pfm.Domain.Transactions;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// Undoing an import batch (spec IMP-5 and the acceptance notes of section 5): every transaction it created is
/// deleted, edited or not, and stays recoverable for 30 days like any deleted transaction; refused while any of them
/// is reconciled.
/// </summary>
public partial class ImportTests
{
    [Fact]
    public async Task Undoing_a_batch_deletes_every_transaction_it_imported_edited_or_not_and_each_stays_recoverable()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank", openingBalance: "1000000");
        var mine = await SpendAsync(browser, account, "Food & drink", "30000", new DateOnly(2026, 8, 3), payee: "BANH MI");
        var batch = await UploadAsync(browser, account, Small);
        (await CommitAsync(browser, batch, SmallMapping, [2, 3, 5])).EnsureSuccessStatusCode();
        await ImportAsync();
        var imported = await ImportedAsync(browser, account, batch.Id);
        (await browser.PostAsync("/api/v1/transactions/bulk", new { action = "addTag", transactionIds = new[] { imported[0] }, tag = "edited" })).EnsureSuccessStatusCode();

        var response = await browser.DeleteAsync($"{Imports}/{batch.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var undone = (await response.Content.ReadFromJsonAsync<BatchBody>())!;
        Assert.Equal(("undone", 3), (undone.Status, undone.ImportedCount));
        Assert.NotNull(undone.UndoneAt);
        Assert.Equal("undone", (await BatchAsync(browser, batch.Id)).Status);
        Assert.Empty(await ImportedAsync(browser, account, batch.Id));
        Assert.Equal("970000.00", await BalanceAsync(browser, account));
        var deleted = (await browser.Http.GetFromJsonAsync<DeletedList>("/api/v1/transactions/deleted"))!.Items;
        Assert.Equal(imported.Order(), deleted.Select(transaction => transaction.Id).Order());
        Assert.DoesNotContain(mine, deleted.Select(transaction => transaction.Id));

        (await browser.PostAsync($"/api/v1/transactions/{imported[0]}/restore")).EnsureSuccessStatusCode();
        Assert.Equal([imported[0]], await ImportedAsync(browser, account, batch.Id));
    }

    [Fact]
    public async Task Undoing_a_batch_again_answers_it_as_undone_and_a_batch_never_committed_has_nothing_to_undo()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var batch = await UploadAsync(browser, account, Small);
        var uploaded = await UploadAsync(browser, account, Small);
        (await CommitAsync(browser, batch, SmallMapping, [2])).EnsureSuccessStatusCode();
        await ImportAsync();
        (await browser.DeleteAsync($"{Imports}/{batch.Id}")).EnsureSuccessStatusCode();

        var again = await browser.DeleteAsync($"{Imports}/{batch.Id}");
        var notCommitted = await browser.DeleteAsync($"{Imports}/{uploaded.Id}");
        var (stranger, _) = await api.SignedInAsync();
        var theirs = await stranger.DeleteAsync($"{Imports}/{batch.Id}");

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("undone", (await again.Content.ReadFromJsonAsync<BatchBody>())!.Status);
        Assert.Equal(HttpStatusCode.Conflict, notCommitted.StatusCode);
        Assert.Equal("urn:pfm:problem:import-not-committed", await notCommitted.ProblemTypeAsync());
        Assert.Equal("uploaded", (await BatchAsync(browser, uploaded.Id)).Status);
        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
    }

    [Fact]
    public async Task Undoing_is_refused_while_any_transaction_of_the_batch_is_reconciled_saying_why()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var batch = await UploadAsync(browser, account, Small);
        (await CommitAsync(browser, batch, SmallMapping, [2, 3])).EnsureSuccessStatusCode();
        await ImportAsync();
        var salary = (await ImportedAsync(browser, account, batch.Id))[1];
        await ReconcileImportedAsync(browser, account, "28500000", salary);

        var response = await browser.DeleteAsync($"{Imports}/{batch.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("urn:pfm:problem:import-reconciled", await response.ProblemTypeAsync());
        Assert.Equal("This statement cannot be undone while any of its transactions is reconciled.", await response.ProblemTitleAsync());
        Assert.Equal("committed", (await BatchAsync(browser, batch.Id)).Status);
        Assert.Equal(2, (await ImportedAsync(browser, account, batch.Id)).Count);
    }

    [Fact]
    public async Task Undoing_a_batch_one_of_whose_transactions_was_marked_as_a_transfer_deletes_the_transfer_both_legs_together()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var card = await AddAccountAsync(browser, "Visa");
        var batch = await UploadAsync(browser, account, Small);
        (await CommitAsync(browser, batch, SmallMapping, [2, 5])).EnsureSuccessStatusCode();
        await ImportAsync();
        var payment = (await ImportedAsync(browser, account, batch.Id))[1];
        var received = await RecordAsync(browser, "income", card, "Salary", "146000", new DateOnly(2026, 8, 6));
        (await browser.PostAsync("/api/v1/transfers/mark", new { expenseId = payment, incomeId = received })).EnsureSuccessStatusCode();

        (await browser.DeleteAsync($"{Imports}/{batch.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(("0.00", "0.00"), (await BalanceAsync(browser, account), await BalanceAsync(browser, card)));
        (await browser.PostAsync($"/api/v1/transactions/{received}/restore")).EnsureSuccessStatusCode();
        Assert.Equal(("-146000.00", "146000.00"), (await BalanceAsync(browser, account), await BalanceAsync(browser, card)));
    }

    [Fact]
    public async Task Undoing_raises_the_budget_notifications_the_deleted_transactions_make_due()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await browser.PatchAsync("/api/v1/me", new { timeZone = "Etc/UTC" })).EnsureSuccessStatusCode();
        var food = await CategoryAsync(browser, "Food & drink");
        (await browser.PutAsync($"/api/v1/budgets/{today:yyyy-MM}/{food}", new { amount = "100000" })).EnsureSuccessStatusCode();
        var batch = await UploadAsync(browser, account, $"Date;Details;Amount\n{today:dd-MM-yyyy};GRABFOOD HCM;-40.000,00\n");
        (await CommitAsync(browser, batch, SmallMapping, [2])).EnsureSuccessStatusCode();
        await ImportAsync();
        var refund = (await ImportedAsync(browser, account, batch.Id))[0];
        var version = (await browser.Http.GetFromJsonAsync<Versioned>($"/api/v1/transactions/{refund}"))!.RowVersion;
        (await browser.PutAsync(
            $"/api/v1/transactions/{refund}", new { type = "expense", isRefund = true, accountId = account, amount = "40000", date = $"{today:yyyy-MM-dd}", categoryId = food },
            ifMatch: $"\"{version}\"")).EnsureSuccessStatusCode();
        await SpendAsync(browser, account, "Food & drink", "115000", today);
        Assert.Empty((await browser.Http.GetFromJsonAsync<NotificationList>("/api/v1/budget-notifications"))!.Items);

        (await browser.DeleteAsync($"{Imports}/{batch.Id}")).EnsureSuccessStatusCode();

        var notifications = await browser.Http.GetFromJsonAsync<NotificationList>("/api/v1/budget-notifications");
        Assert.Equal([(food, "over")], notifications!.Items.Select(notification => (notification.Category.Id, notification.State)));
    }

    [Fact]
    public async Task Undoing_a_batch_still_being_imported_stops_it_and_deletes_what_it_imported_so_far()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Vietcombank");
        var batch = await UploadAsync(browser, account, Statement("vietcombank-sample.csv"));
        var preview = await PreviewAsync(browser, batch, VietcombankMapping);
        (await CommitAsync(browser, batch, VietcombankMapping, [.. preview.Rows.Where(row => row.Selected).Select(row => row.Number)])).EnsureSuccessStatusCode();
        await ImportNextAsync(rowsPerSave: 20);

        var undone = (await (await browser.DeleteAsync($"{Imports}/{batch.Id}")).Content.ReadFromJsonAsync<BatchBody>())!;
        await ImportAsync();

        Assert.Equal(("undone", 20), (undone.Status, undone.ImportedCount));
        Assert.Equal(("undone", 20), ((await BatchAsync(browser, batch.Id)).Status, (await BatchAsync(browser, batch.Id)).ImportedCount));
        Assert.Empty(await ImportedAsync(browser, account, batch.Id));
    }

    [Fact]
    public async Task An_undo_that_meets_the_worker_importing_the_next_rows_deletes_those_too()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Vietcombank");
        var batch = await UploadAsync(browser, account, Statement("vietcombank-sample.csv"));
        var preview = await PreviewAsync(browser, batch, VietcombankMapping);
        (await CommitAsync(browser, batch, VietcombankMapping, [.. preview.Rows.Where(row => row.Selected).Select(row => row.Number)])).EnsureSuccessStatusCode();
        await ImportNextAsync(rowsPerSave: 20);

        // The worker imports the next rows between the undo reading the batch and saving.
        api.Saves.BeforeNextSaveOf<ImportBatch>(() => ImportNextAsync(rowsPerSave: 20));
        HttpResponseMessage response;
        try
        {
            response = await browser.DeleteAsync($"{Imports}/{batch.Id}");
        }
        finally
        {
            api.Saves.Stop();
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("undone", 40), ((await BatchAsync(browser, batch.Id)).Status, (await BatchAsync(browser, batch.Id)).ImportedCount));
        Assert.Empty(await ImportedAsync(browser, account, batch.Id));
    }

    [Fact]
    public async Task A_worker_that_meets_an_undo_while_importing_the_next_rows_imports_none_of_them()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Vietcombank");
        var batch = await UploadAsync(browser, account, Statement("vietcombank-sample.csv"));
        var preview = await PreviewAsync(browser, batch, VietcombankMapping);
        (await CommitAsync(browser, batch, VietcombankMapping, [.. preview.Rows.Where(row => row.Selected).Select(row => row.Number)])).EnsureSuccessStatusCode();
        HttpStatusCode? undone = null;

        // The user undoes the batch between the worker reading the next rows and saving them.
        api.Saves.BeforeNextSaveOf<Transaction>(async () => undone = (await browser.DeleteAsync($"{Imports}/{batch.Id}")).StatusCode);
        try
        {
            await ImportNextAsync(rowsPerSave: 20);
        }
        finally
        {
            api.Saves.Stop();
        }

        await ImportAsync();
        Assert.Equal(HttpStatusCode.OK, undone);
        Assert.Equal(("undone", 0), ((await BatchAsync(browser, batch.Id)).Status, (await BatchAsync(browser, batch.Id)).ImportedCount));
        Assert.Empty(await ImportedAsync(browser, account, batch.Id));
    }

    /// <summary>One run of the worker's job, importing at most this many rows of each batch.</summary>
    private async Task ImportNextAsync(int rowsPerSave)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ImportCommits>().ImportNextAsync(rowsPerSave);
    }

    /// <summary>The ids of the account's transactions the batch imported, not deleted, in date order.</summary>
    private static async Task<List<Guid>> ImportedAsync(Browser browser, Guid accountId, Guid batchId) =>
        [.. (await browser.Http.GetFromJsonAsync<TransactionList>($"/api/v1/transactions?accountId={accountId}&limit=100"))!.Items
            .Where(transaction => transaction.ImportBatchId == batchId)
            .OrderBy(transaction => transaction.Date)
            .Select(transaction => transaction.Id)];

    private static async Task ReconcileImportedAsync(Browser browser, Guid accountId, string statementBalance, params Guid[] ticked)
    {
        var started = await browser.PostAsync($"/api/v1/accounts/{accountId}/reconciliations", new { statementDate = "2026-11-02", statementBalance });
        var reconciliation = (await started.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<Identified>())!.Id;
        foreach (var transaction in ticked)
        {
            (await browser.PutAsync($"/api/v1/reconciliations/{reconciliation}/transactions/{transaction}", new { })).EnsureSuccessStatusCode();
        }

        (await browser.PostAsync($"/api/v1/reconciliations/{reconciliation}/complete")).EnsureSuccessStatusCode();
    }

    private sealed record Identified(Guid Id);

    private sealed record Versioned(string RowVersion);

    private sealed record DeletedBody(Guid Id);

    private sealed record DeletedList(List<DeletedBody> Items);
}
