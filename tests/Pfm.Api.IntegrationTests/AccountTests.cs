using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Infrastructure.Persistence;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

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

    [Theory]
    [InlineData("current", "openingBalance")]
    [InlineData("cash", "openingBalance")]
    [InlineData("creditCard", "amountOwed")]
    [InlineData("loan", "amountOwed")]
    public async Task Any_account_can_be_labelled_with_the_last_4_digits_of_its_number(string type, string amountField)
    {
        var (browser, _) = await api.SignedInAsync();
        var body = new Dictionary<string, object?> { ["name"] = "Money", ["type"] = type, ["currency"] = "VND", ["openingDate"] = "2026-11-01", [amountField] = "0", ["last4"] = "0042" };

        var account = await AddAsync(browser, body);

        Assert.Equal("0042", account.Last4);
        Assert.Equal("0042", (await GetAsync(browser, account.Id)).Last4);
        Assert.Equal("0042", Assert.Single(await ListAsync(browser)).Last4);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("12 4")]
    [InlineData("4111111111111111")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("0011000123456")]
    public async Task A_full_account_or_card_number_is_never_accepted_as_the_last_4_digits_nor_stored(string last4)
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAsync(browser, Current("Everyday"));
        var body = new { name = "Money", type = "current", currency = "VND", openingBalance = "0", openingDate = "2026-11-01", last4 };

        var added = await browser.PostAsync(Accounts, body);
        var edited = await browser.PutAsync($"{Accounts}/{account.Id}", body, ifMatch: Quoted(account.RowVersion));

        Assert.Equal(HttpStatusCode.BadRequest, added.StatusCode);
        Assert.Equal(["last4"], await added.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.BadRequest, edited.StatusCode);
        Assert.Equal(["last4"], await edited.ErrorFieldsAsync());
        Assert.Equal([null], (await ListAsync(browser)).Select(listed => listed.Last4));
    }

    [Fact]
    public async Task An_edit_changes_the_last_4_digits_and_leaving_them_out_removes_the_label()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAsync(browser, new { name = "Everyday", type = "current", currency = "VND", openingBalance = "0", openingDate = "2026-11-01", last4 = "0042" });

        var relabelled = await EditAsync(browser, account, new { name = "Everyday", type = "current", currency = "VND", openingBalance = "0", openingDate = "2026-11-01", last4 = "9001" });
        var unlabelled = await EditAsync(browser, relabelled, Current("Everyday"));

        Assert.Equal("9001", relabelled.Last4);
        Assert.Null(unlabelled.Last4);
        Assert.Null((await GetAsync(browser, account.Id)).Last4);
    }

    [Fact]
    public async Task A_credit_card_has_a_credit_limit_a_statement_day_and_a_payment_due_day_and_shows_its_available_credit()
    {
        var (browser, _) = await api.SignedInAsync();

        var card = await AddAsync(browser, new
        {
            name = "Visa", type = "creditCard", currency = "USD", amountOwed = "250.75", openingDate = "2026-11-01", last4 = "4242", creditLimit = "5000",
            statementDay = 25, paymentDueDay = 15,
        });

        Assert.Equal(("5000.00", 25, 15), (card.CreditLimit, card.StatementDay, card.PaymentDueDay));
        Assert.Equal(("250.75", "-250.75", "4749.25"), (card.AmountOwed, card.Balance, card.AvailableCredit));
        Assert.Equal(card, await GetAsync(browser, card.Id));
        Assert.Equal(card, Assert.Single(await ListAsync(browser)));
    }

    [Fact]
    public async Task Available_credit_follows_an_edit_of_the_credit_limit_or_the_amount_owed_at_once_and_goes_below_zero_over_the_limit()
    {
        var (browser, _) = await api.SignedInAsync();
        var card = await AddAsync(browser, new { name = "Visa", type = "creditCard", currency = "VND", amountOwed = "2000000", openingDate = "2026-11-01", creditLimit = "10000000" });

        var raised = await EditAsync(browser, card, new { name = "Visa", type = "creditCard", currency = "VND", amountOwed = "3000000", openingDate = "2026-11-01", creditLimit = "20000000" });
        var over = await EditAsync(browser, raised, new { name = "Visa", type = "creditCard", currency = "VND", amountOwed = "3000000", openingDate = "2026-11-01", creditLimit = "2500000" });

        Assert.Equal("8000000.00", card.AvailableCredit);
        Assert.Equal("17000000.00", raised.AvailableCredit);
        Assert.Equal("-500000.00", over.AvailableCredit);
    }

    [Fact]
    public async Task A_credit_cards_fields_are_optional_and_with_no_credit_limit_there_is_no_available_credit()
    {
        var (browser, _) = await api.SignedInAsync();

        var bare = await AddAsync(browser, new { name = "Visa", type = "creditCard", currency = "USD", amountOwed = "250.75", openingDate = "2026-11-01" });
        var daysOnly = await AddAsync(browser, new { name = "Mastercard", type = "creditCard", currency = "USD", amountOwed = "0", openingDate = "2026-11-01", statementDay = 31, paymentDueDay = 1 });

        Assert.Equal((null, null, null, null), (bare.CreditLimit, bare.StatementDay, bare.PaymentDueDay, bare.AvailableCredit));
        Assert.Equal((null, 31, 1, null), (daysOnly.CreditLimit, daysOnly.StatementDay, daysOnly.PaymentDueDay, daysOnly.AvailableCredit));
    }

    [Theory]
    [InlineData("creditLimit", "-1")]
    [InlineData("creditLimit", "abc")]
    [InlineData("creditLimit", "1,000")]
    [InlineData("creditLimit", "10.005")]
    [InlineData("creditLimit", "1000000000000000")]
    [InlineData("statementDay", 0)]
    [InlineData("statementDay", 32)]
    [InlineData("paymentDueDay", 0)]
    [InlineData("paymentDueDay", -3)]
    [InlineData("paymentDueDay", 32)]
    public async Task A_credit_limit_that_is_not_an_amount_or_a_day_that_is_not_a_day_of_the_month_is_refused_by_name(string field, object value)
    {
        var (browser, _) = await api.SignedInAsync();
        var body = new Dictionary<string, object?> { ["name"] = "Visa", ["type"] = "creditCard", ["currency"] = "USD", ["amountOwed"] = "0", ["openingDate"] = "2026-11-01", [field] = value };

        var response = await browser.PostAsync(Accounts, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await response.ErrorFieldsAsync());
    }

    [Theory]
    [InlineData("current", "openingBalance")]
    [InlineData("savings", "openingBalance")]
    [InlineData("loan", "amountOwed")]
    public async Task Only_a_credit_card_takes_a_credit_limit_a_statement_day_or_a_payment_due_day(string type, string amountField)
    {
        var (browser, _) = await api.SignedInAsync();
        var body = new Dictionary<string, object?>
        {
            ["name"] = "Money", ["type"] = type, ["currency"] = "USD", ["openingDate"] = "2026-11-01", [amountField] = "0",
            ["creditLimit"] = "5000", ["statementDay"] = 25, ["paymentDueDay"] = 15,
        };

        var response = await browser.PostAsync(Accounts, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["creditLimit", "paymentDueDay", "statementDay"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task Editing_a_credit_card_into_another_type_leaves_its_credit_card_fields_behind_and_keeps_its_last_4_digits()
    {
        var (browser, _) = await api.SignedInAsync();
        var card = await AddAsync(browser, new
        {
            name = "Visa", type = "creditCard", currency = "USD", amountOwed = "0", openingDate = "2026-11-01", last4 = "4242", creditLimit = "5000", statementDay = 25, paymentDueDay = 15,
        });

        var current = await EditAsync(browser, card, new { name = "Everyday", type = "current", currency = "USD", openingBalance = "0", openingDate = "2026-11-01", last4 = "4242" });

        Assert.Equal(("4242", null, null, null, null), (current.Last4, current.CreditLimit, current.StatementDay, current.PaymentDueDay, current.AvailableCredit));
    }

    [Fact]
    public async Task The_list_is_in_order_of_name_with_each_accounts_balance()
    {
        var (browser, _) = await api.SignedInAsync();
        await AddAsync(browser, Current("travel fund", "300"));
        await AddAsync(browser, new { name = "Visa", type = "creditCard", currency = "VND", amountOwed = "200", openingDate = "2026-11-01" });
        await AddAsync(browser, Current("Everyday", "1000"));

        var accounts = await ListAsync(browser);

        Assert.Equal(["Everyday", "travel fund", "Visa"], accounts.Select(account => account.Name));
        Assert.Equal(["1000.00", "300.00", "-200.00"], accounts.Select(account => account.Balance));
    }

    [Fact]
    public async Task The_list_totals_the_balances_of_each_institution_in_each_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        var vietcombank = await AddInstitutionAsync(browser, "Vietcombank");
        var momo = await AddInstitutionAsync(browser, "MoMo");
        await AddAsync(browser, new { name = "Everyday", type = "current", institutionId = vietcombank, currency = "VND", openingBalance = "1000000", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Visa", type = "creditCard", institutionId = vietcombank, currency = "VND", amountOwed = "200000", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Dollars", type = "savings", institutionId = vietcombank, currency = "USD", openingBalance = "50.25", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Wallet", type = "eWallet", institutionId = momo, currency = "VND", openingBalance = "70000", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Pocket", type = "cash", currency = "VND", openingBalance = "5000", openingDate = "2026-11-01" });
        await AddAsync(browser, new { name = "Jar", type = "cash", currency = "VND", openingBalance = "500", openingDate = "2026-11-01" });

        var page = await PageAsync(browser);

        Assert.Equal(
            [
                new TotalBody(null, "VND", "5500.00"),
                new TotalBody(momo, "VND", "70000.00"),
                new TotalBody(vietcombank, "USD", "50.25"),
                new TotalBody(vietcombank, "VND", "800000.00"),
            ],
            page.Totals.OrderBy(total => total.InstitutionId == vietcombank).ThenBy(total => total.InstitutionId == momo).ThenBy(total => total.Currency));
    }

    [Fact]
    public async Task The_list_comes_in_pages_each_pointing_to_the_next_and_every_page_carries_the_totals_of_the_whole_list()
    {
        var (browser, _) = await api.SignedInAsync();
        await AddAsync(browser, Current("travel fund", "300"));
        await AddAsync(browser, Current("Everyday", "1000"));
        await AddAsync(browser, Current("Rainy day", "50"));

        var first = await PageAsync(browser, "?limit=2");
        var second = await PageAsync(browser, $"?limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");

        Assert.Equal(["Everyday", "Rainy day"], first.Items.Select(account => account.Name));
        Assert.Equal(["travel fund"], second.Items.Select(account => account.Name));
        Assert.Null(second.NextCursor);
        Assert.Equal([new TotalBody(null, "VND", "1350.00")], first.Totals);
        Assert.Equal(first.Totals, second.Totals);
    }

    [Theory]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=101", "limit")]
    [InlineData("?cursor=not-a-cursor", "cursor")]
    public async Task A_page_size_outside_1_to_100_or_a_cursor_the_list_did_not_give_is_refused(string query, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync(Accounts + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task Deleting_an_account_archives_it_at_any_balance_so_it_leaves_the_list_and_its_totals_but_remains_readable()
    {
        var (browser, _) = await api.SignedInAsync();
        var kept = await AddAsync(browser, Current("Everyday", "1000"));
        var archived = await AddAsync(browser, Current("Old account", "250"));

        var response = await browser.DeleteAsync($"{Accounts}/{archived.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listed = await PageAsync(browser);
        Assert.Equal([kept.Id], listed.Items.Select(account => account.Id));
        Assert.Equal([new TotalBody(null, "VND", "1000.00")], listed.Totals);

        var read = await GetAsync(browser, archived.Id);
        Assert.Equal((true, "250.00"), (read.IsArchived, read.Balance));

        var withArchived = await PageAsync(browser, "?includeArchived=true");
        Assert.Equal(["Everyday", "Old account"], withArchived.Items.Select(account => account.Name));
        Assert.Equal([new TotalBody(null, "VND", "1250.00")], withArchived.Totals);
    }

    [Fact]
    public async Task Archiving_an_account_twice_changes_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAsync(browser, Current("Old account"));
        await browser.DeleteAsync($"{Accounts}/{account.Id}");
        var archived = await GetAsync(browser, account.Id);

        var again = await browser.DeleteAsync($"{Accounts}/{account.Id}");

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(archived, await GetAsync(browser, account.Id));
    }

    [Fact]
    public async Task An_account_edited_while_it_is_being_archived_is_archived_all_the_same_and_keeps_the_edit()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAsync(browser, Current("Everyday"));
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Accounts.Account>(() => EditAsync(browser, account, Current("Daily spending")));

        var response = await browser.DeleteAsync($"{Accounts}/{account.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var read = await GetAsync(browser, account.Id);
        Assert.Equal((true, "Daily spending"), (read.IsArchived, read.Name));
    }

    [Fact]
    public async Task No_balance_or_available_credit_is_stored_only_the_opening_balance_and_the_credit_limit()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PfmDbContext>();

        var columns = await db.Database
            .SqlQuery<string>($"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'Accounts'")
            .ToListAsync();

        Assert.Equal(
            [
                "CreatedAt", "CreditLimit", "Currency", "Id", "InstitutionId", "IsArchived", "Last4", "Name", "OpeningBalance", "OpeningDate",
                "PaymentDueDay", "StatementDay", "Type", "UpdatedAt", "UserId",
            ],
            columns.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Another_users_accounts_are_never_visible_or_editable()
    {
        var (bob, _) = await api.SignedInAsync();
        var bobs = await AddAsync(bob, Current("Bob's money"));
        var (alice, _) = await api.SignedInAsync();
        var path = $"{Accounts}/{bobs.Id}";

        Assert.Empty((await PageAsync(alice, "?includeArchived=true")).Items);
        Assert.Empty((await PageAsync(alice, "?includeArchived=true")).Totals);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync(path)).StatusCode);
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
        var response = await api.CreateBrowser().GetAsync(Accounts);

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

    private static async Task<AccountBody> EditAsync(Browser browser, AccountBody from, object account)
    {
        var response = await browser.PutAsync($"{Accounts}/{from.Id}", account, ifMatch: Quoted(from.RowVersion));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private static async Task<AccountBody[]> ListAsync(Browser browser, string query = "") => (await PageAsync(browser, query)).Items;

    private static async Task<AccountList> PageAsync(Browser browser, string query = "")
    {
        var response = await browser.GetAsync(Accounts + query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountList>())!;
    }

    private sealed record Identified(Guid Id);

    private sealed record AccountList(AccountBody[] Items, string? NextCursor, TotalBody[] Totals);

    private sealed record TotalBody(Guid? InstitutionId, string Currency, string Balance);

    private sealed record AccountBody(
        Guid Id, Guid? InstitutionId, string Name, string Type, string Currency, string OpeningBalance, string? AmountOwed, string OpeningDate,
        string Balance, bool IsArchived, string RowVersion, string? Last4 = null, string? CreditLimit = null, int? StatementDay = null,
        int? PaymentDueDay = null, string? AvailableCredit = null);
}
