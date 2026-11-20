using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransactionTests(PfmApi api)
{
    private const string Transactions = "/api/v1/transactions";

    [Fact]
    public async Task A_user_can_record_an_expense_in_the_currency_of_its_account_and_read_it_back()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var groceries = await CategoryAsync(browser, "Groceries");

        var response = await browser.PostAsync(Transactions, new
        {
            type = "expense", accountId = everyday, amount = "125000", date = "2026-11-02", categoryId = groceries, payee = "  Co.opmart ", note = " weekly shop ",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
        Assert.Equal(
            new TransactionBody(created.Id, "expense", everyday, "125000.00", "VND", "2026-11-02", groceries, "Co.opmart", "weekly shop", created.RowVersion),
            created);
        Assert.Equal($"{Transactions}/{created.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(Quoted(created.RowVersion), response.Headers.ETag?.Tag);
        Assert.Equal(created, await GetAsync(browser, created.Id));
    }

    [Fact]
    public async Task The_account_balance_is_its_opening_balance_plus_income_minus_expenses()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var untouched = await AddAccountAsync(browser, "Rainy day", "VND", "500");

        await AddAsync(browser, "income", everyday, "15000000");
        await AddAsync(browser, "expense", everyday, "125000");
        await AddAsync(browser, "expense", everyday, "2500000");

        Assert.Equal("13375000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("500.00", await BalanceAsync(browser, untouched));
        var accounts = (await browser.Http.GetFromJsonAsync<AccountList>("/api/v1/accounts"))!;
        Assert.Equal(["13375000.00", "500.00"], accounts.Items.Select(account => account.Balance));
        Assert.Equal(["13375500.00"], accounts.Totals.Select(total => total.Balance));
    }

    [Fact]
    public async Task Expenses_take_a_credit_card_further_below_zero()
    {
        var (browser, _) = await api.SignedInAsync();
        var response = await browser.PostAsync("/api/v1/accounts", new { name = "Visa", type = "creditCard", currency = "USD", amountOwed = "100", openingDate = "2026-11-01" });
        var visa = (await response.Content.ReadFromJsonAsync<Identified>())!.Id;

        var expense = await AddAsync(browser, "expense", visa, "39.5");

        Assert.Equal(("39.50", "USD"), (expense.Amount, expense.Currency));
        Assert.Equal("-139.50", await BalanceAsync(browser, visa));
    }

    [Fact]
    public async Task A_transaction_needs_a_type_an_account_an_amount_a_date_and_a_category()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync(Transactions, new { payee = "Co.opmart" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["accountId", "amount", "categoryId", "date", "type"], await response.ErrorFieldsAsync());
    }

    [Theory]
    [InlineData("VND", "amount", "0")]
    [InlineData("VND", "amount", "-5")]
    [InlineData("VND", "amount", "abc")]
    [InlineData("VND", "amount", "1,500")]
    [InlineData("VND", "amount", "1e3")]
    [InlineData("VND", "amount", "1500.5")]
    [InlineData("USD", "amount", "1.005")]
    [InlineData("USD", "amount", "1000000000000000")]
    [InlineData("USD", "amount", null)]
    [InlineData("USD", "type", "transfer")]
    [InlineData("USD", "type", "1")]
    [InlineData("USD", "date", "02/11/2026")]
    [InlineData("USD", "date", "2026-02-30")]
    public async Task An_amount_that_is_not_above_zero_or_does_not_fit_the_accounts_currency_or_a_type_or_date_the_app_does_not_know_is_refused_by_name(
        string currency, string field, string? value)
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Everyday", currency);
        var transaction = await ANewAsync(browser, "expense", account, "10");
        transaction[field] = value;

        var response = await browser.PostAsync(Transactions, transaction);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await response.ErrorFieldsAsync());
        Assert.Empty((await PageAsync(browser)).Items);
    }

    [Fact]
    public async Task A_payee_holds_at_most_200_characters_and_a_note_1000()
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await ANewAsync(browser, "expense", await AddAccountAsync(browser, "Everyday"), "10");
        transaction["payee"] = new string('p', 201);
        transaction["note"] = new string('n', 1001);

        var response = await browser.PostAsync(Transactions, transaction);

        Assert.Equal(["note", "payee"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_transaction_cannot_be_dated_in_the_future()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var dayAfterTomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd");

        var response = await browser.PostAsync(Transactions, await ANewAsync(browser, "expense", everyday, "10", dayAfterTomorrow));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["date"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task Today_is_read_in_the_users_time_zone()
    {
        // Kiritimati (UTC+14) and Pago Pago (UTC-11) are 25 hours apart: today in the first is always in the future in the second.
        var todayInKiritimati = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(14)).ToString("yyyy-MM-dd");
        var (ahead, _) = await api.SignedInAsync();
        (await ahead.PatchAsync("/api/v1/me", new { timeZone = "Pacific/Kiritimati" })).EnsureSuccessStatusCode();
        var (behind, _) = await api.SignedInAsync();
        (await behind.PatchAsync("/api/v1/me", new { timeZone = "Pacific/Pago_Pago" })).EnsureSuccessStatusCode();

        var today = await ahead.PostAsync(Transactions, await ANewAsync(ahead, "expense", await AddAccountAsync(ahead, "Everyday"), "10", todayInKiritimati));
        var tomorrow = await behind.PostAsync(Transactions, await ANewAsync(behind, "expense", await AddAccountAsync(behind, "Everyday"), "10", todayInKiritimati));

        Assert.Equal(HttpStatusCode.Created, today.StatusCode);
        Assert.Equal(["date"], await tomorrow.ErrorFieldsAsync());
    }

    [Theory]
    [InlineData("income", "Groceries")]
    [InlineData("expense", "Salary")]
    public async Task An_income_transaction_takes_only_an_income_category_and_an_expense_only_an_expense_category(string type, string category)
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await ANewAsync(browser, type, await AddAccountAsync(browser, "Everyday"), "10");
        transaction["categoryId"] = await CategoryAsync(browser, category);

        var response = await browser.PostAsync(Transactions, transaction);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["categoryId"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_transaction_can_only_be_recorded_on_one_of_the_users_own_accounts_and_categories_that_are_not_archived()
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
            var response = await alice.PostAsync(Transactions, await ANewAsync(alice, "expense", accountId, "10"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(["accountId"], await response.ErrorFieldsAsync());
        }

        foreach (var categoryId in new[] { bobsGroceries, archivedCategory, Guid.CreateVersion7() })
        {
            var transaction = await ANewAsync(alice, "expense", everyday, "10");
            transaction["categoryId"] = categoryId;

            var response = await alice.PostAsync(Transactions, transaction);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(["categoryId"], await response.ErrorFieldsAsync());
        }

        Assert.Empty((await PageAsync(alice)).Items);
        Assert.Empty((await PageAsync(bob)).Items);
    }

    [Fact]
    public async Task Editing_a_transaction_changes_it_and_the_balances_of_the_accounts_it_was_on_and_moved_to_follow_at_once()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var dollars = await AddAccountAsync(browser, "Dollars", "USD", "100");
        var salary = await CategoryAsync(browser, "Salary");
        var transaction = await AddAsync(browser, "expense", everyday, "125000");

        var response = await browser.PutAsync(
            $"{Transactions}/{transaction.Id}",
            new { type = "income", accountId = dollars, amount = "99.95", date = "2026-10-31", categoryId = salary, payee = " Acme ", note = "bonus" },
            ifMatch: Quoted(transaction.RowVersion));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
        Assert.Equal(new TransactionBody(transaction.Id, "income", dollars, "99.95", "USD", "2026-10-31", salary, "Acme", "bonus", edited.RowVersion), edited);
        Assert.NotEqual(transaction.RowVersion, edited.RowVersion);
        Assert.Equal(Quoted(edited.RowVersion), response.Headers.ETag?.Tag);
        Assert.Equal(edited, await GetAsync(browser, transaction.Id));
        Assert.Equal("1000000.00", await BalanceAsync(browser, everyday));
        Assert.Equal("199.95", await BalanceAsync(browser, dollars));
    }

    [Fact]
    public async Task An_edit_made_from_a_stale_row_version_is_refused_with_412_and_changes_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var transaction = await AddAsync(browser, "expense", everyday, "100");
        var path = $"{Transactions}/{transaction.Id}";
        (await browser.PutAsync(path, await ANewAsync(browser, "expense", everyday, "200"), ifMatch: Quoted(transaction.RowVersion))).EnsureSuccessStatusCode();

        var stale = await browser.PutAsync(path, await ANewAsync(browser, "expense", everyday, "300"), ifMatch: Quoted(transaction.RowVersion));

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("application/problem+json", stale.Content.Headers.ContentType?.MediaType);
        Assert.Equal("200.00", (await GetAsync(browser, transaction.Id)).Amount);
        Assert.Equal("-200.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task Of_two_edits_made_from_the_same_row_version_at_once_only_one_is_saved()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var transaction = await AddAsync(browser, "expense", everyday, "100");
        var edit = await ANewAsync(browser, "expense", everyday, "200");

        var answers = await Task.WhenAll(Enumerable.Range(1, 6).Select(_ =>
            browser.PutAsync($"{Transactions}/{transaction.Id}", edit, ifMatch: Quoted(transaction.RowVersion))));

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5, answers.Count(answer => answer.StatusCode == HttpStatusCode.PreconditionFailed));
    }

    [Theory]
    [InlineData("\"not-a-version\"", HttpStatusCode.PreconditionFailed)]
    [InlineData("12", HttpStatusCode.PreconditionFailed)]
    [InlineData("*", HttpStatusCode.PreconditionFailed)]
    [InlineData(null, HttpStatusCode.PreconditionRequired)]
    public async Task An_If_Match_that_is_not_a_row_version_the_API_gave_matches_nothing_and_an_edit_without_one_is_refused_with_428(
        string? ifMatch, HttpStatusCode expected)
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var transaction = await AddAsync(browser, "expense", everyday, "100");

        var response = await browser.PutAsync($"{Transactions}/{transaction.Id}", await ANewAsync(browser, "expense", everyday, "200"), ifMatch);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("100.00", (await GetAsync(browser, transaction.Id)).Amount);
    }

    [Fact]
    public async Task An_edit_is_held_to_the_same_rules_as_a_new_transaction()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var transaction = await AddAsync(browser, "expense", everyday, "100");
        var path = $"{Transactions}/{transaction.Id}";
        var salary = await CategoryAsync(browser, "Salary");

        var fields = await browser.PutAsync(path, new { type = "refund", accountId = everyday, amount = "0", date = "tomorrow", categoryId = salary }, Quoted(transaction.RowVersion));
        var kind = await browser.PutAsync(path, new { type = "expense", accountId = everyday, amount = "100", date = "2026-11-02", categoryId = salary }, Quoted(transaction.RowVersion));
        var decimals = await browser.PutAsync(path, new { type = "expense", accountId = everyday, amount = "100.5", date = "2026-11-02", categoryId = transaction.CategoryId }, Quoted(transaction.RowVersion));
        var account = await browser.PutAsync(path, new { type = "expense", accountId = Guid.CreateVersion7(), amount = "100", date = "2026-11-02", categoryId = transaction.CategoryId }, Quoted(transaction.RowVersion));

        Assert.Equal(["amount", "date", "type"], await fields.ErrorFieldsAsync());
        Assert.Equal(["categoryId"], await kind.ErrorFieldsAsync());
        Assert.Equal(["amount"], await decimals.ErrorFieldsAsync());
        Assert.Equal(["accountId"], await account.ErrorFieldsAsync());
        Assert.Equal(transaction, await GetAsync(browser, transaction.Id));
    }

    [Fact]
    public async Task A_transaction_keeps_its_account_and_category_through_an_edit_after_they_are_archived_but_cannot_move_to_archived_ones()
    {
        var (browser, _) = await api.SignedInAsync();
        var oldAccount = await AddAccountAsync(browser, "Old account");
        var otherArchivedAccount = await AddAccountAsync(browser, "Older account");
        var coffee = await CategoryAsync(browser, "Coffee");
        var groceries = await CategoryAsync(browser, "Groceries");
        var created = await browser.PostAsync(Transactions, new { type = "expense", accountId = oldAccount, amount = "45000", date = "2026-11-02", categoryId = coffee });
        var transaction = (await created.Content.ReadFromJsonAsync<TransactionBody>())!;
        var path = $"{Transactions}/{transaction.Id}";
        foreach (var archived in new[] { $"/api/v1/accounts/{oldAccount}", $"/api/v1/accounts/{otherArchivedAccount}", $"/api/v1/categories/{coffee}", $"/api/v1/categories/{groceries}" })
        {
            (await browser.DeleteAsync(archived)).EnsureSuccessStatusCode();
        }

        var kept = await browser.PutAsync(path, new { type = "expense", accountId = oldAccount, amount = "50000", date = "2026-11-02", categoryId = coffee }, Quoted(transaction.RowVersion));
        var edited = (await kept.Content.ReadFromJsonAsync<TransactionBody>())!;
        var movedAccount = await browser.PutAsync(path, new { type = "expense", accountId = otherArchivedAccount, amount = "50000", date = "2026-11-02", categoryId = coffee }, Quoted(edited.RowVersion));
        var movedCategory = await browser.PutAsync(path, new { type = "expense", accountId = oldAccount, amount = "50000", date = "2026-11-02", categoryId = groceries }, Quoted(edited.RowVersion));

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(("50000.00", oldAccount, coffee), (edited.Amount, edited.AccountId, edited.CategoryId));
        Assert.Equal(["accountId"], await movedAccount.ErrorFieldsAsync());
        Assert.Equal(["categoryId"], await movedCategory.ErrorFieldsAsync());
    }

    [Fact]
    public async Task Deleting_a_transaction_takes_it_out_of_the_list_and_the_balance_at_once()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var kept = await AddAsync(browser, "expense", everyday, "100000");
        var deleted = await AddAsync(browser, "expense", everyday, "250000");
        var path = $"{Transactions}/{deleted.Id}";

        var response = await browser.DeleteAsync(path);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([kept.Id], (await PageAsync(browser)).Items.Select(transaction => transaction.Id));
        Assert.Equal("900000.00", await BalanceAsync(browser, everyday));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.DeleteAsync(path)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await browser.PutAsync(path, await ANewAsync(browser, "expense", everyday, "1"), ifMatch: Quoted(deleted.RowVersion))).StatusCode);
    }

    [Fact]
    public async Task A_transaction_deleted_by_several_requests_at_once_is_deleted_once_and_none_of_them_fails()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var transaction = await AddAsync(browser, "expense", everyday, "250");

        var answers = await Task.WhenAll(Enumerable.Range(1, 6).Select(_ => browser.DeleteAsync($"{Transactions}/{transaction.Id}")));

        Assert.Contains(answers, answer => answer.StatusCode == HttpStatusCode.NoContent);
        Assert.All(answers, answer => Assert.Contains(answer.StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound }));
        Assert.Equal("1000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task A_transaction_edited_while_it_is_being_deleted_is_deleted_all_the_same()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var transaction = await AddAsync(browser, "expense", everyday, "250");
        var path = $"{Transactions}/{transaction.Id}";
        var edited = HttpStatusCode.Unused;
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Transactions.Transaction>(async () =>
            edited = (await browser.PutAsync(path, await ANewAsync(browser, "expense", everyday, "300"), ifMatch: Quoted(transaction.RowVersion))).StatusCode);

        var response = await browser.DeleteAsync(path);

        Assert.Equal(HttpStatusCode.OK, edited);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await PageAsync(browser)).Items);
        Assert.Equal("1000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task The_list_is_newest_date_first_and_within_a_date_last_recorded_first()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var second = await AddAsync(browser, "expense", everyday, "2", "2026-11-02");
        var fifth = await AddAsync(browser, "income", everyday, "5", "2026-11-05");
        var first = await AddAsync(browser, "expense", everyday, "1", "2026-10-01");
        var alsoSecond = await AddAsync(browser, "expense", everyday, "22", "2026-11-02");

        var page = await PageAsync(browser);

        Assert.Equal([fifth, alsoSecond, second, first], page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task The_list_comes_in_pages_each_pointing_to_the_next()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        foreach (var amount in new[] { "1", "2", "3", "4", "5" })
        {
            await AddAsync(browser, "expense", everyday, amount, amount is "1" or "5" ? "2026-11-01" : "2026-11-02");
        }

        var first = await PageAsync(browser, "?limit=2");
        var second = await PageAsync(browser, $"?limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        var third = await PageAsync(browser, $"?limit=2&cursor={Uri.EscapeDataString(second.NextCursor!)}");

        Assert.Equal(["4.00", "3.00"], first.Items.Select(transaction => transaction.Amount));
        Assert.Equal(["2.00", "5.00"], second.Items.Select(transaction => transaction.Amount));
        Assert.Equal(["1.00"], third.Items.Select(transaction => transaction.Amount));
        Assert.Null(third.NextCursor);
    }

    [Theory]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=101", "limit")]
    [InlineData("?cursor=not-a-cursor", "cursor")]
    [InlineData("?cursor=bm90LWEtY3Vyc29y", "cursor")]
    public async Task A_page_size_outside_1_to_100_or_a_cursor_the_list_did_not_give_is_refused(string query, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync(Transactions + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_request_sent_again_with_the_same_idempotency_key_records_nothing_and_answers_the_first_transaction()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var expense = await ANewAsync(browser, "expense", everyday, "250");
        var key = Guid.NewGuid().ToString();

        var first = await browser.PostAsync(Transactions, expense, idempotencyKey: key);
        var retry = await browser.PostAsync(Transactions, expense, idempotencyKey: key);
        var another = await browser.PostAsync(Transactions, expense, idempotencyKey: Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var recorded = (await first.Content.ReadFromJsonAsync<TransactionBody>())!;
        Assert.Equal(recorded, await retry.Content.ReadFromJsonAsync<TransactionBody>());
        Assert.Equal(first.Headers.Location, retry.Headers.Location);
        Assert.NotEqual(recorded.Id, (await another.Content.ReadFromJsonAsync<TransactionBody>())!.Id);
        Assert.Equal(2, (await PageAsync(browser)).Items.Length);
        Assert.Equal("500.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task An_idempotency_key_sent_again_with_a_different_transaction_is_refused_with_422_and_records_nothing()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var key = Guid.NewGuid().ToString();
        var first = await browser.PostAsync(Transactions, await ANewAsync(browser, "expense", everyday, "250"), idempotencyKey: key);

        var corrected = await browser.PostAsync(Transactions, await ANewAsync(browser, "expense", everyday, "205"), idempotencyKey: key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, corrected.StatusCode);
        Assert.Equal("urn:pfm:problem:idempotency-key-reused", await corrected.ProblemTypeAsync());
        Assert.Equal(["250.00"], (await PageAsync(browser)).Items.Select(transaction => transaction.Amount));
        Assert.Equal("750.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task The_same_transaction_written_another_way_is_still_the_same_request()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var expense = await ANewAsync(browser, "expense", everyday, "250");
        var key = Guid.NewGuid().ToString();
        var first = await browser.PostAsync(Transactions, new Dictionary<string, object?>(expense) { ["payee"] = "Co.opmart" }, idempotencyKey: key);

        var retry = await browser.PostAsync(
            Transactions, new Dictionary<string, object?>(expense) { ["amount"] = "250.00", ["payee"] = " Co.opmart ", ["note"] = " " }, idempotencyKey: key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(await first.Content.ReadFromJsonAsync<TransactionBody>(), await retry.Content.ReadFromJsonAsync<TransactionBody>());
    }

    [Fact]
    public async Task A_retry_is_judged_by_what_the_first_request_sent_so_it_answers_a_transaction_edited_since_as_it_is_now()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var expense = await ANewAsync(browser, "expense", everyday, "250");
        var key = Guid.NewGuid().ToString();
        var recorded = (await (await browser.PostAsync(Transactions, expense, idempotencyKey: key)).Content.ReadFromJsonAsync<TransactionBody>())!;
        (await browser.PutAsync(
            $"{Transactions}/{recorded.Id}", await ANewAsync(browser, "expense", everyday, "300"), ifMatch: Quoted(recorded.RowVersion))).EnsureSuccessStatusCode();

        var retry = await browser.PostAsync(Transactions, expense, idempotencyKey: key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(await GetAsync(browser, recorded.Id), await retry.Content.ReadFromJsonAsync<TransactionBody>());
        Assert.Single((await PageAsync(browser)).Items);
    }

    [Fact]
    public async Task The_key_of_a_transaction_deleted_since_stays_used_a_retry_answers_it_and_does_not_record_it_again()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var expense = await ANewAsync(browser, "expense", everyday, "250");
        var key = Guid.NewGuid().ToString();
        var recorded = (await (await browser.PostAsync(Transactions, expense, idempotencyKey: key)).Content.ReadFromJsonAsync<TransactionBody>())!;
        (await browser.DeleteAsync($"{Transactions}/{recorded.Id}")).EnsureSuccessStatusCode();

        var retry = await browser.PostAsync(Transactions, expense, idempotencyKey: key);
        var another = await browser.PostAsync(Transactions, await ANewAsync(browser, "expense", everyday, "205"), idempotencyKey: key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(recorded.Id, (await retry.Content.ReadFromJsonAsync<TransactionBody>())!.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, another.StatusCode);
        Assert.Empty((await PageAsync(browser)).Items);
        Assert.Equal("1000.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task A_request_sent_several_times_at_once_with_the_same_idempotency_key_records_one_transaction()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000");
        var expense = await ANewAsync(browser, "expense", everyday, "250");
        var key = Guid.NewGuid().ToString();

        var answers = await Task.WhenAll(Enumerable.Range(1, 6).Select(_ => browser.PostAsync(Transactions, expense, idempotencyKey: key)));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.Created, answer.StatusCode));
        var recorded = await Task.WhenAll(answers.Select(async answer => (await answer.Content.ReadFromJsonAsync<TransactionBody>())!));
        Assert.Single(recorded.Distinct());
        Assert.Single((await PageAsync(browser)).Items);
        Assert.Equal("750.00", await BalanceAsync(browser, everyday));
    }

    [Fact]
    public async Task An_idempotency_key_is_each_users_own()
    {
        var key = Guid.NewGuid().ToString();
        var (bob, _) = await api.SignedInAsync();
        var bobs = await bob.PostAsync(Transactions, await ANewAsync(bob, "expense", await AddAccountAsync(bob, "Bob's money"), "10"), idempotencyKey: key);
        var (alice, _) = await api.SignedInAsync();

        var alices = await alice.PostAsync(Transactions, await ANewAsync(alice, "expense", await AddAccountAsync(alice, "Everyday"), "20"), idempotencyKey: key);

        Assert.Equal(HttpStatusCode.Created, alices.StatusCode);
        Assert.Equal("20.00", (await alices.Content.ReadFromJsonAsync<TransactionBody>())!.Amount);
        Assert.Equal("10.00", (await bobs.Content.ReadFromJsonAsync<TransactionBody>())!.Amount);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("201")]
    public async Task An_idempotency_key_is_1_to_200_characters(string key)
    {
        var (browser, _) = await api.SignedInAsync();
        var expense = await ANewAsync(browser, "expense", await AddAccountAsync(browser, "Everyday"), "250");

        var response = await browser.PostAsync(Transactions, expense, idempotencyKey: key == "201" ? new string('k', 201) : key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["idempotencyKey"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task An_accounts_currency_is_fixed_once_it_has_transactions_even_deleted_ones_which_can_be_recovered()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var transaction = await AddAsync(browser, "expense", everyday, "100");

        var locked = await ChangeCurrencyAsync(browser, everyday, "USD");
        (await browser.DeleteAsync($"{Transactions}/{transaction.Id}")).EnsureSuccessStatusCode();
        var stillLocked = await ChangeCurrencyAsync(browser, everyday, "USD");
        var renamed = await ChangeCurrencyAsync(browser, everyday, "VND");

        Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
        Assert.Equal(["currency"], await locked.ErrorFieldsAsync());
        Assert.Equal(["currency"], await stillLocked.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
    }

    [Fact]
    public async Task An_accounts_currency_cannot_change_while_its_first_transaction_is_being_recorded()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        TransactionBody? recorded = null;
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Accounts.Account>(async () => recorded = await AddAsync(browser, "expense", everyday, "100"));

        var changed = await ChangeCurrencyAsync(browser, everyday, "USD");

        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        Assert.Equal(["currency"], await changed.ErrorFieldsAsync());
        Assert.Equal("VND", (await GetAsync(browser, recorded!.Id)).Currency);
        Assert.Equal("VND", await CurrencyAsync(browser, everyday));
    }

    [Fact]
    public async Task A_transaction_recorded_while_its_accounts_currency_changes_takes_the_new_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var changed = HttpStatusCode.Unused;
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Transactions.Transaction>(async () => changed = (await ChangeCurrencyAsync(browser, everyday, "USD")).StatusCode);

        var recorded = await AddAsync(browser, "expense", everyday, "100");

        Assert.Equal(HttpStatusCode.OK, changed);
        Assert.Equal("USD", recorded.Currency);
        Assert.Equal("USD", (await GetAsync(browser, recorded.Id)).Currency);
        Assert.Equal("USD", await CurrencyAsync(browser, everyday));
    }

    [Fact]
    public async Task A_transaction_moved_to_an_account_whose_currency_changes_meanwhile_takes_the_new_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        var transaction = await AddAsync(browser, "expense", everyday, "100");
        var changed = HttpStatusCode.Unused;
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Transactions.Transaction>(async () => changed = (await ChangeCurrencyAsync(browser, savings, "USD")).StatusCode);

        var moved = await browser.PutAsync(
            $"{Transactions}/{transaction.Id}", await ANewAsync(browser, "expense", savings, "100"), ifMatch: Quoted(transaction.RowVersion));

        Assert.Equal(HttpStatusCode.OK, changed);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var read = await GetAsync(browser, transaction.Id);
        Assert.Equal((savings, "USD"), (read.AccountId, read.Currency));
        Assert.Equal("USD", await CurrencyAsync(browser, savings));
    }

    [Fact]
    public async Task Transactions_recorded_at_the_same_time_as_a_change_of_their_accounts_currency_all_end_up_in_the_accounts_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var expense = await ANewAsync(browser, "expense", everyday, "100");

        var answers = await Task.WhenAll(Enumerable.Range(1, 6).Select(request =>
            request == 3 ? ChangeCurrencyAsync(browser, everyday, "USD") : browser.PostAsync(Transactions, expense)));

        // The change is refused when a transaction got in first; otherwise the ones recorded after it follow it.
        Assert.Contains(answers[2].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
        Assert.All(answers.Where((_, index) => index != 2), answer => Assert.Equal(HttpStatusCode.Created, answer.StatusCode));
        var currency = await CurrencyAsync(browser, everyday);
        Assert.Equal(answers[2].StatusCode == HttpStatusCode.OK ? "USD" : "VND", currency);
        var recorded = (await PageAsync(browser)).Items;
        Assert.Equal(5, recorded.Length);
        Assert.All(recorded, transaction => Assert.Equal(currency, transaction.Currency));
    }

    [Fact]
    public async Task A_categorys_kind_is_fixed_once_it_or_a_subcategory_has_transactions()
    {
        var (browser, _) = await api.SignedInAsync();
        await AddAsync(browser, "expense", await AddAccountAsync(browser, "Everyday"), "100");
        var food = await CategoryAsync(browser, "Food & drink");
        var shopping = await CategoryAsync(browser, "Shopping");

        var used = await browser.PutAsync($"/api/v1/categories/{food}", new { name = "Food & drink", kind = "income", icon = "utensils", color = "#ea580c" });
        var unused = await browser.PutAsync($"/api/v1/categories/{shopping}", new { name = "Shopping", kind = "income", icon = "shopping-bag", color = "#ea580c" });

        Assert.Equal(HttpStatusCode.BadRequest, used.StatusCode);
        Assert.Equal(["kind"], await used.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.OK, unused.StatusCode);
    }

    [Fact]
    public async Task A_categorys_kind_cannot_change_while_its_first_transaction_is_being_recorded()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var shopping = await CategoryAsync(browser, "Shopping");
        var recorded = HttpStatusCode.Unused;
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Transactions.Category>(async () => recorded = (await RecordOnAsync(browser, everyday, shopping)).StatusCode);

        var changed = await MakeShoppingIncomeAsync(browser, shopping);

        Assert.Equal(HttpStatusCode.Created, recorded);
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        Assert.Equal(["kind"], await changed.ErrorFieldsAsync());
        Assert.Equal("expense", await KindAsync(browser, shopping));
    }

    [Fact]
    public async Task A_transaction_recorded_while_its_categorys_kind_changes_is_refused_as_the_category_no_longer_takes_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var shopping = await CategoryAsync(browser, "Shopping");
        var changed = HttpStatusCode.Unused;
        api.Saves.BeforeNextSaveOf<Pfm.Domain.Transactions.Transaction>(async () => changed = (await MakeShoppingIncomeAsync(browser, shopping)).StatusCode);

        var recorded = await RecordOnAsync(browser, everyday, shopping);

        Assert.Equal(HttpStatusCode.OK, changed);
        Assert.Equal(HttpStatusCode.BadRequest, recorded.StatusCode);
        Assert.Equal(["categoryId"], await recorded.ErrorFieldsAsync());
        Assert.Empty((await PageAsync(browser)).Items);
        Assert.Equal("income", await KindAsync(browser, shopping));
    }

    [Fact]
    public async Task Expenses_recorded_at_the_same_time_as_their_category_becomes_an_income_category_are_all_recorded_or_none_is()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var shopping = await CategoryAsync(browser, "Shopping");

        var answers = await Task.WhenAll(Enumerable.Range(1, 6).Select(request =>
            request == 3 ? MakeShoppingIncomeAsync(browser, shopping) : RecordOnAsync(browser, everyday, shopping)));

        // The change is refused when an expense got in first; otherwise the expenses that come after it are.
        var recorded = (await PageAsync(browser)).Items;
        var kind = await KindAsync(browser, shopping);
        Assert.Equal(answers.Count(answer => answer.StatusCode == HttpStatusCode.Created), recorded.Length);
        Assert.Equal(recorded.Length == 0 ? "income" : "expense", kind);
        Assert.Equal(recorded.Length == 0 ? HttpStatusCode.OK : HttpStatusCode.BadRequest, answers[2].StatusCode);
        Assert.All(
            answers.Where((_, index) => index != 2),
            answer => Assert.Contains(answer.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest }));
    }

    [Fact]
    public async Task Another_users_transactions_are_never_visible_or_changeable()
    {
        var (bob, _) = await api.SignedInAsync();
        var bobsAccount = await AddAccountAsync(bob, "Bob's money", "VND", "1000");
        var bobs = await AddAsync(bob, "expense", bobsAccount, "100");
        var (alice, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(alice, "Everyday");
        var path = $"{Transactions}/{bobs.Id}";

        Assert.Empty((await PageAsync(alice)).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync(path)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await alice.PutAsync(path, await ANewAsync(alice, "expense", everyday, "1"), ifMatch: Quoted(bobs.RowVersion))).StatusCode);

        Assert.Equal(bobs, await GetAsync(bob, bobs.Id));
        Assert.Equal("900.00", await BalanceAsync(bob, bobsAccount));
    }

    [Fact]
    public async Task A_transaction_that_does_not_exist_is_not_found_as_problem_details()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Transactions}/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_cannot_see_transactions()
    {
        var response = await api.CreateBrowser().GetAsync(Transactions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> ChangeCurrencyAsync(Browser browser, Guid accountId, string currency)
    {
        var account = (await browser.Http.GetFromJsonAsync<AccountBody>($"/api/v1/accounts/{accountId}"))!;
        return await browser.PutAsync(
            $"/api/v1/accounts/{accountId}", new { name = "Everyday", type = "current", currency, openingBalance = "0", openingDate = "2026-11-01" },
            ifMatch: Quoted(account.RowVersion));
    }

    private static Task<HttpResponseMessage> RecordOnAsync(Browser browser, Guid accountId, Guid categoryId) =>
        browser.PostAsync(Transactions, new { type = "expense", accountId, amount = "100", date = "2026-11-02", categoryId });

    private static Task<HttpResponseMessage> MakeShoppingIncomeAsync(Browser browser, Guid shopping) =>
        browser.PutAsync($"/api/v1/categories/{shopping}", new { name = "Shopping", kind = "income", icon = "shopping-bag", color = "#ea580c" });

    /// <summary>The kind of one of the user's top-level categories.</summary>
    private static async Task<string> KindAsync(Browser browser, Guid categoryId) =>
        (await browser.Http.GetFromJsonAsync<CategoryTree>("/api/v1/categories"))!.Items.Single(category => category.Id == categoryId).Kind;

    private static async Task<string> CurrencyAsync(Browser browser, Guid accountId) =>
        (await browser.Http.GetFromJsonAsync<AccountBody>($"/api/v1/accounts/{accountId}"))!.Currency;

    private static string Quoted(string rowVersion) => $"\"{rowVersion}\"";

    /// <summary>An expense on Groceries or income on Salary, dated in the past, with the given fields changed.</summary>
    private static async Task<Dictionary<string, object?>> ANewAsync(Browser browser, string type, Guid accountId, string amount, string date = "2026-11-02")
    {
        var category = await CategoryAsync(browser, type == "income" ? "Salary" : "Groceries");
        return new() { ["type"] = type, ["accountId"] = accountId, ["amount"] = amount, ["date"] = date, ["categoryId"] = category };
    }

    private static async Task<TransactionBody> AddAsync(Browser browser, string type, Guid accountId, string amount, string date = "2026-11-02")
    {
        var response = await browser.PostAsync(Transactions, await ANewAsync(browser, type, accountId, amount, date));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    private static async Task<TransactionBody> GetAsync(Browser browser, Guid id)
    {
        var response = await browser.GetAsync($"{Transactions}/{id}");
        response.EnsureSuccessStatusCode();
        var transaction = (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
        Assert.Equal(Quoted(transaction.RowVersion), response.Headers.ETag?.Tag);
        return transaction;
    }

    private static async Task<TransactionList> PageAsync(Browser browser, string query = "")
    {
        var response = await browser.GetAsync(Transactions + query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransactionList>())!;
    }

    private sealed record Identified(Guid Id);

    private sealed record CategoryTree(CategoryBody[] Items);

    private sealed record CategoryBody(Guid Id, string Kind);

    private sealed record AccountBody(Guid Id, string Balance, string Currency, string RowVersion);

    private sealed record AccountList(AccountBody[] Items, TotalBody[] Totals);

    private sealed record TotalBody(string Balance);

    private sealed record TransactionList(TransactionBody[] Items, string? NextCursor);

    private sealed record TransactionBody(
        Guid Id, string Type, Guid AccountId, string Amount, string Currency, string Date, Guid CategoryId, string? Payee, string? Note, string RowVersion);
}
