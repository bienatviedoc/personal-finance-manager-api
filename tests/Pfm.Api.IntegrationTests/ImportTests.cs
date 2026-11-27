using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Imports;
using Pfm.Domain.Transactions;
using Xunit.Abstractions;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// Statement import (spec IMP-1, IMP-3 and the acceptance notes of section 5): upload a CSV for an account, map its
/// columns, preview the rows with likely duplicates flagged, and commit, which the worker carries out in the
/// background. These tests run the worker's job the way it does, and read what the user then sees.
/// </summary>
[Collection(ApiCollection.Name)]
public partial class ImportTests(PfmApi api, ITestOutputHelper output)
{
    private const string Imports = "/api/v1/imports";

    /// <summary>What the user maps the Vietcombank stand-in with: debit and credit columns, after a six-row preamble, before a total.</summary>
    private static readonly Dictionary<string, object?> VietcombankMapping = new()
    {
        ["separator"] = "comma", ["skipFirstRows"] = 6, ["hasHeaderRow"] = true, ["skipLastRows"] = 1, ["dateColumn"] = 1,
        ["descriptionColumn"] = 5, ["debitColumn"] = 3, ["creditColumn"] = 4, ["dateFormat"] = "dayMonthYear", ["decimalSeparator"] = "dot",
    };

    /// <summary>A small statement of four rows: two expenses, a salary and a row that cannot be read.</summary>
    private const string Small = "Ngày;Mô tả;Số tiền\n02-08-2026;\"HIGHLANDS COFFEE\";-55.000,00\n05-08-2026;LUONG THANG 08;28.500.000,00\nTổng;;\n06-08-2026;GRABFOOD HCM;-146.000,00\n";

    private static readonly Dictionary<string, object?> SmallMapping = new()
    {
        ["separator"] = "semicolon", ["skipFirstRows"] = 0, ["hasHeaderRow"] = true, ["skipLastRows"] = 0, ["dateColumn"] = 0,
        ["descriptionColumn"] = 1, ["amountColumn"] = 2, ["dateFormat"] = "dayMonthYear", ["decimalSeparator"] = "comma",
    };

    [Fact]
    public async Task A_user_uploads_a_statement_for_an_account_and_is_offered_a_first_guess_at_its_layout()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Vietcombank");

