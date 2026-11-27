using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Imports;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// Categorisation rules at work on an import (spec IMP-4): the preview gives each row the category of the
/// highest-priority rule its description matches, and a commit records each chosen row on the category the user left
/// it with, the batch's expense or income category being for the rows left without one.
/// </summary>
[Collection(ApiCollection.Name)]
public class ImportCategorisationTests(PfmApi api)
{
    private const string Imports = "/api/v1/imports";

    /// <summary>Two expenses, a salary and a row that cannot be read.</summary>
    private const string Small = "Ngày;Mô tả;Số tiền\n02-08-2026;\"HIGHLANDS COFFEE\";-55.000,00\n05-08-2026;LUONG THANG 08;28.500.000,00\nTổng;;\n06-08-2026;GRABFOOD HCM;-146.000,00\n";

    private static readonly Dictionary<string, object?> Mapping = new()
    {
        ["separator"] = "semicolon", ["hasHeaderRow"] = true, ["dateColumn"] = 0, ["descriptionColumn"] = 1, ["amountColumn"] = 2,
        ["dateFormat"] = "dayMonthYear", ["decimalSeparator"] = "comma",
    };

    [Fact]
    public async Task The_preview_gives_each_row_the_category_of_the_highest_priority_rule_of_its_kind_it_matches()
    {
        var (browser, _) = await api.SignedInAsync();
        var transport = await CategoryAsync(browser, "Transport");
        var eatingOut = await CategoryAsync(browser, "Eating out");
        var salary = await CategoryAsync(browser, "Salary");
        await CategorisationRuleTests.AddAsync(browser, "grab", "contains", transport, priority: 1);
        await CategorisationRuleTests.AddAsync(browser, "grabfood", "startsWith", eatingOut, priority: 2);
        await CategorisationRuleTests.AddAsync(browser, "luong", "startsWith", salary);
        // An expense rule does not categorise income.
        await CategorisationRuleTests.AddAsync(browser, "thang", "contains", transport, priority: 9);
        var batch = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"));

        var preview = await PreviewAsync(browser, batch);

        Assert.Equal(
            [(1, null), (2, null), (3, salary), (4, null), (5, eatingOut)],
            preview.Rows.Select(row => (row.Number, row.SuggestedCategoryId)));
    }

    [Fact]
    public async Task Each_chosen_row_is_recorded_on_its_own_category_and_the_rest_on_the_batchs()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var transport = await CategoryAsync(browser, "Transport");
        var food = await CategoryAsync(browser, "Food & drink");
        var salary = await CategoryAsync(browser, "Salary");
        var batch = await UploadAsync(browser, account);

