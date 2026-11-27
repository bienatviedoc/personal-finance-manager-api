using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// The user's categorisation rules (spec IMP-4, section 11 "Rules"): set up, edited, reordered and deleted, each a
/// match text, a match type, a category and a priority.
/// </summary>
[Collection(ApiCollection.Name)]
public class CategorisationRuleTests(PfmApi api)
{
    private const string Rules = "/api/v1/category-rules";

    [Fact]
    public async Task A_user_sets_up_rules_and_lists_them_highest_priority_first()
    {
        var (browser, _) = await api.SignedInAsync();
        var transport = await CategoryAsync(browser, "Transport");
        var eatingOut = await CategoryAsync(browser, "Eating out");

        var response = await browser.PostAsync(Rules, new { matchText = " GRAB ", matchType = "contains", categoryId = transport, priority = 1 });
        await AddAsync(browser, "grabfood", "startsWith", eatingOut, priority: 5);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var grab = (await response.Content.ReadFromJsonAsync<RuleBody>())!;
        Assert.Equal($"{Rules}/{grab.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(("GRAB", "contains", transport, 1), (grab.MatchText, grab.MatchType, grab.CategoryId, grab.Priority));
        Assert.Equal(["grabfood", "GRAB"], (await ListAsync(browser)).Select(rule => rule.MatchText));
    }

    [Fact]
    public async Task A_rule_set_up_without_a_priority_goes_above_every_other()
    {
        var (browser, _) = await api.SignedInAsync();
        var transport = await CategoryAsync(browser, "Transport");
        await AddAsync(browser, "grab", "contains", transport, priority: 7);

        var added = await AddAsync(browser, "be", "startsWith", transport);

        Assert.Equal(8, added.Priority);
        Assert.Equal(["be", "grab"], (await ListAsync(browser)).Select(rule => rule.MatchText));
        var (other, _) = await api.SignedInAsync();
        Assert.Equal(0, (await AddAsync(other, "grab", "contains", await CategoryAsync(other, "Transport"))).Priority);
    }

    [Fact]
    public async Task A_user_edits_a_rule()
    {
        var (browser, _) = await api.SignedInAsync();
        var rule = await AddAsync(browser, "grab", "contains", await CategoryAsync(browser, "Transport"), priority: 1);
        var eatingOut = await CategoryAsync(browser, "Eating out");

        var response = await browser.PutAsync($"{Rules}/{rule.Id}", new { matchText = "grabfood", matchType = "exactly", categoryId = eatingOut, priority = 3 }, IfMatch(rule));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<RuleBody>())!;
        Assert.Equal((rule.Id, "grabfood", "exactly", eatingOut, 3), (edited.Id, edited.MatchText, edited.MatchType, edited.CategoryId, edited.Priority));
        Assert.NotEqual(rule.RowVersion, edited.RowVersion);
        Assert.Equal(IfMatch(edited), response.Headers.ETag?.Tag);
        Assert.Equal([edited], await ListAsync(browser));
    }