        var response = await UploadAsync(browser, account, "vcb-2026-10.csv", Statement("vietcombank-sample.csv"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var batch = (await response.Content.ReadFromJsonAsync<BatchBody>())!;
        Assert.Equal($"{Imports}/{batch.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal((account, "vcb-2026-10.csv", "uploaded"), (batch.AccountId, batch.FileName, batch.Status));
        Assert.Equal(new MappingBody("comma", 6, true, 0, null, null, null, null, null, "dayMonthYear", "dot"), batch.SuggestedMapping);
        Assert.Null(batch.Mapping);
        var fetched = (await browser.Http.GetFromJsonAsync<BatchBody>($"{Imports}/{batch.Id}"))!;
        Assert.Equal(batch with { CreatedAt = fetched.CreatedAt }, fetched);
        Assert.True((batch.CreatedAt - fetched.CreatedAt).Duration() < TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task A_file_that_is_not_a_text_file_or_is_empty_or_an_account_that_cannot_take_transactions_is_refused()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Everyday");
        var archived = await AddAccountAsync(browser, "Old");
        (await browser.DeleteAsync($"/api/v1/accounts/{archived}")).EnsureSuccessStatusCode();
        var (stranger, _) = await api.SignedInAsync();
        var theirs = await AddAccountAsync(stranger, "Theirs");

        var spreadsheet = await UploadAsync(browser, account, "statement.xlsx", [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00, 0x08, 0x00]);
        var empty = await UploadAsync(browser, account, "statement.csv", []);
        var toArchived = await UploadAsync(browser, archived, "statement.csv", Encoding.UTF8.GetBytes(Small));
        var toTheirs = await UploadAsync(browser, theirs, "statement.csv", Encoding.UTF8.GetBytes(Small));

        Assert.Equal("Upload a CSV file saved as text (UTF-8).", await spreadsheet.ErrorOnAsync("file"));
        Assert.Equal("This file is empty.", await empty.ErrorOnAsync("file"));
        Assert.Equal("Choose one of your accounts that is not archived.", await toArchived.ErrorOnAsync("accountId"));
        Assert.Equal("Choose one of your accounts that is not archived.", await toTheirs.ErrorOnAsync("accountId"));
    }

    [Fact]
    public async Task A_statement_saved_as_UTF16_is_read_as_text_too()
    {
        var (browser, _) = await api.SignedInAsync();
        var batch = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"), [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Small)]);

        Assert.Equal(["Ngày", "Mô tả", "Số tiền"], (await PreviewAsync(browser, batch, SmallMapping)).Header);
    }

    [Fact]
    public async Task A_statement_of_more_than_10000_rows_is_refused()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Everyday");
        var rows = string.Concat(Enumerable.Repeat("02-08-2026;GRAB;-1.000\n", 10_000));

        var atTheLimit = await UploadAsync(browser, account, "statement.csv", Encoding.UTF8.GetBytes(rows));
        var over = await UploadAsync(browser, account, "statement.csv", Encoding.UTF8.GetBytes("Date;Details;Amount\n" + rows));

        Assert.Equal(HttpStatusCode.Created, atTheLimit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Equal("Upload a statement of at most 10,000 rows.", await over.ErrorOnAsync("file"));
    }

    [Fact]
    public async Task A_file_over_5_MB_is_refused()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Everyday");

        var response = await UploadAsync(browser, account, "statement.csv", Encoding.UTF8.GetBytes(new string('x', 5 * 1024 * 1024 + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Upload a CSV file of at most 5 MB.", await response.ErrorOnAsync("file"));
    }

    [Fact]
    public async Task The_preview_shows_every_row_as_the_mapping_reads_it_with_the_rows_that_cannot_be_read_and_why()
    {
        var (browser, _) = await api.SignedInAsync();
        var batch = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"), Small);

        var preview = await PreviewAsync(browser, batch, SmallMapping);

        Assert.Equal(["Ngày", "Mô tả", "Số tiền"], preview.Header);
        Assert.Equal("VND", preview.Currency);
        Assert.Equal(
            [
                new RowBody(1, ["Ngày", "Mô tả", "Số tiền"], "header", null, null, null, null, null, false, false),
                new RowBody(2, ["02-08-2026", "HIGHLANDS COFFEE", "-55.000,00"], "parsed", "2026-08-02", "expense", "55000.00", "HIGHLANDS COFFEE", null, false, true),
                new RowBody(3, ["05-08-2026", "LUONG THANG 08", "28.500.000,00"], "parsed", "2026-08-05", "income", "28500000.00", "LUONG THANG 08", null, false, true),
                new RowBody(4, ["Tổng", "", ""], "failed", null, null, null, null, "dateUnreadable", false, false),
                new RowBody(5, ["06-08-2026", "GRABFOOD HCM", "-146.000,00"], "parsed", "2026-08-06", "expense", "146000.00", "GRABFOOD HCM", null, false, true),
            ],
            preview.Rows);
        Assert.Equal(new CountsBody(3, 1, 0, 3), preview.Counts);
    }

    [Fact]
    public async Task Without_a_complete_mapping_the_preview_only_cuts_the_rows_into_cells_for_the_user_to_map()
    {
        var (browser, _) = await api.SignedInAsync();
        var batch = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"), Small);

        var preview = await PreviewAsync(browser, batch, new() { ["separator"] = "semicolon", ["hasHeaderRow"] = true });

        Assert.Equal(["unmapped"], preview.Rows.Skip(1).Select(row => row.Kind).Distinct());
        Assert.Equal(3, preview.ColumnCount);
    }

    [Fact]
    public async Task A_mapping_the_preview_cannot_read_is_a_validation_problem_naming_each_choice_at_fault()
    {
        var (browser, _) = await api.SignedInAsync();
        var batch = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"), Small);

        var response = await browser.GetAsync($"{Imports}/{batch.Id}/preview?separator=colon&dateColumn=-1&dateFormat=dd%2FMM%2Fyyyy&amountColumn=2&debitColumn=3");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["amountColumn", "dateColumn", "dateFormat", "separator"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_row_matching_a_transaction_already_on_the_account_is_a_likely_duplicate_unticked_and_counted()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        await SpendAsync(browser, account, "Food & drink", "55000", new DateOnly(2026, 8, 2), payee: "Highlands Coffee");
        var elsewhere = await AddAccountAsync(browser, "Cash");
        await SpendAsync(browser, elsewhere, "Food & drink", "146000", new DateOnly(2026, 8, 6), payee: "GRABFOOD HCM");
        var batch = await UploadAsync(browser, account, Small);

        var preview = await PreviewAsync(browser, batch, SmallMapping);

        Assert.Equal([(2, true, false), (3, false, true), (5, false, true)], preview.Rows.Where(row => row.Kind == "parsed").Select(row => (row.Number, row.LikelyDuplicate, row.Selected)));
        Assert.Equal(new CountsBody(3, 1, 1, 2), preview.Counts);
    }

    [Fact]
    public async Task A_row_matching_a_transfer_leg_on_the_account_the_same_way_is_a_likely_duplicate_whatever_its_description()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var savings = await AddAccountAsync(browser, "Savings");
        await TransferAsync(browser, account, savings, "146000", new DateOnly(2026, 8, 6));
        await TransferAsync(browser, savings, account, "28500000", new DateOnly(2026, 8, 5));
        await TransferAsync(browser, savings, account, "55000", new DateOnly(2026, 8, 2));
        var batch = await UploadAsync(browser, account, Small);

        var preview = await PreviewAsync(browser, batch, SmallMapping);

        Assert.Equal([(2, false), (3, true), (5, true)], preview.Rows.Where(row => row.Kind == "parsed").Select(row => (row.Number, row.LikelyDuplicate)));
    }

    [Fact]
    public async Task Committing_imports_the_ticked_rows_in_the_background_each_transaction_belonging_to_the_batch()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank", openingBalance: "1000000");
        await SpendAsync(browser, account, "Food & drink", "55000", new DateOnly(2026, 8, 2), payee: "HIGHLANDS COFFEE");
        var batch = await UploadAsync(browser, account, Small);

        var response = await CommitAsync(browser, batch, SmallMapping, [3, 5]);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var committing = (await response.Content.ReadFromJsonAsync<BatchBody>())!;
        Assert.Equal(("committing", 4, 2, 0, 1, 1), (committing.Status, committing.RowCount, committing.SelectedCount, committing.ImportedCount, committing.DuplicateCount, committing.FailedCount));
        Assert.Equal(new MappingBody("semicolon", 0, true, 0, 0, 1, 2, null, null, "dayMonthYear", "comma"), committing.Mapping);

        await ImportAsync();

        var committed = await BatchAsync(browser, batch.Id);
        Assert.Equal(("committed", 2), (committed.Status, committed.ImportedCount));
        Assert.NotNull(committed.CommittedAt);
        var imported = (await browser.Http.GetFromJsonAsync<TransactionList>($"/api/v1/transactions?accountId={account}"))!.Items
            .Where(transaction => transaction.ImportBatchId == batch.Id)
            .OrderBy(transaction => transaction.Date)
            .ToList();
        Assert.Equal(
            [
                ("income", "28500000.00", "2026-08-05", "LUONG THANG 08", await CategoryAsync(browser, "Salary")),
                ("expense", "146000.00", "2026-08-06", "GRABFOOD HCM", await CategoryAsync(browser, "Food & drink")),
            ],
            imported.Select(transaction => (transaction.Type, transaction.Amount, transaction.Date, transaction.Payee, transaction.CategoryId)));
        Assert.Equal("29299000.00", await BalanceAsync(browser, account));
    }

    [Fact]
    public async Task While_the_worker_imports_the_batch_shows_how_many_rows_are_done()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Vietcombank");
        var batch = await UploadAsync(browser, account, Statement("vietcombank-sample.csv"));
        var preview = await PreviewAsync(browser, batch, VietcombankMapping);
        (await CommitAsync(browser, batch, VietcombankMapping, [.. preview.Rows.Where(row => row.Selected).Select(row => row.Number)])).EnsureSuccessStatusCode();

        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ImportCommits>().ImportNextAsync(rowsPerSave: 20);
        }

        var halfway = await BatchAsync(browser, batch.Id);
        Assert.Equal(("committing", 20, 56), (halfway.Status, halfway.ImportedCount, halfway.SelectedCount));

        await ImportAsync();
        Assert.Equal(("committed", 56), ((await BatchAsync(browser, batch.Id)).Status, (await BatchAsync(browser, batch.Id)).ImportedCount));
    }

    [Fact]
    public async Task Importing_raises_the_budget_notifications_the_imported_expenses_make_due()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await browser.PatchAsync("/api/v1/me", new { timeZone = "Etc/UTC" })).EnsureSuccessStatusCode();
        var food = await CategoryAsync(browser, "Food & drink");
        (await browser.PutAsync($"/api/v1/budgets/{today:yyyy-MM}/{food}", new { amount = "100000" })).EnsureSuccessStatusCode();
        var text = $"Date;Details;Amount\n{today:dd-MM-yyyy};GRABFOOD HCM;-146.000,00\n";
        var batch = await UploadAsync(browser, account, text);
        (await CommitAsync(browser, batch, SmallMapping, [2])).EnsureSuccessStatusCode();

        await ImportAsync();

        var notifications = await browser.Http.GetFromJsonAsync<NotificationList>("/api/v1/budget-notifications");
        Assert.Equal([(food, "over")], notifications!.Items.Select(notification => (notification.Category.Id, notification.State)));
    }

