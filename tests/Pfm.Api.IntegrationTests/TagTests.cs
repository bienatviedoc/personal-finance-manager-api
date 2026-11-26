using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Tags: free-form labels on transactions that cut across categories (spec TXN-5, section 11).</summary>
[Collection(ApiCollection.Name)]
public class TagTests(PfmApi api)
{
    private const string Transactions = "/api/v1/transactions";

    [Fact]
    public async Task A_transaction_is_recorded_with_its_tags_which_come_back_in_order_trimmed_and_in_lower_case()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");

        var tagged = await RecordAsync(browser, everyday, " Japan-Trip ", "tet-2027", "japan-trip");

        Assert.Equal(["japan-trip", "tet-2027"], tagged.Tags);
        Assert.Equal(tagged.Tags, (await GetAsync(browser, tagged.Id)).Tags);
        Assert.Equal(tagged.Tags, Assert.Single((await ListAsync(browser)).Items).Tags);
    }

    [Fact]
    public async Task An_edit_replaces_the_tags_and_one_without_them_takes_every_tag_off()
    {
        var (browser, _) = await api.SignedInAsync();
        var tagged = await RecordAsync(browser, await AddAccountAsync(browser, "Everyday"), "japan-trip", "tet-2027");

        var edited = await EditAsync(browser, tagged, ["tet-2027", "gifts"]);
        var untagged = await EditAsync(browser, edited, tags: null);

        Assert.Equal(["gifts", "tet-2027"], edited.Tags);
        Assert.Empty(untagged.Tags);
        Assert.Empty((await GetAsync(browser, tagged.Id)).Tags);
    }

    [Fact]
    public async Task Tags_are_created_on_first_use_and_listed_by_name_for_the_user_who_used_them_alone()
    {
        var (bob, _) = await api.SignedInAsync();
        await RecordAsync(bob, await AddAccountAsync(bob, "Bob's"), "bobs-tag");
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        Assert.Empty(await TagNamesAsync(browser));

        await RecordAsync(browser, everyday, "tet-2027", "japan-trip");
        await RecordAsync(browser, everyday, "Japan-Trip", "gifts");

        Assert.Equal(["gifts", "japan-trip", "tet-2027"], await TagNamesAsync(browser));
        Assert.Equal(["bobs-tag"], await TagNamesAsync(bob));
    }

    [Fact]
    public async Task The_tag_list_suggests_the_tags_whose_name_contains_what_is_typed_a_page_at_a_time()
    {
        var (browser, _) = await api.SignedInAsync();
        await RecordAsync(browser, await AddAccountAsync(browser, "Everyday"), "japan-trip", "tet-2027", "trip-hanoi", "gifts");

        Assert.Equal(["japan-trip", "trip-hanoi"], await TagNamesAsync(browser, "?q=TRIP"));
        var first = (await browser.Http.GetFromJsonAsync<TagList>("/api/v1/tags?limit=3"))!;
        var second = (await browser.Http.GetFromJsonAsync<TagList>($"/api/v1/tags?limit=3&cursor={first.NextCursor}"))!;
        Assert.Equal(["gifts", "japan-trip", "tet-2027"], first.Items.Select(tag => tag.Name));
        Assert.Equal(["trip-hanoi"], second.Items.Select(tag => tag.Name));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task The_list_can_be_narrowed_to_a_tag_in_any_case_and_its_total_counts_only_those()
    {
        var (bob, _) = await api.SignedInAsync();
        await RecordAsync(bob, await AddAccountAsync(bob, "Bob's"), "japan-trip");
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var trip = await RecordAsync(browser, everyday, "japan-trip", "tet-2027");
        var tet = await RecordAsync(browser, everyday, "tet-2027");
        await RecordAsync(browser, everyday);

        var onTrip = await ListAsync(browser, "?tag=Japan-Trip");

        Assert.Equal([trip.Id], onTrip.Items.Select(transaction => transaction.Id));
        Assert.Equal(1, onTrip.Total.Count);
        Assert.Equal([tet.Id, trip.Id], (await ListAsync(browser, "?tag=tet-2027")).Items.Select(transaction => transaction.Id));
        Assert.Empty((await ListAsync(browser, "?tag=never-used")).Items);
        Assert.Equal(["tag"], await (await browser.GetAsync($"{Transactions}?tag={new string('t', 51)}")).ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_transaction_takes_at_most_twenty_tags_of_at_most_fifty_characters_and_with_too_many_nothing_is_recorded()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await CategoryAsync(browser, "Groceries");
        object Expense(params string[] tags) => new { type = "expense", accountId = everyday, amount = "1000", date = "2026-11-02", categoryId = groceries, tags };

        var tooLong = await browser.PostAsync(Transactions, Expense(new string('t', 51)));
        var blank = await browser.PostAsync(Transactions, Expense("  "));
        var tooMany = await browser.PostAsync(Transactions, Expense([.. Enumerable.Range(1, 21).Select(number => $"tag-{number}")]));
        var twentyOnceEach = await browser.PostAsync(Transactions, Expense([.. Enumerable.Range(1, 20).Select(number => $"tag-{number}"), "TAG-1"]));

        Assert.Equal(["tags"], await tooLong.ErrorFieldsAsync());
        Assert.Equal(["tags"], await blank.ErrorFieldsAsync());
        Assert.Equal(["tags"], await tooMany.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.Created, twentyOnceEach.StatusCode);
        Assert.Single((await ListAsync(browser)).Items);
    }

    [Fact]
    public async Task Two_transactions_recorded_at_once_with_the_same_new_tag_share_one_tag()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");

        var recorded = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RecordAsync(browser, everyday, "japan-trip")));

        Assert.All(recorded, transaction => Assert.Equal(["japan-trip"], transaction.Tags));
        Assert.Equal(["japan-trip"], await TagNamesAsync(browser));
        Assert.Equal(4, (await ListAsync(browser, "?tag=japan-trip")).Items.Length);
    }

    [Fact]
    public async Task A_retry_with_the_same_tags_in_any_order_records_nothing_and_other_tags_under_the_same_key_are_refused()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await CategoryAsync(browser, "Groceries");
        object Expense(params string[] tags) => new { type = "expense", accountId = everyday, amount = "1000", date = "2026-11-02", categoryId = groceries, tags };
        var key = Guid.NewGuid().ToString();

        var first = await browser.PostAsync(Transactions, Expense("japan-trip", "tet-2027"), key);
        var retry = await browser.PostAsync(Transactions, Expense("Tet-2027", "japan-trip"), key);
        var other = await browser.PostAsync(Transactions, Expense("japan-trip"), key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<TransactionBody>())!.Id, (await retry.Content.ReadFromJsonAsync<TransactionBody>())!.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
    }

    private async Task<TransactionBody> RecordAsync(Browser browser, Guid accountId, params string[] tags)
    {
        var response = await browser.PostAsync(Transactions, new
        {
            type = "expense", accountId, amount = "100000", date = "2026-11-02", categoryId = await CategoryAsync(browser, "Groceries"), tags,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    private static async Task<TransactionBody> EditAsync(Browser browser, TransactionBody transaction, string[]? tags)
    {
        var response = await browser.PutAsync(
            $"{Transactions}/{transaction.Id}",
            new { type = "expense", accountId = transaction.AccountId, amount = transaction.Amount, date = "2026-11-02", categoryId = transaction.CategoryId, tags },
            ifMatch: $"\"{transaction.RowVersion}\"");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    private static async Task<string[]> TagNamesAsync(Browser browser, string query = "") =>
        [.. (await browser.Http.GetFromJsonAsync<TagList>("/api/v1/tags" + query))!.Items.Select(tag => tag.Name)];

    private static async Task<TransactionBody> GetAsync(Browser browser, Guid id) =>
        (await browser.Http.GetFromJsonAsync<TransactionBody>($"{Transactions}/{id}"))!;

    private static async Task<TransactionList> ListAsync(Browser browser, string query = "") =>
        (await browser.Http.GetFromJsonAsync<TransactionList>(Transactions + query))!;

    private sealed record TransactionList(TransactionBody[] Items, TotalBody Total);

    private sealed record TotalBody(int Count);

    private sealed record TransactionBody(Guid Id, Guid AccountId, Guid CategoryId, string Amount, string RowVersion, string[] Tags);

    private sealed record TagList(TagBody[] Items, string? NextCursor);

    private sealed record TagBody(Guid Id, string Name);
}
