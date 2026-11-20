using System.Net;
using System.Net.Http.Json;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AccountTests(PfmApi api)
{
    private const string Accounts = "/api/v1/accounts";

    [Fact]
    public async Task A_user_can_add_an_account_and_its_balance_is_its_opening_balance()
    {
        var (browser, _) = await api.SignedInAsync();
        var vietcombank = await AddInstitutionAsync(browser, "Vietcombank");

        var response = await browser.PostAsync(Accounts, new
        {
            name = "  Everyday  ", type = "current", institutionId = vietcombank, currency = "VND", openingBalance = "1500000", openingDate = "2026-11-01",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<AccountBody>())!;
        Assert.Equal(
            new AccountBody(created.Id, vietcombank, "Everyday", "current", "VND", "1500000.00", null, "2026-11-01", "1500000.00", false, created.RowVersion),
            created);
        Assert.Equal($"{Accounts}/{created.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal($"\"{created.RowVersion}\"", response.Headers.ETag?.Tag);
        Assert.Equal(created, await GetAsync(browser, created.Id));
    }

    [Theory]
    [InlineData("creditCard")]
    [InlineData("loan")]
    public async Task A_credit_card_or_loan_takes_the_amount_owed_as_a_positive_number_and_shows_a_negative_balance(string type)
    {
        var (browser, _) = await api.SignedInAsync();

        var account = await AddAsync(browser, new { name = "Visa", type, currency = "USD", amountOwed = "250.75", openingDate = "2026-11-01" });

        Assert.Equal(("250.75", "-250.75", "-250.75"), (account.AmountOwed, account.OpeningBalance, account.Balance));
        Assert.Null(account.InstitutionId);
    }

    [Theory]
    [InlineData("savings")]
    [InlineData("cash")]
    [InlineData("eWallet")]
    public async Task Every_account_type_can_be_added(string type)
    {
        var (browser, _) = await api.SignedInAsync();

        var account = await AddAsync(browser, new { name = "Money", type, currency = "EUR", openingBalance = "12.5", openingDate = "2026-11-01" });

        Assert.Equal((type, "12.50", "12.50"), (account.Type, account.OpeningBalance, account.Balance));
    }

    [Fact]
    public async Task An_account_needs_a_name_a_type_a_currency_an_amount_and_an_opening_date()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync(Accounts, new { name = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["currency", "name", "openingDate", "type"], await response.ErrorFieldsAsync());
    }

    [Theory]
    [InlineData("current", "VND", "openingBalance", "abc")]
    [InlineData("current", "VND", "openingBalance", "1,500")]
    [InlineData("current", "VND", "openingBalance", "1500.5")]
    [InlineData("current", "USD", "openingBalance", "1.005")]
    [InlineData("current", "USD", "openingBalance", "1e3")]
    [InlineData("current", "USD", "openingBalance", "1000000000000000")]
    [InlineData("current", "USD", "openingBalance", null)]
    [InlineData("current", "USD", "amountOwed", "5")]
    [InlineData("creditCard", "USD", "amountOwed", "-5")]
    [InlineData("creditCard", "USD", "amountOwed", null)]
    [InlineData("loan", "USD", "openingBalance", "5")]
    public async Task An_amount_that_does_not_fit_the_currency_or_the_type_is_refused(string type, string currency, string field, string? amount)
    {
        var (browser, _) = await api.SignedInAsync();
        var body = new Dictionary<string, object?> { ["name"] = "Money", ["type"] = type, ["currency"] = currency, ["openingDate"] = "2026-11-01", [field] = amount };

        var response = await browser.PostAsync(Accounts, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(await response.ErrorFieldsAsync(), refused => refused is "openingBalance" or "amountOwed");
    }

    [Theory]
    [InlineData("type", "mortgage")]
    [InlineData("currency", "JPY")]
    [InlineData("openingDate", "01/11/2026")]
    public async Task A_type_currency_or_date_the_app_does_not_know_is_refused_by_name(string field, string value)
    {
        var (browser, _) = await api.SignedInAsync();
        var body = new Dictionary<string, object?> { ["name"] = "Money", ["type"] = "current", ["currency"] = "VND", ["openingBalance"] = "0", ["openingDate"] = "2026-11-01" };
        body[field] = value;

        var response = await browser.PostAsync(Accounts, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task An_account_can_only_be_put_with_one_of_the_users_own_institutions_that_is_not_archived()
    {
        var (bob, _) = await api.SignedInAsync();
        var bobs = await AddInstitutionAsync(bob, "Bob's bank");
        var (alice, _) = await api.SignedInAsync();
        var archived = await AddInstitutionAsync(alice, "Old bank");
        (await alice.DeleteAsync($"/api/v1/institutions/{archived}")).EnsureSuccessStatusCode();

        foreach (var institutionId in new[] { bobs, archived, Guid.CreateVersion7() })
        {
            var response = await alice.PostAsync(Accounts, new { name = "Everyday", type = "current", institutionId, currency = "VND", openingBalance = "0", openingDate = "2026-11-01" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(["institutionId"], await response.ErrorFieldsAsync());
        }
    }

    [Fact]
    public async Task Editing_an_account_changes_it_and_its_balance_follows_the_new_opening_balance_at_once()
    {
        var (browser, _) = await api.SignedInAsync();
        var acb = await AddInstitutionAsync(browser, "ACB");
        var account = await AddAsync(browser, new { name = "Everyday", type = "current", currency = "VND", openingBalance = "1000", openingDate = "2026-11-01" });

        var response = await browser.PutAsync(
            $"{Accounts}/{account.Id}",
            new { name = " Rainy day ", type = "savings", institutionId = acb, currency = "USD", openingBalance = "2500.5", openingDate = "2026-10-15" },
            ifMatch: Quoted(account.RowVersion));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<AccountBody>())!;
        Assert.Equal(new AccountBody(account.Id, acb, "Rainy day", "savings", "USD", "2500.50", null, "2026-10-15", "2500.50", false, edited.RowVersion), edited);
        Assert.NotEqual(account.RowVersion, edited.RowVersion);
        Assert.Equal(Quoted(edited.RowVersion), response.Headers.ETag?.Tag);
        Assert.Equal(edited, await GetAsync(browser, account.Id));
    }

    [Fact]
    public async Task Editing_an_account_into_a_credit_card_takes_the_amount_owed()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAsync(browser, Current("Everyday"));

        var response = await browser.PutAsync(
            $"{Accounts}/{account.Id}", new { name = "Visa", type = "creditCard", currency = "VND", amountOwed = "300000", openingDate = "2026-11-01" },
            ifMatch: Quoted(account.RowVersion));

        var edited = (await response.Content.ReadFromJsonAsync<AccountBody>())!;
        Assert.Equal(("300000.00", "-300000.00", "-300000.00"), (edited.AmountOwed, edited.OpeningBalance, edited.Balance));
    }

    [Fact]
    public async Task An_edit_made_from_a_stale_row_version_is_refused_with_412_and_changes_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAsync(browser, Current("Everyday"));
        var first = await browser.PutAsync($"{Accounts}/{account.Id}", Current("First edit"), ifMatch: Quoted(account.RowVersion));
        first.EnsureSuccessStatusCode();

        var stale = await browser.PutAsync($"{Accounts}/{account.Id}", Current("Second edit"), ifMatch: Quoted(account.RowVersion));

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("application/problem+json", stale.Content.Headers.ContentType?.MediaType);
        Assert.Equal("First edit", (await GetAsync(browser, account.Id)).Name);
    }

    [Fact]
    public async Task Of_two_edits_made_from_the_same_row_version_at_once_only_one_is_saved()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAsync(browser, Current("Everyday"));

        var answers = await Task.WhenAll(Enumerable.Range(1, 6).Select(number =>
            browser.PutAsync($"{Accounts}/{account.Id}", Current($"Edit {number}"), ifMatch: Quoted(account.RowVersion))));

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5, answers.Count(answer => answer.StatusCode == HttpStatusCode.PreconditionFailed));
    }

    [Theory]
    [InlineData("\"not-a-version\"")]
    [InlineData("12")]
    [InlineData("*")]
    public async Task An_If_Match_that_is_not_a_row_version_the_API_gave_matches_nothing(string ifMatch)
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAsync(browser, Current("Everyday"));

        var response = await browser.PutAsync($"{Accounts}/{account.Id}", Current("Edited"), ifMatch);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task An_edit_that_names_no_row_version_is_refused_with_428()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAsync(browser, Current("Everyday"));

        var response = await browser.PutAsync($"{Accounts}/{account.Id}", Current("Edited"));

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Everyday", (await GetAsync(browser, account.Id)).Name);
    }

    [Fact]
    public async Task An_edit_is_held_to_the_same_field_rules_as_a_new_account()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAsync(browser, Current("Everyday"));

        var response = await browser.PutAsync(
            $"{Accounts}/{account.Id}",
            new { name = new string('x', 101), type = "current", institutionId = Guid.CreateVersion7(), currency = "VND", openingBalance = "0.5", openingDate = "2026-11-01" },
            ifMatch: Quoted(account.RowVersion));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["name", "openingBalance"], await response.ErrorFieldsAsync());

        var unknownInstitution = await browser.PutAsync(
            $"{Accounts}/{account.Id}",
            new { name = "Everyday", type = "current", institutionId = Guid.CreateVersion7(), currency = "VND", openingBalance = "0", openingDate = "2026-11-01" },
            ifMatch: Quoted(account.RowVersion));
        Assert.Equal(["institutionId"], await unknownInstitution.ErrorFieldsAsync());
    }

    [Fact]
    public async Task An_account_keeps_its_institution_through_an_edit_after_the_institution_is_archived()
    {
        var (browser, _) = await api.SignedInAsync();
        var oldBank = await AddInstitutionAsync(browser, "Old bank");
        var account = await AddAsync(browser, new { name = "Everyday", type = "current", institutionId = oldBank, currency = "VND", openingBalance = "0", openingDate = "2026-11-01" });
        (await browser.DeleteAsync($"/api/v1/institutions/{oldBank}")).EnsureSuccessStatusCode();

        var response = await browser.PutAsync(
            $"{Accounts}/{account.Id}",
            new { name = "Renamed", type = "current", institutionId = oldBank, currency = "VND", openingBalance = "0", openingDate = "2026-11-01" },
            ifMatch: Quoted(account.RowVersion));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(oldBank, (await GetAsync(browser, account.Id)).InstitutionId);
    }

    [Fact]
    public async Task Another_users_accounts_are_never_visible_or_editable()
    {
        var (bob, _) = await api.SignedInAsync();
        var bobs = await AddAsync(bob, Current("Bob's money"));
        var (alice, _) = await api.SignedInAsync();
        var path = $"{Accounts}/{bobs.Id}";

        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PutAsync(path, Current("Alice's now"), ifMatch: Quoted(bobs.RowVersion))).StatusCode);

        Assert.Equal(bobs, await GetAsync(bob, bobs.Id));
    }

    [Fact]
    public async Task An_account_that_does_not_exist_is_not_found_as_problem_details()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Accounts}/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_cannot_see_accounts()
    {
        var response = await api.CreateBrowser().GetAsync($"{Accounts}/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A current account in VND that opens empty.</summary>
    private static object Current(string name, string openingBalance = "0") =>
        new { name, type = "current", currency = "VND", openingBalance, openingDate = "2026-11-01" };

    private static string Quoted(string rowVersion) => $"\"{rowVersion}\"";

    private static async Task<Guid> AddInstitutionAsync(Browser browser, string name)
    {
        var response = await browser.PostAsync("/api/v1/institutions", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    private static async Task<AccountBody> AddAsync(Browser browser, object account)
    {
        var response = await browser.PostAsync(Accounts, account);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountBody>())!;
    }

    private static async Task<AccountBody> GetAsync(Browser browser, Guid id)
    {
        var response = await browser.GetAsync($"{Accounts}/{id}");
        response.EnsureSuccessStatusCode();
        var account = (await response.Content.ReadFromJsonAsync<AccountBody>())!;
        Assert.Equal($"\"{account.RowVersion}\"", response.Headers.ETag?.Tag);
        return account;
    }

    private sealed record Identified(Guid Id);

    private sealed record AccountBody(
        Guid Id, Guid? InstitutionId, string Name, string Type, string Currency, string OpeningBalance, string? AmountOwed, string OpeningDate,
        string Balance, bool IsArchived, string RowVersion);
}