    [Fact]
    public async Task A_row_dated_today_when_it_was_committed_is_imported_even_when_the_users_time_zone_moves_back_a_day_before_the_worker_gets_to_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        (await browser.PatchAsync("/api/v1/me", new { timeZone = "Pacific/Kiritimati" })).EnsureSuccessStatusCode();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(14));
        var batch = await UploadAsync(browser, account, $"Date;Details;Amount\n{today:dd-MM-yyyy};GRABFOOD HCM;-146.000,00\n");
        (await CommitAsync(browser, batch, SmallMapping, [2])).EnsureSuccessStatusCode();
        (await browser.PatchAsync("/api/v1/me", new { timeZone = "Etc/GMT+12" })).EnsureSuccessStatusCode();

        await ImportAsync();

        var imported = await BatchAsync(browser, batch.Id);
        Assert.Equal(("committed", 1, (string?)null), (imported.Status, imported.ImportedCount, imported.Failure));
    }

    [Fact]
    public async Task A_batch_whose_category_or_account_is_archived_before_it_is_imported_stops_with_a_code_saying_which()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var categoryGone = await UploadAsync(browser, account, Small);
        var otherAccount = await AddAccountAsync(browser, "Cash");
        var accountGone = await UploadAsync(browser, otherAccount, Small);
        var groceries = await CategoryAsync(browser, "Groceries");
        (await CommitAsync(browser, categoryGone, SmallMapping, [2], expenseCategoryId: groceries)).EnsureSuccessStatusCode();
        (await CommitAsync(browser, accountGone, SmallMapping, [2])).EnsureSuccessStatusCode();
        (await browser.DeleteAsync($"/api/v1/categories/{groceries}")).EnsureSuccessStatusCode();
        (await browser.DeleteAsync($"/api/v1/accounts/{otherAccount}")).EnsureSuccessStatusCode();

        await ImportAsync();

        Assert.Equal(("failed", "categoryArchived"), ((await BatchAsync(browser, categoryGone.Id)).Status, (await BatchAsync(browser, categoryGone.Id)).Failure));
        Assert.Equal(("failed", "accountArchived"), ((await BatchAsync(browser, accountGone.Id)).Status, (await BatchAsync(browser, accountGone.Id)).Failure));
    }

    [Fact]
    public async Task Only_rows_the_preview_reads_as_transactions_can_be_committed_and_with_a_category_of_the_kind_each_takes()
    {
        var (browser, _) = await api.SignedInAsync();
        var batch = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"), Small);
        var salary = await CategoryAsync(browser, "Salary");

        var failedRow = await CommitAsync(browser, batch, SmallMapping, [2, 4]);
        var none = await CommitAsync(browser, batch, SmallMapping, []);
        var wrongKind = await CommitAsync(browser, batch, SmallMapping, [2], expenseCategoryId: salary);
        var incomplete = await CommitAsync(browser, batch, new() { ["separator"] = "semicolon" }, [2]);

        Assert.Equal("Row 4 is not a transaction that can be imported.", await failedRow.ErrorOnAsync("rows"));
        Assert.Equal("Choose at least one row to import.", await none.ErrorOnAsync("rows"));
        Assert.Equal("Choose one of your expense categories that is not archived.", await wrongKind.ErrorOnAsync("expenseCategoryId"));
        Assert.Equal("Choose the date column and the amount column, or the debit and credit columns.", await incomplete.ErrorOnAsync("mapping"));
        Assert.Equal("uploaded", (await BatchAsync(browser, batch.Id)).Status);
    }

    [Fact]
    public async Task A_statement_whose_account_was_archived_after_it_was_uploaded_cannot_be_committed()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var batch = await UploadAsync(browser, account, Small);
        (await browser.DeleteAsync($"/api/v1/accounts/{account}")).EnsureSuccessStatusCode();

        var response = await CommitAsync(browser, batch, SmallMapping, [2]);

        Assert.Equal("The statement's account has been archived: upload it again for one of your accounts that is not.", await response.ErrorOnAsync("id"));
        Assert.Equal("uploaded", (await BatchAsync(browser, batch.Id)).Status);
    }

    [Fact]
    public async Task A_batch_is_committed_once_and_then_has_no_file_to_preview()
    {
        var (browser, _) = await api.SignedInAsync();
        var batch = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"), Small);
        (await CommitAsync(browser, batch, SmallMapping, [2])).EnsureSuccessStatusCode();

        var again = await CommitAsync(browser, batch, SmallMapping, [2]);
        await ImportAsync();
        var preview = await browser.GetAsync($"{Imports}/{batch.Id}/preview?separator=semicolon");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("urn:pfm:problem:import-committed", await again.ProblemTypeAsync());
        Assert.Equal(HttpStatusCode.Conflict, preview.StatusCode);
        Assert.Equal("urn:pfm:problem:import-committed", await preview.ProblemTypeAsync());
    }

    [Fact]
    public async Task A_second_statement_for_an_account_cannot_be_committed_while_another_is_being_imported_and_once_it_is_its_rows_are_likely_duplicates()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var first = await UploadAsync(browser, account, Small);
        var second = await UploadAsync(browser, account, Small);
        var elsewhere = await UploadAsync(browser, await AddAccountAsync(browser, "Cash"), Small);
        (await CommitAsync(browser, first, SmallMapping, [2, 3, 5])).EnsureSuccessStatusCode();

        var meanwhile = await CommitAsync(browser, second, SmallMapping, [2, 3, 5]);
        var otherAccount = await CommitAsync(browser, elsewhere, SmallMapping, [2, 3, 5]);
        await ImportAsync();

        Assert.Equal(HttpStatusCode.Conflict, meanwhile.StatusCode);
        Assert.Equal("urn:pfm:problem:import-in-progress", await meanwhile.ProblemTypeAsync());
        Assert.Equal(HttpStatusCode.Accepted, otherAccount.StatusCode);
        Assert.Equal("uploaded", (await BatchAsync(browser, second.Id)).Status);
        Assert.Equal(new CountsBody(3, 1, 3, 0), (await PreviewAsync(browser, second, SmallMapping)).Counts);
    }

    [Fact]
    public async Task A_batch_the_worker_keeps_failing_on_unexpectedly_is_stopped_after_a_few_attempts_and_holds_up_no_other_batch()
    {
        var (browser, _) = await api.SignedInAsync();
        var failing = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"), Small);
        (await CommitAsync(browser, failing, SmallMapping, [2])).EnsureSuccessStatusCode();
        var (other, _) = await api.SignedInAsync();
        var healthy = await UploadAsync(other, await AddAccountAsync(other, "Vietcombank"), Small);
        (await CommitAsync(other, healthy, SmallMapping, [2, 3, 5])).EnsureSuccessStatusCode();
        api.Saves.BeforeEverySaveOf<Transaction>(transaction => transaction.ImportBatchId == failing.Id, () => throw new InvalidOperationException("Something unforeseen."));
        try
        {
            var first = await ImportNextAsync();

            Assert.Equal(("committed", 3), ((await BatchAsync(other, healthy.Id)).Status, (await BatchAsync(other, healthy.Id)).ImportedCount));
            Assert.Equal("committing", (await BatchAsync(browser, failing.Id)).Status);
            Assert.Contains(first.Failures, failure => failure.InnerException?.Message == "Something unforeseen.");

            for (var attempt = 1; attempt < ImportCommits.MaxAttempts; attempt++)
            {
                await ImportNextAsync();
            }
        }
        finally
        {
            api.Saves.Stop();
        }

        var stopped = await BatchAsync(browser, failing.Id);
        Assert.Equal(("failed", "unexpected", 0), (stopped.Status, stopped.Failure, stopped.ImportedCount));
        Assert.Empty((await ImportNextAsync()).Failures);
    }

    [Fact]
    public async Task A_statement_uploaded_and_never_committed_is_removed_with_its_file_once_it_is_older_than_the_time_uploads_are_kept()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var abandoned = await UploadAsync(browser, account, Small);
        var committed = await UploadAsync(browser, account, Small);
        (await CommitAsync(browser, committed, SmallMapping, [2])).EnsureSuccessStatusCode();
        await ImportAsync();

        await using (var earlier = api.After(TimeSpan.FromHours(23)).Services.CreateAsyncScope())
        {
            await earlier.ServiceProvider.GetRequiredService<AbandonedUploadSweep>().RunAsync(keepFor: TimeSpan.FromHours(24));
        }

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync($"{Imports}/{abandoned.Id}")).StatusCode);

        await using (var later = api.After(TimeSpan.FromHours(25)).Services.CreateAsyncScope())
        {
            Assert.True(await later.ServiceProvider.GetRequiredService<AbandonedUploadSweep>().RunAsync(keepFor: TimeSpan.FromHours(24)) >= 1);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Imports}/{abandoned.Id}")).StatusCode);
        Assert.Equal("committed", (await BatchAsync(browser, committed.Id)).Status);
    }

    [Fact]
    public async Task Another_users_batch_is_not_found()
    {
        var (browser, _) = await api.SignedInAsync();
        var batch = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"), Small);
        var (stranger, _) = await api.SignedInAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"{Imports}/{batch.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"{Imports}/{batch.Id}/preview?separator=semicolon")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CommitAsync(stranger, batch, SmallMapping, [2])).StatusCode);
    }

    [Theory]
    [InlineData("vietcombank-sample.csv")]
    [InlineData("techcombank-sample.csv")]
    public async Task Each_bank_statement_imports_cleanly_into_the_balance_it_says(string statement)
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, statement);
        var mapping = await MappingOfAsync(statement);
        var batch = await UploadAsync(browser, account, Statement(statement));

        var preview = await PreviewAsync(browser, batch, mapping);
        (await CommitAsync(browser, batch, mapping, [.. preview.Rows.Where(row => row.Selected).Select(row => row.Number)])).EnsureSuccessStatusCode();
        await ImportAsync();

        var expected = ExpectedRows(statement);
        Assert.Equal(expected.Count, (await BatchAsync(browser, batch.Id)).ImportedCount);
        var net = expected.Sum(row => row.Type == "income" ? row.Amount : -row.Amount);
        Assert.Equal(net.ToString("0.00", CultureInfo.InvariantCulture), await BalanceAsync(browser, account));
    }

    [Fact]
    public async Task Importing_a_statement_twice_flags_every_row_of_the_second_as_a_likely_duplicate()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Vietcombank");
        var first = await UploadAsync(browser, account, Statement("vietcombank-sample.csv"));
        var preview = await PreviewAsync(browser, first, VietcombankMapping);
        (await CommitAsync(browser, first, VietcombankMapping, [.. preview.Rows.Where(row => row.Selected).Select(row => row.Number)])).EnsureSuccessStatusCode();
        await ImportAsync();

        var again = await PreviewAsync(browser, await UploadAsync(browser, account, Statement("vietcombank-sample.csv")), VietcombankMapping);

        Assert.Equal(new CountsBody(56, 0, 56, 0), again.Counts);
    }

    [Fact]
    public async Task A_statement_of_5000_rows_previews_in_under_5_seconds()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Vietcombank");
        var start = new DateOnly(2026, 1, 1);
        var text = new StringBuilder("Ngày,Mô tả,Ghi nợ,Ghi có\n");
        for (var row = 0; row < 5000; row++)
        {
            var amount = (row * 7919 % 2_000_000 + 1000).ToString("#,0", CultureInfo.InvariantCulture);
            text.Append(CultureInfo.InvariantCulture, $"{start.AddDays(row % 300):dd/MM/yyyy},\"GRABFOOD HCM - Ma GD {880000 + row}\",{(row % 5 == 0 ? $",\"{amount}\"" : $"\"{amount}\",")}\n");
        }

        var mapping = new Dictionary<string, object?>
        {
            ["separator"] = "comma", ["hasHeaderRow"] = true, ["dateColumn"] = 0, ["descriptionColumn"] = 1, ["debitColumn"] = 2, ["creditColumn"] = 3,
            ["dateFormat"] = "dayMonthYear", ["decimalSeparator"] = "dot",
        };

        var clock = Stopwatch.StartNew();
        var preview = await PreviewAsync(browser, await UploadAsync(browser, account, text.ToString()), mapping);
        clock.Stop();

        output.WriteLine($"Uploading and previewing 5,000 rows took {clock.Elapsed.TotalMilliseconds:0} ms.");
        Assert.Equal(5000, preview.Counts.Parsed);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Uploading and previewing 5,000 rows took {clock.Elapsed}.");
    }

    /// <summary>Imports what is committed the way the worker's job does, a scope for each run, until a run imports nothing.</summary>
    private async Task ImportAsync()
    {
        while ((await ImportNextAsync()).Rows > 0)
        {
        }
    }

    /// <summary>One run of the worker's job.</summary>
    private async Task<ImportRun> ImportNextAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ImportCommits>().ImportNextAsync();
    }

    private static Task<HttpResponseMessage> UploadAsync(Browser browser, Guid accountId, string fileName, byte[] content) =>
        browser.PostFileAsync(Imports, fileName, content, new Dictionary<string, string> { ["accountId"] = accountId.ToString() });

    private static Task<BatchBody> UploadAsync(Browser browser, Guid accountId, string text) =>
        UploadAsync(browser, accountId, Encoding.UTF8.GetBytes(text));

    private static async Task<BatchBody> UploadAsync(Browser browser, Guid accountId, byte[] content)
    {
        var response = await UploadAsync(browser, accountId, "statement.csv", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BatchBody>())!;
    }

    private static async Task<PreviewBody> PreviewAsync(Browser browser, BatchBody batch, Dictionary<string, object?> mapping)
    {
        var query = string.Join('&', mapping.Where(choice => choice.Value is not null).Select(choice => $"{choice.Key}={Uri.EscapeDataString(Text(choice.Value!))}"));
        var response = await browser.GetAsync($"{Imports}/{batch.Id}/preview?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PreviewBody>())!;
    }

    private static async Task<HttpResponseMessage> CommitAsync(
        Browser browser, BatchBody batch, Dictionary<string, object?> mapping, int[] rows, Guid? expenseCategoryId = null, Guid? incomeCategoryId = null) =>
        await browser.PostAsync($"{Imports}/{batch.Id}/commit", new
        {
            mapping,
            rows,
            expenseCategoryId = expenseCategoryId ?? await CategoryAsync(browser, "Food & drink"),
            incomeCategoryId = incomeCategoryId ?? await CategoryAsync(browser, "Salary"),
        });

    private static async Task<BatchBody> BatchAsync(Browser browser, Guid id) => (await browser.Http.GetFromJsonAsync<BatchBody>($"{Imports}/{id}"))!;

    private static string Text(object value) => value switch
    {
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()!,
    };

    private static string StatementsFolder([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "statements", "synthetic-stand-ins"));

    private static byte[] Statement(string name) => File.ReadAllBytes(Path.Combine(StatementsFolder(), name));

    /// <summary>The mapping a user chooses for a golden statement, as its <c>.mapping.json</c> says, less what is not a choice on the screen.</summary>
    private static async Task<Dictionary<string, object?>> MappingOfAsync(string statement)
    {
        await using var json = File.OpenRead(Path.Combine(StatementsFolder(), Path.ChangeExtension(statement, ".mapping.json")));
        var choices = (await System.Text.Json.JsonSerializer.DeserializeAsync<Dictionary<string, System.Text.Json.JsonElement>>(json))!;
        return choices.Where(choice => choice.Key is not ("currency" or "today"))
            .ToDictionary(choice => choice.Key, choice => (object?)(choice.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => choice.Value.GetInt32(),
                System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => choice.Value.GetBoolean(),
                System.Text.Json.JsonValueKind.Null => null,
                _ => choice.Value.GetString(),
            }));
    }

    /// <summary>The transactions a golden statement's expected rows say it holds.</summary>
    private static List<(string Type, decimal Amount)> ExpectedRows(string statement) =>
        [.. File.ReadAllLines(Path.Combine(StatementsFolder(), Path.ChangeExtension(statement, ".expected.csv")))
            .Skip(1)
            .Select(line => line.Split(','))
            .Where(cells => cells[1] == "parsed")
            .Select(cells => (cells[3], decimal.Parse(cells[4], CultureInfo.InvariantCulture)))];

    private sealed record MappingBody(
        string Separator, int SkipFirstRows, bool HasHeaderRow, int SkipLastRows, int? DateColumn, int? DescriptionColumn, int? AmountColumn,
        int? DebitColumn, int? CreditColumn, string DateFormat, string DecimalSeparator);

    private sealed record BatchBody(
        Guid Id, Guid AccountId, string FileName, string Status, int RowCount, int SelectedCount, int ImportedCount, int DuplicateCount,
        int FailedCount, string? Failure, DateTimeOffset CreatedAt, DateTimeOffset? CommittedAt, MappingBody? Mapping, MappingBody? SuggestedMapping,
        MappingBody? SavedMapping = null, DateTimeOffset? UndoneAt = null, Guid? SavedExpenseCategoryId = null, Guid? SavedIncomeCategoryId = null);

    private sealed record RowBody(
        int Number, string[] Cells, string Kind, string? Date, string? Type, string? Amount, string? Description, string? Problem, bool LikelyDuplicate,
        bool Selected)
    {
        public bool Equals(RowBody? other) =>
            other is not null && (Number, Kind, Date, Type, Amount, Description, Problem, LikelyDuplicate, Selected)
                == (other.Number, other.Kind, other.Date, other.Type, other.Amount, other.Description, other.Problem, other.LikelyDuplicate, other.Selected)
            && Cells.SequenceEqual(other.Cells);

        public override int GetHashCode() => Number;
    }

    private sealed record CountsBody(int Parsed, int Failed, int LikelyDuplicates, int Selected);

    private sealed record PreviewBody(string[] Header, int ColumnCount, string Currency, List<RowBody> Rows, CountsBody Counts);

    private sealed record TransactionBody(Guid Id, string Type, string Amount, string Date, string? Payee, Guid? CategoryId, Guid? ImportBatchId);

    private sealed record TransactionList(List<TransactionBody> Items);

    private sealed record CategoryBody(Guid Id);

    private sealed record NotificationBody(CategoryBody Category, string State);

    private sealed record NotificationList(List<NotificationBody> Items);
}
