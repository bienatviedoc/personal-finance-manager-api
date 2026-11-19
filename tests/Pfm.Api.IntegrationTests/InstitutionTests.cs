using System.Net;
using System.Net.Http.Json;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class InstitutionTests(PfmApi api)
{
    private const string Institutions = "/api/v1/institutions";

    [Fact]
    public async Task A_user_can_add_an_institution_and_find_it_in_their_list()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync(Institutions, new { name = "Vietcombank", color = "#007a3d", logoKey = "bank" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<InstitutionBody>())!;
        Assert.Equal(new InstitutionBody(created.Id, "Vietcombank", "#007a3d", "bank", false), created);
        Assert.Equal($"{Institutions}/{created.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal([created], await ListAsync(browser));
    }

    [Fact]
    public async Task A_user_can_edit_an_institution()
    {
        var (browser, _) = await api.SignedInAsync();
        var institution = await AddAsync(browser, "Vietcombank");

        var response = await browser.PutAsync($"{Institutions}/{institution.Id}", new { name = "  VCB  ", color = "#FFAA00", logoKey = "building" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = new InstitutionBody(institution.Id, "VCB", "#ffaa00", "building", false);
        Assert.Equal(edited, await response.Content.ReadFromJsonAsync<InstitutionBody>());
        Assert.Equal(edited, await GetAsync(browser, institution.Id));
    }

    [Fact]
    public async Task Colour_and_logo_are_optional_and_can_be_taken_away()
    {
        var (browser, _) = await api.SignedInAsync();
        var institution = await AddAsync(browser, "MoMo", color: "#a50064", logoKey: "wallet");

        var response = await browser.PutAsync($"{Institutions}/{institution.Id}", new { name = "MoMo" });

        Assert.Equal(new InstitutionBody(institution.Id, "MoMo", null, null, false), await response.Content.ReadFromJsonAsync<InstitutionBody>());
    }

    [Fact]
    public async Task An_institution_needs_a_name_and_a_well_formed_colour_and_logo()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync(Institutions, new { name = " ", color = "green", logoKey = "vietcombank" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["color", "logoKey", "name"], await ErrorFieldsAsync(response));
        Assert.Empty(await ListAsync(browser));
    }

    [Fact]
    public async Task A_name_longer_than_100_characters_is_refused_when_editing_too()
    {
        var (browser, _) = await api.SignedInAsync();
        var institution = await AddAsync(browser, "Techcombank");

        var response = await browser.PutAsync($"{Institutions}/{institution.Id}", new { name = new string('x', 101) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["name"], await ErrorFieldsAsync(response));
        Assert.Equal("Techcombank", (await GetAsync(browser, institution.Id)).Name);
    }

    [Fact]
    public async Task The_list_is_in_order_of_name()
    {
        var (browser, _) = await api.SignedInAsync();
        await AddAsync(browser, "techcombank");
        await AddAsync(browser, "MoMo");
        await AddAsync(browser, "ACB");

        Assert.Equal(["ACB", "MoMo", "techcombank"], (await ListAsync(browser)).Select(institution => institution.Name));
    }

    [Fact]
    public async Task The_list_comes_in_pages_each_pointing_to_the_next()
    {
        var (browser, _) = await api.SignedInAsync();
        await AddAsync(browser, "techcombank");
        await AddAsync(browser, "MoMo");
        await AddAsync(browser, "ACB");

        var first = await PageAsync(browser, "?limit=2");
        var second = await PageAsync(browser, $"?limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");

        Assert.Equal(["ACB", "MoMo"], first.Items.Select(institution => institution.Name));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(["techcombank"], second.Items.Select(institution => institution.Name));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task A_page_holds_50_institutions_unless_asked_for_another_size()
    {
        var (browser, _) = await api.SignedInAsync();
        for (var number = 1; number <= 51; number++)
        {
            await AddAsync(browser, $"Bank {number:D2}");
        }

        var first = await PageAsync(browser);
        var rest = await PageAsync(browser, $"?cursor={Uri.EscapeDataString(first.NextCursor!)}");

        Assert.Equal(50, first.Items.Length);
        Assert.Equal(["Bank 51"], rest.Items.Select(institution => institution.Name));
        Assert.Null(rest.NextCursor);
    }

    [Theory]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=101", "limit")]
    [InlineData("?cursor=not-a-cursor", "cursor")]
    public async Task A_page_size_outside_1_to_100_or_a_cursor_the_list_did_not_give_is_refused(string query, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync(Institutions + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task Deleting_an_institution_archives_it_so_it_leaves_the_list_but_can_still_be_shown_where_it_is_used()
    {
        var (browser, _) = await api.SignedInAsync();
        var kept = await AddAsync(browser, "ACB");
        var archived = await AddAsync(browser, "Old bank");

        var response = await browser.DeleteAsync($"{Institutions}/{archived.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([kept], await ListAsync(browser));
        Assert.Equal(archived with { IsArchived = true }, await GetAsync(browser, archived.Id));
        Assert.Equal([kept, archived with { IsArchived = true }], await ListAsync(browser, "?includeArchived=true"));
    }

    [Fact]
    public async Task Archiving_an_institution_twice_changes_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var institution = await AddAsync(browser, "Old bank");
        await browser.DeleteAsync($"{Institutions}/{institution.Id}");

        var again = await browser.DeleteAsync($"{Institutions}/{institution.Id}");

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.True((await GetAsync(browser, institution.Id)).IsArchived);
    }

    [Fact]
    public async Task Another_users_institutions_are_never_visible_or_editable()
    {
        var (bob, _) = await api.SignedInAsync();
        var bobs = await AddAsync(bob, "Bob's bank");
        var (alice, _) = await api.SignedInAsync();
        var path = $"{Institutions}/{bobs.Id}";

        Assert.Empty(await ListAsync(alice, "?includeArchived=true"));
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PutAsync(path, new { name = "Alice's now" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync(path)).StatusCode);

        Assert.Equal(bobs, await GetAsync(bob, bobs.Id));
    }

    [Fact]
    public async Task An_institution_that_does_not_exist_is_not_found_as_problem_details()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Institutions}/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_cannot_see_institutions()
    {
        var response = await api.CreateBrowser().GetAsync(Institutions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<InstitutionBody> AddAsync(Browser browser, string name, string? color = null, string? logoKey = null)
    {
        var response = await browser.PostAsync(Institutions, new { name, color, logoKey });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InstitutionBody>())!;
    }

    private static async Task<InstitutionBody> GetAsync(Browser browser, Guid id)
    {
        var response = await browser.GetAsync($"{Institutions}/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InstitutionBody>())!;
    }

    private static async Task<string[]> ErrorFieldsAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. json.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).Order()];
    }

    private static async Task<InstitutionBody[]> ListAsync(Browser browser, string query = "") => (await PageAsync(browser, query)).Items;

    private static async Task<InstitutionList> PageAsync(Browser browser, string query = "")
    {
        var response = await browser.GetAsync(Institutions + query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InstitutionList>())!;
    }

    private sealed record InstitutionBody(Guid Id, string Name, string? Color, string? LogoKey, bool IsArchived);

    private sealed record InstitutionList(InstitutionBody[] Items, string? NextCursor);
}
