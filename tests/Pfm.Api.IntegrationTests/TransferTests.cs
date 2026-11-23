using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransferTests(PfmApi api)
{
    private const string Transfers = "/api/v1/transfers";
    private const string Transactions = "/api/v1/transactions";

    [Fact]
    public async Task A_transfer_records_a_transfer_out_leg_on_the_source_account_and_a_transfer_in_leg_on_the_destination()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings", "VND", "1000000");

        var response = await browser.PostAsync(Transfers, new { fromAccountId = everyday, toAccountId = savings, amount = "2000000", date = "2026-11-02", note = "  for Tet " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = (await response.Content.ReadFromJsonAsync<TransferBody>())!;
        Assert.Equal(
            new TransferBody(
                transfer.Id, everyday, savings, "2000000.00", "VND", "2000000.00", "VND", "2026-11-02", "for Tet", transfer.OutTransactionId,
                transfer.InTransactionId, transfer.RowVersion),
            transfer);
        Assert.Equal($"{Transfers}/{transfer.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(Quoted(transfer.RowVersion), response.Headers.ETag?.Tag);
        Assert.Equal(transfer, await GetAsync(browser, transfer.Id));

        var legs = await LegsAsync(browser);
        Assert.Equal(
            [
                new LegBody(transfer.OutTransactionId, "transferOut", everyday, "2000000.00", "VND", "2026-11-02", null, null, "for Tet", transfer.Id),
                new LegBody(transfer.InTransactionId, "transferIn", savings, "2000000.00", "VND", "2026-11-02", null, null, "for Tet", transfer.Id),
            ],
            legs.OrderByDescending(leg => leg.Type));
    }

    [Fact]
    public async Task Both_account_balances_change_and_together_they_hold_what_they_held_before()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings", "VND", "1000000");

        await TransferAsync(browser, everyday, savings, "2000000");

        Assert.Equal("3000000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("3000000.00", await BalanceAsync(browser, savings));
        var accounts = (await browser.Http.GetFromJsonAsync<AccountList>("/api/v1/accounts"))!;
        Assert.Equal(["6000000.00"], accounts.Totals.Select(total => total.Balance));
    }

    [Fact]
    public async Task Paying_a_credit_card_from_a_current_account_lowers_the_amount_owed()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var card = await browser.PostAsync("/api/v1/accounts", new { name = "Visa", type = "creditCard", currency = "VND", amountOwed = "1500000", openingDate = "2026-01-01" });
        var visa = (await card.Content.ReadFromJsonAsync<Identified>())!.Id;

        await TransferAsync(browser, everyday, visa, "1000000");

        Assert.Equal("4000000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("-500000.00", await BalanceAsync(browser, visa));
    }

    [Fact]
    public async Task Between_accounts_in_different_currencies_each_leg_keeps_the_amount_the_user_entered_for_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var dollars = await AddAccountAsync(browser, "Dollars", "USD", "10");

        var response = await browser.PostAsync(
            Transfers, new { fromAccountId = everyday, toAccountId = dollars, amount = "2550000", toAmount = "99.5", date = "2026-11-02" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = (await response.Content.ReadFromJsonAsync<TransferBody>())!;
        Assert.Equal(("2550000.00", "VND", "99.50", "USD"), (transfer.Amount, transfer.Currency, transfer.ToAmount, transfer.ToCurrency));
        Assert.Equal("2450000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("109.50", await BalanceAsync(browser, dollars));
        Assert.Equal(
            [("transferOut", "2550000.00", "VND"), ("transferIn", "99.50", "USD")],
            (await LegsAsync(browser)).OrderByDescending(leg => leg.Type).Select(leg => (leg.Type, leg.Amount, leg.Currency)));
    }

    [Fact]
    public async Task Between_currencies_the_amount_that_arrives_must_be_entered_because_no_rate_is_looked_up()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var dollars = await AddAccountAsync(browser, "Dollars", "USD");

        var response = await browser.PostAsync(Transfers, new { fromAccountId = everyday, toAccountId = dollars, amount = "2550000", date = "2026-11-02" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["toAmount"], await response.ErrorFieldsAsync());
        Assert.Empty(await LegsAsync(browser));
    }

    [Fact]
    public async Task Between_accounts_in_one_currency_the_amount_that_arrives_is_the_amount_that_left()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings");

        var same = await browser.PostAsync(Transfers, new { fromAccountId = everyday, toAccountId = savings, amount = "100", toAmount = "100.00", date = "2026-11-02" });
        var different = await browser.PostAsync(Transfers, new { fromAccountId = everyday, toAccountId = savings, amount = "100", toAmount = "90", date = "2026-11-02" });

        Assert.Equal(HttpStatusCode.Created, same.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, different.StatusCode);
        Assert.Equal(["toAmount"], await different.ErrorFieldsAsync());
        Assert.Equal("100.00", await BalanceAsync(browser, savings));
    }

    [Fact]
    public async Task A_transfer_needs_two_accounts_an_amount_and_a_date()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync(Transfers, new { note = "nothing else" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["amount", "date", "fromAccountId", "toAccountId"], await response.ErrorFieldsAsync());
    }

    [Theory]
    [InlineData("amount", "0")]
    [InlineData("amount", "-5")]
    [InlineData("amount", "abc")]
    [InlineData("amount", "1500.5")]
    [InlineData("toAmount", "0")]
    [InlineData("toAmount", "1.005")]
    [InlineData("toAmount", "1,5")]
    [InlineData("date", "02/11/2026")]
    [InlineData("date", "2999-01-01")]
    public async Task An_amount_that_is_not_above_zero_or_does_not_fit_its_accounts_currency_or_a_date_that_is_not_one_or_is_in_the_future_is_refused_by_name(
        string field, string value)
    {
        var (browser, _) = await api.SignedInAsync();
        var transfer = new Dictionary<string, object?>
        {
            ["fromAccountId"] = await AddAccountAsync(browser, "Everyday", "VND", "5000000"),
            ["toAccountId"] = await AddAccountAsync(browser, "Dollars", "USD"),
            ["amount"] = "2550000",
            ["toAmount"] = "100",
            ["date"] = "2026-11-02",
            [field] = value,
        };

        var response = await browser.PostAsync(Transfers, transfer);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await response.ErrorFieldsAsync());
        Assert.Empty(await LegsAsync(browser));
    }

    [Fact]
    public async Task A_transfer_is_between_two_different_accounts_of_the_users_own_that_are_not_archived()
    {
        var (bob, _) = await api.SignedInAsync();
        var bobsAccount = await AddAccountAsync(bob, "Bob's money");
        var (alice, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(alice, "Everyday", "VND", "1000");
        var archived = await AddAccountAsync(alice, "Old account");
        (await alice.DeleteAsync($"/api/v1/accounts/{archived}")).EnsureSuccessStatusCode();

        foreach (var other in new[] { bobsAccount, archived, Guid.CreateVersion7() })
        {
            var to = await alice.PostAsync(Transfers, new { fromAccountId = everyday, toAccountId = other, amount = "100", date = "2026-11-02" });
            var from = await alice.PostAsync(Transfers, new { fromAccountId = other, toAccountId = everyday, amount = "100", date = "2026-11-02" });

            Assert.Equal(HttpStatusCode.BadRequest, to.StatusCode);
            Assert.Equal(["toAccountId"], await to.ErrorFieldsAsync());
            Assert.Equal(["fromAccountId"], await from.ErrorFieldsAsync());
        }

        var toItself = await alice.PostAsync(Transfers, new { fromAccountId = everyday, toAccountId = everyday, amount = "100", date = "2026-11-02" });
        Assert.Equal(["toAccountId"], await toItself.ErrorFieldsAsync());
        Assert.Empty(await LegsAsync(alice));
        Assert.Empty(await LegsAsync(bob));
        Assert.Equal("1000.00", await BalanceAsync(alice, everyday));
    }

    [Fact]
    public async Task Editing_a_transfer_changes_both_legs_and_the_balances_of_every_account_it_was_or_is_now_between()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var savings = await AddAccountAsync(browser, "Savings", "VND", "1000000");
        var dollars = await AddAccountAsync(browser, "Dollars", "USD", "10");
        var transfer = await TransferAsync(browser, everyday, savings, "2000000");

        var response = await browser.PutAsync(
            $"{Transfers}/{transfer.Id}",
            new { fromAccountId = savings, toAccountId = dollars, amount = "510000", toAmount = "20", date = "2026-11-01", note = "holiday money" },
            ifMatch: Quoted(transfer.RowVersion));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<TransferBody>())!;
        Assert.Equal(
            new TransferBody(
                transfer.Id, savings, dollars, "510000.00", "VND", "20.00", "USD", "2026-11-01", "holiday money", transfer.OutTransactionId,
                transfer.InTransactionId, edited.RowVersion),
            edited);
        Assert.NotEqual(transfer.RowVersion, edited.RowVersion);
        Assert.Equal(Quoted(edited.RowVersion), response.Headers.ETag?.Tag);
        Assert.Equal(edited, await GetAsync(browser, transfer.Id));
        Assert.Equal(
            [
                new LegBody(transfer.OutTransactionId, "transferOut", savings, "510000.00", "VND", "2026-11-01", null, null, "holiday money", transfer.Id),
                new LegBody(transfer.InTransactionId, "transferIn", dollars, "20.00", "USD", "2026-11-01", null, null, "holiday money", transfer.Id),
            ],
            (await LegsAsync(browser)).OrderByDescending(leg => leg.Type));
        Assert.Equal("5000000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("490000.00", await BalanceAsync(browser, savings));
        Assert.Equal("30.00", await BalanceAsync(browser, dollars));
    }

    [Fact]
    public async Task An_edit_is_held_to_the_same_rules_as_a_new_transfer_and_one_that_breaks_them_changes_neither_leg()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var dollars = await AddAccountAsync(browser, "Dollars", "USD");
        var transfer = await TransferAsync(browser, everyday, dollars, "2550000", "100");
        var path = $"{Transfers}/{transfer.Id}";

        var noToAmount = await browser.PutAsync(path, new { fromAccountId = everyday, toAccountId = dollars, amount = "3000000", date = "2026-11-02" }, Quoted(transfer.RowVersion));
        var toItself = await browser.PutAsync(path, new { fromAccountId = everyday, toAccountId = everyday, amount = "3000000", date = "2026-11-02" }, Quoted(transfer.RowVersion));
        var tooFine = await browser.PutAsync(
            path, new { fromAccountId = everyday, toAccountId = dollars, amount = "3000000", toAmount = "1.005", date = "2026-11-02" }, Quoted(transfer.RowVersion));

        Assert.Equal(["toAmount"], await noToAmount.ErrorFieldsAsync());
        Assert.Equal(["toAccountId"], await toItself.ErrorFieldsAsync());
        Assert.Equal(["toAmount"], await tooFine.ErrorFieldsAsync());
        Assert.Equal(transfer, await GetAsync(browser, transfer.Id));
        Assert.Equal("2450000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("100.00", await BalanceAsync(browser, dollars));
    }

    [Fact]
    public async Task A_transfer_keeps_an_account_through_an_edit_after_it_is_archived_but_cannot_move_to_an_archived_one()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        var old = await AddAccountAsync(browser, "Old account");
        var transfer = await TransferAsync(browser, everyday, savings, "2000");
        (await browser.DeleteAsync($"/api/v1/accounts/{savings}")).EnsureSuccessStatusCode();
        (await browser.DeleteAsync($"/api/v1/accounts/{old}")).EnsureSuccessStatusCode();
        var path = $"{Transfers}/{transfer.Id}";

        var kept = await browser.PutAsync(path, new { fromAccountId = everyday, toAccountId = savings, amount = "2500", date = "2026-11-02" }, Quoted(transfer.RowVersion));
        var edited = (await kept.Content.ReadFromJsonAsync<TransferBody>())!;
        var moved = await browser.PutAsync(path, new { fromAccountId = everyday, toAccountId = old, amount = "2500", date = "2026-11-02" }, Quoted(edited.RowVersion));

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(["toAccountId"], await moved.ErrorFieldsAsync());
        Assert.Equal("2500.00", await BalanceAsync(browser, savings));
    }

    [Fact]
    public async Task An_edit_made_from_a_stale_row_version_is_refused_with_412_and_one_without_a_row_version_with_428_and_neither_changes_a_leg()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        var transfer = await TransferAsync(browser, everyday, savings, "2000");
        var path = $"{Transfers}/{transfer.Id}";
        var first = await browser.PutAsync(path, new { fromAccountId = everyday, toAccountId = savings, amount = "2500", date = "2026-11-02" }, Quoted(transfer.RowVersion));

        var stale = await browser.PutAsync(path, new { fromAccountId = everyday, toAccountId = savings, amount = "1", date = "2026-11-02" }, Quoted(transfer.RowVersion));
        var none = await browser.PutAsync(path, new { fromAccountId = everyday, toAccountId = savings, amount = "1", date = "2026-11-02" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionRequired, none.StatusCode);
        Assert.Equal("2500.00", await BalanceAsync(browser, everyday));
        Assert.Equal("2500.00", await BalanceAsync(browser, savings));
    }

    [Fact]
    public async Task Of_two_edits_made_from_the_same_row_version_at_once_only_one_is_saved_and_the_legs_still_agree()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        var transfer = await TransferAsync(browser, everyday, savings, "2000");
        var path = $"{Transfers}/{transfer.Id}";
        var other = HttpStatusCode.Unused;
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Transactions.Transaction>(async () =>
            other = (await browser.PutAsync(path, new { fromAccountId = everyday, toAccountId = savings, amount = "3000", date = "2026-11-02" }, Quoted(transfer.RowVersion))).StatusCode);

        var response = await browser.PutAsync(path, new { fromAccountId = everyday, toAccountId = savings, amount = "1000", date = "2026-11-02" }, Quoted(transfer.RowVersion));

        Assert.Equal(HttpStatusCode.OK, other);
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(["3000.00", "3000.00"], (await LegsAsync(browser)).Select(leg => leg.Amount));
        Assert.Equal("2000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("3000.00", await BalanceAsync(browser, savings));
    }

    [Fact]
    public async Task Deleting_a_transfer_deletes_both_legs_and_both_balances_are_as_before_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000000");
        var dollars = await AddAccountAsync(browser, "Dollars", "USD", "10");
        var kept = await TransferAsync(browser, everyday, dollars, "255000", "10");
        var transfer = await TransferAsync(browser, everyday, dollars, "2550000", "100");
        var path = $"{Transfers}/{transfer.Id}";

        var response = await browser.DeleteAsync(path);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.All(await LegsAsync(browser), leg => Assert.Equal(kept.Id, leg.TransferId));
        Assert.Equal(2, (await LegsAsync(browser)).Length);
        Assert.Equal("4745000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("20.00", await BalanceAsync(browser, dollars));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Transactions}/{transfer.InTransactionId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await browser.PutAsync(path, new { fromAccountId = everyday, toAccountId = dollars, amount = "1", toAmount = "1", date = "2026-11-02" }, Quoted(transfer.RowVersion))).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deleting_either_leg_as_a_transaction_deletes_the_other_leg_with_it(bool theOutLeg)
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        var transfer = await TransferAsync(browser, everyday, savings, "2000");

        var response = await browser.DeleteAsync($"{Transactions}/{(theOutLeg ? transfer.OutTransactionId : transfer.InTransactionId)}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await LegsAsync(browser));
        Assert.Equal("5000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("0.00", await BalanceAsync(browser, savings));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"{Transfers}/{transfer.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_transfer_deleted_by_several_requests_at_once_through_either_leg_is_deleted_once_and_none_of_them_fails()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        var transfer = await TransferAsync(browser, everyday, savings, "2000");
        string[] paths = [$"{Transfers}/{transfer.Id}", $"{Transactions}/{transfer.OutTransactionId}", $"{Transactions}/{transfer.InTransactionId}"];

        var answers = await Task.WhenAll(paths.Concat(paths).Select(browser.DeleteAsync));

        Assert.Contains(answers, answer => answer.StatusCode == HttpStatusCode.NoContent);
        Assert.All(answers, answer => Assert.Contains(answer.StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound }));
        Assert.Empty(await LegsAsync(browser));
        Assert.Equal("5000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("0.00", await BalanceAsync(browser, savings));
    }

    [Fact]
    public async Task A_leg_is_read_as_a_transaction_but_is_edited_only_through_its_transfer()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        var transfer = await TransferAsync(browser, everyday, savings, "2000");
        var path = $"{Transactions}/{transfer.OutTransactionId}";
        var leg = await browser.GetAsync(path);
        var read = (await leg.Content.ReadFromJsonAsync<LegBody>())!;

        var response = await browser.PutAsync(
            path, new { type = "expense", accountId = everyday, amount = "1", date = "2026-11-02", categoryId = await CategoryAsync(browser, "Groceries") },
            ifMatch: leg.Headers.ETag!.Tag);

        Assert.Equal(("transferOut", transfer.Id, everyday), (read.Type, read.TransferId, read.AccountId));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["type"], await response.ErrorFieldsAsync());
        Assert.Equal(transfer, await GetAsync(browser, transfer.Id));
        Assert.Equal("3000.00", await BalanceAsync(browser, everyday));
    }

    [Theory]
    [InlineData("transferOut")]
    [InlineData("transferIn")]
    public async Task A_leg_cannot_be_recorded_on_its_own_as_a_transaction(string type)
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");

        var response = await browser.PostAsync(
            Transactions, new { type, accountId = everyday, amount = "1", date = "2026-11-02", categoryId = await CategoryAsync(browser, "Groceries") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["type"], await response.ErrorFieldsAsync());
        Assert.Equal("5000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task A_category_is_still_archived_and_changes_kind_as_before_when_the_user_has_transfers()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        await TransferAsync(browser, everyday, savings, "2000");
        var shopping = await CategoryAsync(browser, "Shopping");

        var response = await browser.PutAsync($"/api/v1/categories/{shopping}", new { name = "Shopping", kind = "income", icon = "shopping-bag", color = "#ea580c" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_request_sent_again_with_the_same_idempotency_key_records_nothing_and_answers_the_first_transfer()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        var key = Guid.NewGuid().ToString();

        var first = await browser.PostAsync(Transfers, new { fromAccountId = everyday, toAccountId = savings, amount = "2000", date = "2026-11-02", note = "rent" }, key);
        var retry = await browser.PostAsync(Transfers, new { fromAccountId = everyday, toAccountId = savings, amount = "2000.00", toAmount = "2000", date = "2026-11-02", note = " rent " }, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(await first.Content.ReadFromJsonAsync<TransferBody>(), await retry.Content.ReadFromJsonAsync<TransferBody>());
        Assert.Equal(first.Headers.Location, retry.Headers.Location);
        Assert.Equal(2, (await LegsAsync(browser)).Length);
        Assert.Equal("3000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task An_idempotency_key_sent_again_with_a_different_transfer_or_a_transaction_is_refused_with_422_and_records_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        var groceries = await CategoryAsync(browser, "Groceries");
        var key = Guid.NewGuid().ToString();
        var expenseKey = Guid.NewGuid().ToString();
        (await browser.PostAsync(Transfers, new { fromAccountId = everyday, toAccountId = savings, amount = "2000", date = "2026-11-02" }, key)).EnsureSuccessStatusCode();
        (await browser.PostAsync(Transactions, new { type = "expense", accountId = everyday, amount = "500", date = "2026-11-02", categoryId = groceries }, expenseKey))
            .EnsureSuccessStatusCode();

        var corrected = await browser.PostAsync(Transfers, new { fromAccountId = everyday, toAccountId = savings, amount = "200", date = "2026-11-02" }, key);
        var asExpense = await browser.PostAsync(Transactions, new { type = "expense", accountId = everyday, amount = "2000", date = "2026-11-02", categoryId = groceries }, key);
        var asTransfer = await browser.PostAsync(Transfers, new { fromAccountId = everyday, toAccountId = savings, amount = "500", date = "2026-11-02" }, expenseKey);

        Assert.All(new[] { corrected, asExpense, asTransfer }, refused => Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode));
        Assert.Equal("urn:pfm:problem:idempotency-key-reused", await corrected.ProblemTypeAsync());
        Assert.Equal("2500.00", await BalanceAsync(browser, everyday));
        Assert.Equal("2000.00", await BalanceAsync(browser, savings));
    }

    [Fact]
    public async Task The_key_of_a_transfer_deleted_since_stays_used_a_retry_answers_it_and_does_not_record_it_again()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        var key = Guid.NewGuid().ToString();
        var body = new { fromAccountId = everyday, toAccountId = savings, amount = "2000", date = "2026-11-02" };
        var first = (await (await browser.PostAsync(Transfers, body, key)).Content.ReadFromJsonAsync<TransferBody>())!;
        (await browser.DeleteAsync($"{Transfers}/{first.Id}")).EnsureSuccessStatusCode();

        var retry = await browser.PostAsync(Transfers, body, key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(first.Id, (await retry.Content.ReadFromJsonAsync<TransferBody>())!.Id);
        Assert.Empty(await LegsAsync(browser));
        Assert.Equal("5000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task A_request_sent_several_times_at_once_with_the_same_idempotency_key_records_one_transfer()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        var key = Guid.NewGuid().ToString();
        var body = new { fromAccountId = everyday, toAccountId = savings, amount = "2000", date = "2026-11-02" };

        var answers = await Task.WhenAll(Enumerable.Range(1, 6).Select(_ => browser.PostAsync(Transfers, body, key)));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.Created, answer.StatusCode));
        Assert.Single(answers.Select(answer => answer.Headers.Location).Distinct());
        Assert.Equal(2, (await LegsAsync(browser)).Length);
        Assert.Equal("3000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task A_transfer_does_not_change_the_account_quick_add_starts_a_new_transaction_from()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        var cash = await AddAccountAsync(browser, "Cash");
        (await browser.PostAsync(Transactions, new { type = "expense", accountId = cash, amount = "100", date = "2026-11-02", categoryId = await CategoryAsync(browser, "Groceries") }))
            .EnsureSuccessStatusCode();

        await TransferAsync(browser, everyday, savings, "2000");

        var quickAdd = (await browser.Http.GetFromJsonAsync<QuickAddBody>($"{Transactions}/quick-add"))!;
        Assert.Equal(cash, quickAdd.LastAccountId);
    }

    [Theory]
    [InlineData("transferOut")]
    [InlineData("transferIn")]
    public async Task A_recurring_rule_is_for_income_or_an_expense_and_never_posts_a_leg_of_a_transfer(string type)
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");

        var response = await browser.PostAsync("/api/v1/recurring-rules", new
        {
            type, accountId = everyday, amount = "1000", categoryId = await CategoryAsync(browser, "Groceries"), frequency = "monthly", startDate = "2026-11-01",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["type"], await response.ErrorFieldsAsync());
        Assert.Empty(await LegsAsync(browser));
        Assert.Equal("5000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task An_accounts_currency_is_fixed_once_a_transfer_goes_through_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "5000");
        var savings = await AddAccountAsync(browser, "Savings");
        await TransferAsync(browser, everyday, savings, "2000");
        var account = (await browser.Http.GetFromJsonAsync<AccountBody>($"/api/v1/accounts/{savings}"))!;

        var response = await browser.PutAsync(
            $"/api/v1/accounts/{savings}", new { name = "Savings", type = "current", currency = "USD", openingBalance = "0", openingDate = "2026-01-01" },
            ifMatch: Quoted(account.RowVersion));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["currency"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task Another_users_transfers_are_never_visible_or_changeable()
    {
        var (bob, _) = await api.SignedInAsync();
        var bobsEveryday = await AddAccountAsync(bob, "Everyday", "VND", "5000");
        var bobsSavings = await AddAccountAsync(bob, "Savings");
        var bobs = await TransferAsync(bob, bobsEveryday, bobsSavings, "2000");
        var (alice, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(alice, "Everyday");
        var savings = await AddAccountAsync(alice, "Savings");
        var path = $"{Transfers}/{bobs.Id}";

        Assert.Empty(await LegsAsync(alice));
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"{Transactions}/{bobs.OutTransactionId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await alice.PutAsync(path, new { fromAccountId = everyday, toAccountId = savings, amount = "1", date = "2026-11-02" }, Quoted(bobs.RowVersion))).StatusCode);
        Assert.Equal(bobs, await GetAsync(bob, bobs.Id));
        Assert.Equal("3000.00", await BalanceAsync(bob, bobsEveryday));
    }

    [Fact]
    public async Task A_transfer_that_does_not_exist_is_not_found_and_a_visitor_who_is_not_signed_in_sees_none()
    {
        var (browser, _) = await api.SignedInAsync();

        var missing = await browser.GetAsync($"{Transfers}/{Guid.CreateVersion7()}");
        var visitor = await api.CreateBrowser().GetAsync($"{Transfers}/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, visitor.StatusCode);
    }

    private static string Quoted(string rowVersion) => $"\"{rowVersion}\"";

    /// <summary>A transfer dated in the past; between currencies when <paramref name="toAmount"/> is given.</summary>
    private static async Task<TransferBody> TransferAsync(Browser browser, Guid from, Guid to, string amount, string? toAmount = null)
    {
        var response = await browser.PostAsync(Transfers, new { fromAccountId = from, toAccountId = to, amount, toAmount, date = "2026-11-02" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransferBody>())!;
    }

    private static async Task<TransferBody> GetAsync(Browser browser, Guid id)
    {
        var response = await browser.GetAsync($"{Transfers}/{id}");
        response.EnsureSuccessStatusCode();
        var transfer = (await response.Content.ReadFromJsonAsync<TransferBody>())!;
        Assert.Equal(Quoted(transfer.RowVersion), response.Headers.ETag?.Tag);
        return transfer;
    }

    /// <summary>The user's transaction list, which here holds transfer legs only.</summary>
    private static async Task<LegBody[]> LegsAsync(Browser browser) =>
        (await browser.Http.GetFromJsonAsync<LegList>(Transactions))!.Items;

    private sealed record Identified(Guid Id);

    private sealed record QuickAddBody(Guid? LastAccountId);

    private sealed record AccountBody(string RowVersion);

    private sealed record AccountList(TotalBody[] Totals);

    private sealed record TotalBody(string Balance);

    private sealed record LegList(LegBody[] Items);

    /// <summary>A transaction as the list answers it, without its row version.</summary>
    private sealed record LegBody(
        Guid Id, string Type, Guid AccountId, string Amount, string Currency, string Date, Guid? CategoryId, string? Payee, string? Note, Guid? TransferId);

    private sealed record TransferBody(
        Guid Id, Guid FromAccountId, Guid ToAccountId, string Amount, string Currency, string ToAmount, string ToCurrency, string Date, string? Note,
        Guid OutTransactionId, Guid InTransactionId, string RowVersion);
}
