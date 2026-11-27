using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Imports;
using Pfm.Domain.Imports;
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
    public async Task The_categories_a_statement_is_committed_with_are_saved_with_its_mapping_and_offered_for_the_next_upload()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var groceries = await CategoryAsync(browser, "Groceries");
        var salary = await CategoryAsync(browser, "Salary");
        (await CommitAsync(browser, await UploadAsync(browser, account, Small), SmallMapping, [2, 3], groceries, salary)).EnsureSuccessStatusCode();
        await ImportAsync();

        var next = await UploadAsync(browser, account, Small);
        var elsewhere = await UploadAsync(browser, await AddAccountAsync(browser, "Cash"), Small);

        Assert.Equal((groceries, salary), (next.SavedExpenseCategoryId, next.SavedIncomeCategoryId));
        Assert.Equal((groceries, salary), ((await BatchAsync(browser, next.Id)).SavedExpenseCategoryId, (await BatchAsync(browser, next.Id)).SavedIncomeCategoryId));
        Assert.Equal((null, null), (elsewhere.SavedExpenseCategoryId, elsewhere.SavedIncomeCategoryId));
    }

    [Fact]
    public async Task A_commit_that_needs_no_category_of_one_kind_keeps_the_one_saved_for_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var salary = await CategoryAsync(browser, "Salary");
        (await CommitAsync(browser, await UploadAsync(browser, account, Small), SmallMapping, [2, 3], await CategoryAsync(browser, "Groceries"), salary)).EnsureSuccessStatusCode();
        await ImportAsync();
        var transport = await CategoryAsync(browser, "Transport");

        var spendingOnly = await UploadAsync(browser, account, Small);
        (await browser.PostAsync($"{Imports}/{spendingOnly.Id}/commit", new { mapping = SmallMapping, rows = new[] { 5 }, expenseCategoryId = transport })).EnsureSuccessStatusCode();
        await ImportAsync();

        var next = await UploadAsync(browser, account, Small);
        Assert.Equal((transport, salary), (next.SavedExpenseCategoryId, next.SavedIncomeCategoryId));
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

    [Fact]
    public async Task A_commit_whose_account_had_its_mapping_saved_by_another_commit_meanwhile_is_committed_and_saves_its_own()
    {
        var race = new MappingSavedMeanwhile();
        var racing = api.WithServices(services =>
        {
            var saved = services.Last(service => service.ServiceType == typeof(IImportMappingRepository));
            services.AddScoped<IImportMappingRepository>(provider =>
                race.Hiding((IImportMappingRepository)ActivatorUtilities.CreateInstance(provider, saved.ImplementationType!)));
        });
        var (browser, _) = await api.SignedInAsync(PfmApi.CreateBrowser(racing));
        var account = await AddAccountAsync(browser, "Techcombank");
        (await CommitAsync(browser, await UploadAsync(browser, account, Small), SmallMapping, [2])).EnsureSuccessStatusCode();
        await ImportAsync();
        var changed = new Dictionary<string, object?>(SmallMapping) { ["descriptionColumn"] = null };
        var next = await UploadAsync(browser, account, Small);

        // The commit reads no saved mapping, as when another commit saves the account's first one a moment later.
        race.HideTheNextOne();
        var committed = await CommitAsync(browser, next, changed, [2]);

        Assert.Equal(HttpStatusCode.Accepted, committed.StatusCode);
        Assert.Equal("committing", (await BatchAsync(browser, next.Id)).Status);
        Assert.Equal(SmallMappingBody with { DescriptionColumn = null }, (await UploadAsync(browser, account, Small)).SavedMapping);
    }

    /// <summary>Saved mappings, one of which is not found once when asked: it is saved a moment after it is looked for.</summary>
    private sealed class MappingSavedMeanwhile
    {
        private int _hide;

        public void HideTheNextOne() => Interlocked.Exchange(ref _hide, 1);

        public IImportMappingRepository Hiding(IImportMappingRepository mappings) => new Hidden(this, mappings);

        private sealed class Hidden(MappingSavedMeanwhile race, IImportMappingRepository mappings) : IImportMappingRepository
        {
            public async Task<ImportMapping?> FindAsync(Guid accountId, CancellationToken cancellationToken) =>
                Interlocked.Exchange(ref race._hide, 0) == 1 ? null : await mappings.FindAsync(accountId, cancellationToken);

            public void Add(ImportMapping mapping) => mappings.Add(mapping);
        }
    }
}