    [Fact]
    public async Task An_edit_made_from_a_stale_row_version_is_refused_with_412_and_one_without_a_row_version_with_428_and_neither_changes_the_rule()
    {
        var (browser, _) = await api.SignedInAsync();
        var transport = await CategoryAsync(browser, "Transport");
        var rule = await AddAsync(browser, "grab", "contains", transport, priority: 1);
        (await browser.PutAsync($"{Rules}/{rule.Id}", new { matchText = "grab bike", matchType = "contains", categoryId = transport }, IfMatch(rule))).EnsureSuccessStatusCode();
        var read = Assert.Single(await ListAsync(browser));

        var stale = await browser.PutAsync($"{Rules}/{rule.Id}", new { matchText = "be", matchType = "contains", categoryId = transport }, IfMatch(rule));
        var none = await browser.PutAsync($"{Rules}/{rule.Id}", new { matchText = "be", matchType = "contains", categoryId = transport });

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionRequired, none.StatusCode);
        Assert.Equal([read], await ListAsync(browser));
    }

    [Fact]
    public async Task A_rule_set_up_answers_its_row_version_as_its_etag()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync(Rules, new { matchText = "grab", matchType = "contains", categoryId = await CategoryAsync(browser, "Transport") });

        var created = (await response.Content.ReadFromJsonAsync<RuleBody>())!;
        Assert.Equal(IfMatch(created), response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task A_user_reorders_the_rules_which_takes_their_priorities_from_the_order()
    {
        var (browser, _) = await api.SignedInAsync();
        var transport = await CategoryAsync(browser, "Transport");
        var a = await AddAsync(browser, "a", "contains", transport, priority: 10);
        var b = await AddAsync(browser, "b", "contains", transport, priority: 10);
        var c = await AddAsync(browser, "c", "contains", transport, priority: 2);

        var response = await browser.PutAsync($"{Rules}/order", Order(c, a, b));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reordered = (await response.Content.ReadFromJsonAsync<RuleList>())!.Items;
        Assert.Equal([("c", 2), ("a", 1), ("b", 0)], reordered.Select(rule => (rule.MatchText, rule.Priority)));
        Assert.Equal(reordered, await ListAsync(browser));
    }

    [Fact]
    public async Task An_order_that_names_a_rule_twice_is_refused_and_changes_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var transport = await CategoryAsync(browser, "Transport");
        var a = await AddAsync(browser, "a", "contains", transport, priority: 1);
        var b = await AddAsync(browser, "b", "contains", transport, priority: 0);

        var response = await browser.PutAsync($"{Rules}/order", Order(b, b, a));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("List each of your rules once.", await response.ErrorOnAsync("rules"));
        Assert.Equal(["a", "b"], (await ListAsync(browser)).Select(rule => rule.MatchText));
    }

    [Fact]
    public async Task An_order_made_from_rules_that_changed_since_they_were_read_is_refused_with_412_and_changes_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var transport = await CategoryAsync(browser, "Transport");
        var a = await AddAsync(browser, "a", "contains", transport, priority: 1);
        var b = await AddAsync(browser, "b", "contains", transport, priority: 0);
        var (other, _) = await api.SignedInAsync();
        var theirs = await AddAsync(other, "x", "contains", await CategoryAsync(other, "Transport"));
        var read = await ListAsync(browser);

        var leftOut = await browser.PutAsync($"{Rules}/order", Order(b));
        var another = await browser.PutAsync($"{Rules}/order", Order(b, a, theirs));
        (await browser.PutAsync($"{Rules}/{a.Id}", new { matchText = "a bike", matchType = "contains", categoryId = transport }, IfMatch(a))).EnsureSuccessStatusCode();
        var edited = await browser.PutAsync($"{Rules}/order", Order(b, a));
        var c = await AddAsync(browser, "c", "contains", transport);
        var added = await browser.PutAsync($"{Rules}/order", Order([.. (await ListAsync(browser)).Where(rule => rule.Id != c.Id).Reverse()]));

        Assert.All([leftOut, another, edited, added], response => Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode));
        Assert.Equal(["c", "a bike", "b"], (await ListAsync(browser)).Select(rule => rule.MatchText));
        Assert.Equal(["a", "b"], read.Select(rule => rule.MatchText));
    }

    [Fact]
    public async Task A_user_deletes_a_rule()
    {
        var (browser, _) = await api.SignedInAsync();
        var rule = await AddAsync(browser, "grab", "contains", await CategoryAsync(browser, "Transport"));

        Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"{Rules}/{rule.Id}")).StatusCode);

        Assert.Empty(await ListAsync(browser));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.DeleteAsync($"{Rules}/{rule.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("", "contains", 1, "matchText")]
    [InlineData("grab", "endsWith", 1, "matchType")]
    [InlineData("grab", null, 1, "matchType")]
    [InlineData("grab", "contains", -1, "priority")]
    public async Task A_rule_without_match_text_a_known_match_type_or_with_a_negative_priority_is_refused_by_name(
        string matchText, string? matchType, int priority, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync(Rules, new { matchText, matchType, categoryId = await CategoryAsync(browser, "Transport"), priority });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_rule_must_be_on_one_of_the_users_categories_that_is_not_archived()
    {
        var (browser, _) = await api.SignedInAsync();
        var taxi = await CategoryAsync(browser, "Transport");
        (await browser.DeleteAsync($"/api/v1/categories/{taxi}")).EnsureSuccessStatusCode();
        var (other, _) = await api.SignedInAsync();

        var archived = await browser.PostAsync(Rules, new { matchText = "grab", matchType = "contains", categoryId = taxi });
        var theirs = await browser.PostAsync(Rules, new { matchText = "grab", matchType = "contains", categoryId = await CategoryAsync(other, "Transport") });

        Assert.Equal("Choose one of your categories that is not archived.", await archived.ErrorOnAsync("categoryId"));
        Assert.Equal("Choose one of your categories that is not archived.", await theirs.ErrorOnAsync("categoryId"));
    }

    [Fact]
    public async Task A_rule_whose_category_is_archived_since_stays_listed_and_can_be_edited_keeping_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var transport = await CategoryAsync(browser, "Transport");
        var rule = await AddAsync(browser, "grab", "contains", transport);
        (await browser.DeleteAsync($"/api/v1/categories/{transport}")).EnsureSuccessStatusCode();

        var response = await browser.PutAsync($"{Rules}/{rule.Id}", new { matchText = "grab bike", matchType = "contains", categoryId = transport, priority = 0 }, IfMatch(rule));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([transport], (await ListAsync(browser)).Select(listed => listed.CategoryId));
    }

    [Fact]
    public async Task A_rule_follows_its_category_into_the_one_it_is_merged_into()
    {
        var (browser, _) = await api.SignedInAsync();
        var transport = await CategoryAsync(browser, "Transport");
        var eatingOut = await CategoryAsync(browser, "Eating out");
        await AddAsync(browser, "grab", "contains", eatingOut);

        (await browser.PostAsync($"/api/v1/categories/{eatingOut}/merge", new { targetId = transport })).EnsureSuccessStatusCode();

        Assert.Equal([transport], (await ListAsync(browser)).Select(rule => rule.CategoryId));
    }

    [Fact]
    public async Task Another_users_rule_is_not_found_and_not_listed()
    {
        var (alice, _) = await api.SignedInAsync();
        var rule = await AddAsync(alice, "grab", "contains", await CategoryAsync(alice, "Transport"));
        var (bob, _) = await api.SignedInAsync();

        var edit = await bob.PutAsync($"{Rules}/{rule.Id}", new { matchText = "x", matchType = "contains", categoryId = await CategoryAsync(bob, "Transport"), priority = 0 }, IfMatch(rule));

        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"{Rules}/{rule.Id}")).StatusCode);
        Assert.Empty(await ListAsync(bob));
        Assert.Single(await ListAsync(alice));
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_is_told_nothing()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateBrowser().GetAsync(Rules)).StatusCode);
    }

    public static async Task<RuleBody> AddAsync(Browser browser, string matchText, string matchType, Guid categoryId, int? priority = null)
    {
        var response = await browser.PostAsync(Rules, new { matchText, matchType, categoryId, priority });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RuleBody>())!;
    }

    private static async Task<List<RuleBody>> ListAsync(Browser browser) => (await browser.Http.GetFromJsonAsync<RuleList>(Rules))!.Items;

    /// <summary>The rule's row version as it was read, in double quotes, ready for <c>If-Match</c>.</summary>
    private static string IfMatch(RuleBody rule) => $"\"{rule.RowVersion}\"";

    /// <summary>A new order of the rules, each with the row version it was read at.</summary>
    private static object Order(params RuleBody[] rules) => new { rules = rules.Select(rule => new { id = rule.Id, rowVersion = rule.RowVersion }) };

    public sealed record RuleBody(Guid Id, string MatchText, string MatchType, Guid CategoryId, int Priority, string RowVersion);

    private sealed record RuleList(List<RuleBody> Items);
}
