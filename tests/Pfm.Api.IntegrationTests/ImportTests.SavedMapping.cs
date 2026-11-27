using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// The mapping is saved per account (spec IMP-2): the mapping a statement is committed with is offered for the next
/// statement uploaded for the same account, which the user can still change, and the change is saved in turn.
/// </summary>
public partial class ImportTests
{
    private static readonly MappingBody SmallMappingBody = new("semicolon", 0, true, 0, 0, 1, 2, null, null, "dayMonthYear", "comma");

    [Fact]
    public async Task The_mapping_a_statement_is_committed_with_is_saved_for_its_account_and_offered_for_the_next_upload()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var first = await UploadAsync(browser, account, Small);
        (await CommitAsync(browser, first, SmallMapping, [2])).EnsureSuccessStatusCode();

        var next = await UploadAsync(browser, account, Small);
        var elsewhere = await UploadAsync(browser, await AddAccountAsync(browser, "Cash"), Small);

        Assert.Null(first.SavedMapping);
        Assert.Equal(SmallMappingBody, next.SavedMapping);
        Assert.Equal(SmallMappingBody, (await BatchAsync(browser, next.Id)).SavedMapping);
        Assert.Null(elsewhere.SavedMapping);
    }

    [Fact]
    public async Task A_mapping_changed_for_the_next_statement_is_saved_in_place_of_the_last_once_that_statement_is_committed()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        (await CommitAsync(browser, await UploadAsync(browser, account, Small), SmallMapping, [2])).EnsureSuccessStatusCode();
        await ImportAsync();
        var changed = new Dictionary<string, object?>(SmallMapping) { ["descriptionColumn"] = null };

        (await CommitAsync(browser, await UploadAsync(browser, account, Small), changed, [2], expenseCategoryId: await CategoryAsync(browser, "Groceries"))).EnsureSuccessStatusCode();

        Assert.Equal(SmallMappingBody with { DescriptionColumn = null }, (await UploadAsync(browser, account, Small)).SavedMapping);
    }

    [Fact]
    public async Task A_commit_refused_saves_no_mapping()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var batch = await UploadAsync(browser, account, Small);

        var refused = await CommitAsync(browser, batch, SmallMapping, [4]);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Null((await UploadAsync(browser, account, Small)).SavedMapping);
    }
}
