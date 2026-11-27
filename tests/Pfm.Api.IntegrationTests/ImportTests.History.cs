using System.Net;
using System.Net.Http.Json;
using System.Text;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>The past import batches: each statement committed, with its file name, when it was uploaded and its counts.</summary>
public partial class ImportTests
{
    [Fact]
    public async Task Past_import_batches_are_listed_newest_first_with_their_file_name_date_and_counts()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var cash = await AddAccountAsync(browser, "Cash");
        var august = await UploadNamedAsync(browser, account, "tcb-2026-08.csv", Small);
        (await CommitAsync(browser, august, SmallMapping, [2, 3])).EnsureSuccessStatusCode();
        await ImportAsync();
        var wallet = await UploadNamedAsync(browser, cash, "wallet.csv", Small);
        (await CommitAsync(browser, wallet, SmallMapping, [5])).EnsureSuccessStatusCode();
        await UploadNamedAsync(browser, account, "not-yet.csv", Small);

        var listed = (await browser.Http.GetFromJsonAsync<BatchList>(Imports))!;
        var onAccount = (await browser.Http.GetFromJsonAsync<BatchList>($"{Imports}?accountId={account}"))!;

        Assert.Equal(
            [("wallet.csv", "committing", 4, 1, 0, 0, 1), ("tcb-2026-08.csv", "committed", 4, 2, 2, 0, 1)],
            listed.Items.Select(batch => (batch.FileName, batch.Status, batch.RowCount, batch.SelectedCount, batch.ImportedCount, batch.DuplicateCount, batch.FailedCount)));
        Assert.True((listed.Items[1].CreatedAt - august.CreatedAt).Duration() < TimeSpan.FromMilliseconds(1));
        Assert.Null(listed.NextCursor);
        Assert.Equal(["tcb-2026-08.csv"], onAccount.Items.Select(batch => batch.FileName));
    }

    [Fact]
    public async Task Past_import_batches_are_listed_a_page_at_a_time_and_only_the_users_own()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        foreach (var name in new[] { "1.csv", "2.csv", "3.csv" })
        {
            (await CommitAsync(browser, await UploadNamedAsync(browser, account, name, Small), SmallMapping, [2])).EnsureSuccessStatusCode();
            await ImportAsync();
        }

        var (stranger, _) = await api.SignedInAsync();
        var first = (await browser.Http.GetFromJsonAsync<BatchList>($"{Imports}?limit=2"))!;
        var second = (await browser.Http.GetFromJsonAsync<BatchList>($"{Imports}?limit=2&cursor={first.NextCursor}"))!;
        var badCursor = await browser.GetAsync($"{Imports}?cursor=nonsense");

        Assert.Equal(["3.csv", "2.csv"], first.Items.Select(batch => batch.FileName));
        Assert.Equal(["1.csv"], second.Items.Select(batch => batch.FileName));
        Assert.Null(second.NextCursor);
        Assert.Empty((await stranger.Http.GetFromJsonAsync<BatchList>(Imports))!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
        Assert.Equal(["cursor"], await badCursor.ErrorFieldsAsync());
    }

    private static async Task<BatchBody> UploadNamedAsync(Browser browser, Guid accountId, string fileName, string text)
    {
        var response = await UploadAsync(browser, accountId, fileName, Encoding.UTF8.GetBytes(text));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BatchBody>())!;
    }

    private sealed record BatchList(List<BatchBody> Items, string? NextCursor);
}
