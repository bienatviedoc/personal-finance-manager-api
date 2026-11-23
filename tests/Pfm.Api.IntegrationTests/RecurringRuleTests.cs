using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Recurring rules as the recurring screen uses them: created, edited, paused, resumed and deleted (spec REC-1).</summary>
[Collection(ApiCollection.Name)]
public class RecurringRuleTests(PfmApi api)
{
    private const string Rules = "/api/v1/recurring-rules";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task A_user_can_set_up_a_recurring_expense_and_read_it_back()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND");
        var rent = await CategoryAsync(browser, "Rent");

        var response = await browser.PostAsync(Rules, new
        {
            type = "expense", accountId = everyday, amount = "9000000", categoryId = rent, payee = " Landlord ", note = " flat ",
            frequency = "monthly", startDate = "2030-01-31", endDate = "2030-12-31",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<RuleBody>())!;
        Assert.Equal(
            new RuleBody(created.Id, "expense", everyday, "9000000.00", "VND", rent, "Landlord", "flat", "monthly", null, "2030-01-31", "2030-12-31", "2030-01-31", false),
            created);
        Assert.Equal($"{Rules}/{created.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(created, await browser.Http.GetFromJsonAsync<RuleBody>($"{Rules}/{created.Id}"));
        Assert.Equal([created], (await ListAsync(browser)).Items);
    }

    [Fact]
    public async Task Recurring_income_every_N_days_with_no_end_date_goes_on()
    {
        var (browser, _) = await api.SignedInAsync();
        var rule = await ANewAsync(browser, "income", await AddAccountAsync(browser, "Everyday", "USD"), "1250.50");
        rule["frequency"] = "everyNDays";
        rule["interval"] = 14;

        var created = await AddAsync(browser, rule);

        Assert.Equal(("income", "1250.50", "USD", "everyNDays", 14, null), (created.Type, created.Amount, created.Currency, created.Frequency, created.Interval, created.EndDate));
    }

    [Fact]
    public async Task A_rule_needs_a_type_an_account_an_amount_a_category_a_frequency_and_a_start_date()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync(Rules, new { payee = "Landlord" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["accountId", "amount", "categoryId", "frequency", "startDate", "type"], await response.ErrorFieldsAsync());
    }

    [Theory]
    [InlineData("amount", "0")]
    [InlineData("amount", "1500.5")]
    [InlineData("amount", "abc")]
    [InlineData("type", "transferOut")]
    [InlineData("type", "transfer")]
    [InlineData("frequency", "daily")]
    [InlineData("startDate", "31/01/2030")]
    [InlineData("endDate", "2029-12-31")]
    [InlineData("endDate", "soon")]
    [InlineData("interval", 3)]
    public async Task A_value_a_rule_cannot_take_is_refused_by_name(string field, object value)
    {
        var (browser, _) = await api.SignedInAsync();
        var rule = await ANewAsync(browser, "expense", await AddAccountAsync(browser, "Everyday", "VND"), "100");
        rule[field] = value;

        var response = await browser.PostAsync(Rules, rule);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await response.ErrorFieldsAsync());
        Assert.Empty((await ListAsync(browser)).Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(366)]
    public async Task An_every_N_days_rule_needs_an_interval_of_1_to_365_days(int? interval)
    {
        var (browser, _) = await api.SignedInAsync();
        var rule = await ANewAsync(browser, "expense", await AddAccountAsync(browser, "Everyday"), "100");
        rule["frequency"] = "everyNDays";
        rule["interval"] = interval;

        Assert.Equal(["interval"], await (await browser.PostAsync(Rules, rule)).ErrorFieldsAsync());
    }

    [Theory]
    [InlineData("income", "Groceries")]
    [InlineData("expense", "Salary")]
    public async Task An_income_rule_takes_only_an_income_category_and_an_expense_rule_only_an_expense_category(string type, string category)
    {
        var (browser, _) = await api.SignedInAsync();
        var rule = await ANewAsync(browser, type, await AddAccountAsync(browser, "Everyday"), "10");
        rule["categoryId"] = await CategoryAsync(browser, category);

        Assert.Equal(["categoryId"], await (await browser.PostAsync(Rules, rule)).ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_rule_can_only_be_set_up_on_one_of_the_users_own_accounts_and_categories_that_are_not_archived()
    {
        var (bob, _) = await api.SignedInAsync();
        var bobsAccount = await AddAccountAsync(bob, "Bob's money");
        var bobsGroceries = await CategoryAsync(bob, "Groceries");
        var (alice, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(alice, "Everyday");
        var archivedAccount = await AddAccountAsync(alice, "Old account");
        (await alice.DeleteAsync($"/api/v1/accounts/{archivedAccount}")).EnsureSuccessStatusCode();
        var archivedCategory = await CategoryAsync(alice, "Coffee");
        (await alice.DeleteAsync($"/api/v1/categories/{archivedCategory}")).EnsureSuccessStatusCode();

        foreach (var accountId in new[] { bobsAccount, archivedAccount, Guid.CreateVersion7() })
        {
            Assert.Equal(["accountId"], await (await alice.PostAsync(Rules, await ANewAsync(alice, "expense", accountId, "10"))).ErrorFieldsAsync());
        }

        foreach (var categoryId in new[] { bobsGroceries, archivedCategory, Guid.CreateVersion7() })
        {
            var rule = await ANewAsync(alice, "expense", everyday, "10");
            rule["categoryId"] = categoryId;

            Assert.Equal(["categoryId"], await (await alice.PostAsync(Rules, rule)).ErrorFieldsAsync());
        }

        Assert.Empty((await ListAsync(alice)).Items);
    }

    [Fact]
    public async Task Editing_a_rule_replaces_what_it_says_and_when_it_is_due()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND");
        var dollars = await AddAccountAsync(browser, "Dollars", "USD");
        var rule = await ANewAsync(browser, "expense", everyday, "100");
        rule["payee"] = "Landlord";
        var created = await AddAsync(browser, rule);

        rule["accountId"] = dollars;
        rule["amount"] = "12.34";
        rule["frequency"] = "weekly";
        rule["startDate"] = "2031-03-03";
        rule["endDate"] = "2031-06-30";
        rule.Remove("payee");
        var response = await browser.PutAsync($"{Rules}/{created.Id}", rule);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<RuleBody>())!;
        Assert.Equal(
            created with { AccountId = dollars, Amount = "12.34", Currency = "USD", Payee = null, Frequency = "weekly", StartDate = "2031-03-03", EndDate = "2031-06-30", NextRunDate = "2031-03-03" },
            edited);
        Assert.Equal(edited, await browser.Http.GetFromJsonAsync<RuleBody>($"{Rules}/{created.Id}"));
    }

    [Fact]
    public async Task An_edit_is_held_to_the_same_rules_as_a_new_rule()
    {
        var (browser, _) = await api.SignedInAsync();
        var rule = await ANewAsync(browser, "expense", await AddAccountAsync(browser, "Everyday", "VND"), "100");
        var created = await AddAsync(browser, rule);
        rule["amount"] = "0";
        rule["type"] = "transferIn";

        var response = await browser.PutAsync($"{Rules}/{created.Id}", rule);

        Assert.Equal(["amount", "type"], await response.ErrorFieldsAsync());
        Assert.Equal(created, await browser.Http.GetFromJsonAsync<RuleBody>($"{Rules}/{created.Id}"));
    }

    [Fact]
    public async Task A_paused_rule_says_so_until_it_is_resumed()
    {
        var (browser, _) = await api.SignedInAsync();
        var created = await AddAsync(browser, await ANewAsync(browser, "expense", await AddAccountAsync(browser, "Everyday"), "100"));

        var paused = await browser.PostAsync($"{Rules}/{created.Id}/pause");
        Assert.Equal(created with { IsPaused = true }, await paused.Content.ReadFromJsonAsync<RuleBody>());
        Assert.Equal(created with { IsPaused = true }, (await ListAsync(browser)).Items.Single());

        var resumed = await browser.PostAsync($"{Rules}/{created.Id}/resume");
        Assert.Equal(created, await resumed.Content.ReadFromJsonAsync<RuleBody>());
    }

    [Fact]
    public async Task A_deleted_rule_is_gone()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Everyday");
        var kept = await AddAsync(browser, await ANewAsync(browser, "expense", account, "100"));
        var deleted = await AddAsync(browser, await ANewAsync(browser, "income", account, "200"));

        var response = await browser.DeleteAsync($"{Rules}/{deleted.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Rules}/{deleted.Id}")).StatusCode);
        Assert.Equal([kept], (await ListAsync(browser)).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.DeleteAsync($"{Rules}/{deleted.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_user_cannot_see_or_change_another_users_rule()
    {
        var (bob, _) = await api.SignedInAsync();
        var bobs = await AddAsync(bob, await ANewAsync(bob, "expense", await AddAccountAsync(bob, "Bob's money"), "100"));
        var (alice, _) = await api.SignedInAsync();
        var alices = await ANewAsync(alice, "expense", await AddAccountAsync(alice, "Everyday"), "5");

        Assert.Empty((await ListAsync(alice)).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"{Rules}/{bobs.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PutAsync($"{Rules}/{bobs.Id}", alices)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsync($"{Rules}/{bobs.Id}/pause")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsync($"{Rules}/{bobs.Id}/resume")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"{Rules}/{bobs.Id}")).StatusCode);
        Assert.Equal(bobs, await bob.Http.GetFromJsonAsync<RuleBody>($"{Rules}/{bobs.Id}"));
    }

    [Fact]
    public async Task The_list_is_newest_rule_first_and_comes_in_pages_each_pointing_to_the_next()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Everyday");
        var added = new List<Guid>();
        foreach (var amount in new[] { "1", "2", "3" })
        {
            added.Add((await AddAsync(browser, await ANewAsync(browser, "expense", account, amount))).Id);
        }

        var first = await ListAsync(browser, "?limit=2");
        var second = await ListAsync(browser, $"?limit=2&cursor={first.NextCursor}");

        Assert.Equal([added[2], added[1]], first.Items.Select(rule => rule.Id));
        Assert.Equal([added[0]], second.Items.Select(rule => rule.Id));
        Assert.Null(second.NextCursor);
    }

    [Theory]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=101", "limit")]
    [InlineData("?cursor=not-one", "cursor")]
    public async Task A_page_size_outside_1_to_100_or_a_cursor_the_list_did_not_give_is_refused(string query, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        Assert.Equal([field], await (await browser.GetAsync(Rules + query)).ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_cannot_see_recurring_rules()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateBrowser().GetAsync(Rules)).StatusCode);
    }

    /// <summary>A monthly rule on Rent or Salary that starts in 2030, with the given fields to change.</summary>
    private static async Task<Dictionary<string, object?>> ANewAsync(Browser browser, string type, Guid accountId, string amount, string startDate = "2030-01-31") =>
        new()
        {
            ["type"] = type, ["accountId"] = accountId, ["amount"] = amount,
            ["categoryId"] = await CategoryAsync(browser, type == "income" ? "Salary" : "Rent"),
            ["frequency"] = "monthly", ["startDate"] = startDate,
        };

    private static async Task<RuleBody> AddAsync(Browser browser, Dictionary<string, object?> rule)
    {
        var response = await browser.PostAsync(Rules, rule);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RuleBody>())!;
    }

    private static async Task<RuleList> ListAsync(Browser browser, string query = "")
    {
        var response = await browser.GetAsync(Rules + query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RuleList>())!;
    }

    private sealed record RuleList(RuleBody[] Items, string? NextCursor)
    {
        public bool Equals(RuleList? other) => other is not null && Items.SequenceEqual(other.Items) && NextCursor == other.NextCursor;

        public override int GetHashCode() => Items.Length;
    }

    private sealed record RuleBody(
        Guid Id, string Type, Guid AccountId, string Amount, string Currency, Guid CategoryId, string? Payee, string? Note, string Frequency,
        int? Interval, string StartDate, string? EndDate, string? NextRunDate, bool IsPaused);
}