        var response = await CommitAsync(browser, batch, [2, 3, 5], food, salary, (5, transport));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await ImportAsync();
        Assert.Equal(
            [("HIGHLANDS COFFEE", food), ("LUONG THANG 08", salary), ("GRABFOOD HCM", transport)],
            await ImportedAsync(browser, account, batch.Id));
    }

    [Fact]
    public async Task Rows_that_each_have_a_category_need_no_category_for_the_batch()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var transport = await CategoryAsync(browser, "Transport");
        var coffee = await CategoryAsync(browser, "Coffee");
        var batch = await UploadAsync(browser, account);

        var response = await CommitAsync(browser, batch, [2, 5], expenseCategoryId: null, incomeCategoryId: null, (2, coffee), (5, transport));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await ImportAsync();
        Assert.Equal([("HIGHLANDS COFFEE", coffee), ("GRABFOOD HCM", transport)], await ImportedAsync(browser, account, batch.Id));
    }

    [Fact]
    public async Task A_row_left_without_a_category_still_needs_the_batchs()
    {
        var (browser, _) = await api.SignedInAsync();
        var batch = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"));

        var response = await CommitAsync(browser, batch, [2, 5], expenseCategoryId: null, incomeCategoryId: null, (5, await CategoryAsync(browser, "Transport")));

        Assert.Equal("Choose the category money going out is recorded on.", await response.ErrorOnAsync("expenseCategoryId"));
    }

    [Fact]
    public async Task A_rows_category_must_be_one_of_the_users_of_the_kind_the_row_takes_not_archived_and_the_row_chosen()
    {
        var (browser, _) = await api.SignedInAsync();
        var batch = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"));
        var food = await CategoryAsync(browser, "Food & drink");
        var salary = await CategoryAsync(browser, "Salary");
        var archived = await CategoryAsync(browser, "Coffee");
        (await browser.DeleteAsync($"/api/v1/categories/{archived}")).EnsureSuccessStatusCode();
        var (other, _) = await api.SignedInAsync();

        var wrongKind = await CommitAsync(browser, batch, [2, 3], food, salary, (2, salary));
        var notChosen = await CommitAsync(browser, batch, [2], food, salary, (3, salary));
        var archivedOne = await CommitAsync(browser, batch, [2], food, salary, (2, archived));
        var theirs = await CommitAsync(browser, batch, [2], food, salary, (2, await CategoryAsync(other, "Transport")));
        var twice = await CommitAsync(browser, batch, [2], food, salary, (2, food), (2, food));

        Assert.Equal("Row 2 takes one of your expense categories that is not archived.", await wrongKind.ErrorOnAsync("rowCategories"));
        Assert.Equal("Row 3 is not chosen to import.", await notChosen.ErrorOnAsync("rowCategories"));
        Assert.Equal("Row 2 takes one of your expense categories that is not archived.", await archivedOne.ErrorOnAsync("rowCategories"));
        Assert.Equal("Row 2 takes one of your expense categories that is not archived.", await theirs.ErrorOnAsync("rowCategories"));
        Assert.Equal("Give row 2 one category.", await twice.ErrorOnAsync("rowCategories"));
        Assert.Equal("uploaded", (await browser.Http.GetFromJsonAsync<BatchBody>($"{Imports}/{batch.Id}"))!.Status);
    }

    [Fact]
    public async Task A_batch_one_of_whose_rows_categories_is_archived_before_it_is_imported_stops_as_category_archived()
    {
        var (browser, _) = await api.SignedInAsync();
        var batch = await UploadAsync(browser, await AddAccountAsync(browser, "Techcombank"));
        var coffee = await CategoryAsync(browser, "Coffee");
        (await CommitAsync(browser, batch, [2, 5], await CategoryAsync(browser, "Food & drink"), null, (2, coffee))).EnsureSuccessStatusCode();
        (await browser.DeleteAsync($"/api/v1/categories/{coffee}")).EnsureSuccessStatusCode();

        await ImportAsync();

        var failed = (await browser.Http.GetFromJsonAsync<BatchBody>($"{Imports}/{batch.Id}"))!;
        Assert.Equal(("failed", "categoryArchived"), (failed.Status, failed.Failure));
    }

    private async Task ImportAsync()
    {
        while (true)
        {
            await using var scope = api.Services.CreateAsyncScope();
            if ((await scope.ServiceProvider.GetRequiredService<ImportCommits>().ImportNextAsync()).Rows == 0)
            {
                return;
            }
        }
    }

    private static async Task<BatchBody> UploadAsync(Browser browser, Guid accountId)
    {
        var response = await browser.PostFileAsync(Imports, "statement.csv", Encoding.UTF8.GetBytes(Small), new Dictionary<string, string> { ["accountId"] = accountId.ToString() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BatchBody>())!;
    }

    private static async Task<PreviewBody> PreviewAsync(Browser browser, BatchBody batch)
    {
        var query = string.Join('&', Mapping.Select(choice => $"{choice.Key}={Uri.EscapeDataString(choice.Value is bool flag ? (flag ? "true" : "false") : choice.Value!.ToString()!)}"));
        var response = await browser.GetAsync($"{Imports}/{batch.Id}/preview?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PreviewBody>())!;
    }

    private static Task<HttpResponseMessage> CommitAsync(
        Browser browser, BatchBody batch, int[] rows, Guid? expenseCategoryId, Guid? incomeCategoryId, params (int Row, Guid CategoryId)[] rowCategories) =>
        browser.PostAsync($"{Imports}/{batch.Id}/commit", new
        {
            mapping = Mapping,
            rows,
            expenseCategoryId,
            incomeCategoryId,
            rowCategories = rowCategories.Select(row => new { row = row.Row, categoryId = row.CategoryId }),
        });

    private static async Task<List<(string? Payee, Guid? CategoryId)>> ImportedAsync(Browser browser, Guid account, Guid batchId) =>
        [.. (await browser.Http.GetFromJsonAsync<TransactionList>($"/api/v1/transactions?accountId={account}"))!.Items
            .Where(transaction => transaction.ImportBatchId == batchId)
            .OrderBy(transaction => transaction.Date)
            .Select(transaction => (transaction.Payee, transaction.CategoryId))];

    private sealed record BatchBody(Guid Id, string Status, string? Failure);

    private sealed record RowBody(int Number, Guid? SuggestedCategoryId);

    private sealed record PreviewBody(List<RowBody> Rows);

    private sealed record TransactionBody(string Date, string? Payee, Guid? CategoryId, Guid? ImportBatchId);

    private sealed record TransactionList(List<TransactionBody> Items);
}
