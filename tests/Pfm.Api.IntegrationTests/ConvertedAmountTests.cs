using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Domain;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// A transaction's amount is converted once into the user's base currency, at the exchange rate of its date,
/// and kept (spec section 3, rule 7; decision record 0002). Budgets and reports will read the converted
/// amount; until they exist no endpoint answers it, so these tests read it from the saved transaction.
/// </summary>
[Collection(ApiCollection.Name)]
public class ConvertedAmountTests(PfmApi api) : IAsyncLifetime
{
    private const string Transactions = "/api/v1/transactions";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync()
    {
        api.Saves.Stop();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_transaction_in_another_currency_is_converted_into_the_base_currency_at_the_rate_of_its_date_or_the_nearest_earlier_one()
    {
        await RatesAreAsync((Today.AddDays(-3), 24000m), (Today.AddDays(-1), 25000m), (Today, 26000m));
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");

        var yesterday = await AddAsync(browser, dollars, "100", Today.AddDays(-1));
        var twoDaysAgo = await AddAsync(browser, dollars, "39.50", Today.AddDays(-2));

        Assert.Equal(new Converted(Currency.VND, 2_500_000m, 25000m, Today.AddDays(-1)), await ConvertedAsync(yesterday.Id));
        Assert.Equal(new Converted(Currency.VND, 948_000m, 24000m, Today.AddDays(-3)), await ConvertedAsync(twoDaysAgo.Id));
    }

    [Fact]
    public async Task A_transaction_in_the_base_currency_needs_no_rate()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "In VND", "VND");

        var expense = await AddAsync(browser, everyday, "125000", Today.AddDays(-1));

        Assert.Equal(new Converted(Currency.VND, 125_000m, 1m, Today.AddDays(-1)), await ConvertedAsync(expense.Id));
    }

    [Fact]
    public async Task With_no_rate_on_or_before_its_date_a_transaction_is_still_saved_and_is_flagged_not_converted()
    {
        await RatesAreAsync((Today, 26000m));
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");

        var expense = await AddAsync(browser, dollars, "100", Today.AddDays(-1));

        Assert.Equal(("100.00", "USD"), (expense.Amount, expense.Currency));
        Assert.Equal(new Converted(Currency.VND, null, null, null), await ConvertedAsync(expense.Id));
        Assert.Equal("-100.00", await BalanceAsync(browser, dollars));
    }

    [Fact]
    public async Task An_amount_too_large_to_hold_in_the_base_currency_is_saved_and_flagged_not_converted()
    {
        await RatesAreAsync((Today, 25000m));
        var (browser, _) = await api.SignedInAsync();

        var expense = await AddAsync(browser, await AddAccountAsync(browser, "In USD", "USD"), "999999999999999", Today);

        Assert.Equal(new Converted(Currency.VND, null, null, null), await ConvertedAsync(expense.Id));
    }

    [Fact]
    public async Task A_converted_amount_is_not_revalued_when_a_newer_rate_arrives_or_when_the_payee_note_category_or_type_is_edited()
    {
        await RatesAreAsync((Today.AddDays(-1), 25000m));
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var expense = await AddAsync(browser, dollars, "100", Today);
        var atYesterdaysRate = new Converted(Currency.VND, 2_500_000m, 25000m, Today.AddDays(-1));
        Assert.Equal(atYesterdaysRate, await ConvertedAsync(expense.Id));

        await RatesAreAsync((Today, 26000m));
        Assert.Equal(atYesterdaysRate, await ConvertedAsync(expense.Id));

        var edited = await EditAsync(browser, await GetAsync(browser, expense.Id), new() { ["type"] = "income", ["categoryId"] = await CategoryAsync(browser, "Salary"), ["payee"] = "Acme", ["note"] = "refund of a deposit" });
        Assert.Equal(("income", "Acme"), (edited.Type, edited.Payee));
        Assert.Equal(atYesterdaysRate, await ConvertedAsync(expense.Id));
    }

    [Fact]
    public async Task Editing_the_amount_the_date_or_the_account_converts_the_amount_again_at_the_rate_of_its_date()
    {
        await RatesAreAsync((Today.AddDays(-2), 24000m, 0.8m), (Today.AddDays(-1), 25000m, 0.5m));
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var euros = await AddAccountAsync(browser, "In EUR", "EUR");
        var expense = await AddAsync(browser, dollars, "100", Today.AddDays(-1));

        expense = await EditAsync(browser, expense, new() { ["amount"] = "120" });
        Assert.Equal(new Converted(Currency.VND, 3_000_000m, 25000m, Today.AddDays(-1)), await ConvertedAsync(expense.Id));

        expense = await EditAsync(browser, expense, new() { ["date"] = Text(Today.AddDays(-2)) });
        Assert.Equal(new Converted(Currency.VND, 2_880_000m, 24000m, Today.AddDays(-2)), await ConvertedAsync(expense.Id));

        // A euro was 30,000 VND two days ago: 24,000 / 0.8.
        expense = await EditAsync(browser, expense, new() { ["accountId"] = euros });
        Assert.Equal(new Converted(Currency.VND, 3_600_000m, 30000m, Today.AddDays(-2)), await ConvertedAsync(expense.Id));

        expense = await EditAsync(browser, expense, new() { ["date"] = Text(Today.AddDays(-3)) });
        Assert.Equal(new Converted(Currency.VND, null, null, null), await ConvertedAsync(expense.Id));
    }

    [Fact]
    public async Task A_transaction_flagged_not_converted_is_converted_by_an_edit_once_a_rate_exists_whatever_the_edit_changes()
    {
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var expense = await AddAsync(browser, dollars, "100", Today);
        api.ExchangeRateSource.Quote(Today.AddDays(-1), dong: 25000m);
        await StoreRatesWithoutConvertingAsync();
        Assert.Equal(new Converted(Currency.VND, null, null, null), await ConvertedAsync(expense.Id));

        await EditAsync(browser, expense, new() { ["note"] = "paid in cash" });

        Assert.Equal(new Converted(Currency.VND, 2_500_000m, 25000m, Today.AddDays(-1)), await ConvertedAsync(expense.Id));
    }

    [Fact]
    public async Task When_new_rates_arrive_every_users_transactions_flagged_not_converted_are_converted_deleted_ones_too()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        var alicesDollars = await AddAccountAsync(alice, "In USD", "USD");
        var today = await AddAsync(alice, alicesDollars, "100", Today);
        var yesterday = await AddAsync(alice, alicesDollars, "10", Today.AddDays(-1));
        var deleted = await AddAsync(alice, alicesDollars, "1", Today);
        (await alice.DeleteAsync($"{Transactions}/{deleted.Id}")).EnsureSuccessStatusCode();
        var bobs = await AddAsync(bob, await AddAccountAsync(bob, "In USD", "USD"), "2", Today);
        Assert.Equal(new Converted(Currency.VND, null, null, null), await ConvertedAsync(today.Id));

        await RatesAreAsync((Today, 26000m));

        Assert.Equal(new Converted(Currency.VND, 2_600_000m, 26000m, Today), await ConvertedAsync(today.Id));
        Assert.Equal(new Converted(Currency.VND, 26_000m, 26000m, Today), await ConvertedAsync(deleted.Id));
        Assert.Equal(new Converted(Currency.VND, 52_000m, 26000m, Today), await ConvertedAsync(bobs.Id));

        // Still no rate on or before yesterday.
        Assert.Equal(new Converted(Currency.VND, null, null, null), await ConvertedAsync(yesterday.Id));
        Assert.Equal(today with { RowVersion = (await GetAsync(alice, today.Id)).RowVersion }, await GetAsync(alice, today.Id));
    }

    [Fact]
    public async Task New_rates_convert_only_what_is_not_converted_and_what_was_converted_stays_as_it_was()
    {
        await RatesAreAsync((Today.AddDays(-1), 25000m));
        var (browser, _) = await api.SignedInAsync();
        var converted = await AddAsync(browser, await AddAccountAsync(browser, "In USD", "USD"), "100", Today);
        var waiting = await AddAsync(browser, await AddAccountAsync(browser, "In EUR", "EUR"), "100", Today);

        await RatesAreAsync((Today, 26000m, 0.8m));

        Assert.Equal(new Converted(Currency.VND, 2_500_000m, 25000m, Today.AddDays(-1)), await ConvertedAsync(converted.Id));
        Assert.Equal(new Converted(Currency.VND, 3_250_000m, 32500m, Today), await ConvertedAsync(waiting.Id));
        Assert.Equal(converted.RowVersion, (await GetAsync(browser, converted.Id)).RowVersion);
    }

    [Fact]
    public async Task Every_fetch_converts_what_is_flagged_not_converted_also_one_that_stores_no_new_rates()
    {
        var (browser, _) = await api.SignedInAsync();
        var expense = await AddAsync(browser, await AddAccountAsync(browser, "In USD", "USD"), "100", Today);
        api.ExchangeRateSource.Quote(Today, dong: 26000m, euros: 0.8m);
        await StoreRatesWithoutConvertingAsync();

        var fetched = await api.FetchExchangeRatesAsync();

        Assert.Equal(0, fetched.Outcome.Days);
        Assert.Equal(new Converted(Currency.VND, 2_600_000m, 26000m, Today), await ConvertedAsync(expense.Id));
    }

    [Fact]
    public async Task Transactions_edited_while_new_rates_convert_them_keep_the_edit_and_every_user_ends_up_converted()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        var alices = await AddAsync(alice, await AddAccountAsync(alice, "In USD", "USD"), "100", Today);
        var bobs = await AddAsync(bob, await AddAccountAsync(bob, "In USD", "USD"), "2", Today);
        api.ExchangeRateSource.Quote(Today, dong: 26000m, euros: 0.8m);
        api.Saves.BeforeNextSaveOf<Transaction>(async () =>
        {
            await EditAsync(alice, alices, new() { ["payee"] = "Edited meanwhile" });
            await EditAsync(bob, bobs, new() { ["payee"] = "Edited meanwhile" });
        });

        var fetched = await api.FetchExchangeRatesAsync();

        Assert.Equal(0, fetched.Outcome.UsersNotSaved);
        Assert.Equal(new Converted(Currency.VND, 2_600_000m, 26000m, Today), await ConvertedAsync(alices.Id));
        Assert.Equal(new Converted(Currency.VND, 52_000m, 26000m, Today), await ConvertedAsync(bobs.Id));
        Assert.Equal("Edited meanwhile", (await GetAsync(alice, alices.Id)).Payee);
        Assert.Equal("Edited meanwhile", (await GetAsync(bob, bobs.Id)).Payee);
    }

    [Fact]
    public async Task A_user_whose_transactions_keep_changing_under_the_fetch_waits_for_the_next_one_and_the_other_users_are_converted()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        var alices = await AddAsync(alice, await AddAccountAsync(alice, "In USD", "USD"), "100", Today);
        var bobs = await AddAsync(bob, await AddAccountAsync(bob, "In USD", "USD"), "2", Today);
        api.ExchangeRateSource.Quote(Today, dong: 26000m, euros: 0.8m);
        api.Saves.BeforeEverySaveOf<Transaction>(() => SaveAgainAsItIsAsync(alices.Id));

        var fetched = await api.FetchExchangeRatesAsync();
        api.Saves.Stop();

        Assert.Equal(1, fetched.Outcome.UsersNotSaved);
        Assert.Equal(new Converted(Currency.VND, 52_000m, 26000m, Today), await ConvertedAsync(bobs.Id));
        Assert.Equal(new Converted(Currency.VND, null, null, null), await ConvertedAsync(alices.Id));

        var next = await api.FetchExchangeRatesAsync();

        Assert.Equal(0, next.Outcome.UsersNotSaved);
        Assert.Equal(new Converted(Currency.VND, 2_600_000m, 26000m, Today), await ConvertedAsync(alices.Id));
    }

    [Fact]
    public async Task Changing_the_base_currency_converts_every_transaction_into_the_new_one_at_the_rate_of_its_own_date()
    {
        await RatesAreAsync((Today.AddDays(-2), 24000m, 0.8m), (Today.AddDays(-1), 25000m, 0.5m));
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var inDollars = await AddAsync(browser, dollars, "100", Today.AddDays(-1));
        var inDong = await AddAsync(browser, await AddAccountAsync(browser, "In VND", "VND"), "1200000", Today.AddDays(-2));
        var beforeAnyRate = await AddAsync(browser, await AddAccountAsync(browser, "In EUR", "EUR"), "10", Today.AddDays(-3));
        var deleted = await AddAsync(browser, dollars, "8", Today.AddDays(-2));
        (await browser.DeleteAsync($"{Transactions}/{deleted.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(new Converted(Currency.VND, 2_500_000m, 25000m, Today.AddDays(-1)), await ConvertedAsync(inDollars.Id));

        (await browser.PatchAsync("/api/v1/me", new { baseCurrency = "EUR" })).EnsureSuccessStatusCode();

        Assert.Equal(new Converted(Currency.EUR, 50m, 0.5m, Today.AddDays(-1)), await ConvertedAsync(inDollars.Id));
        Assert.Equal(new Converted(Currency.EUR, 6.4m, 0.8m, Today.AddDays(-2)), await ConvertedAsync(deleted.Id));
        Assert.Equal(new Converted(Currency.EUR, 10m, 1m, Today.AddDays(-3)), await ConvertedAsync(beforeAnyRate.Id));

        // A dong was 0.8 / 24,000 of a euro two days ago.
        Assert.Equal(new Converted(Currency.EUR, 40m, 0.000033333333333333m, Today.AddDays(-2)), await ConvertedAsync(inDong.Id));

        (await browser.PatchAsync("/api/v1/me", new { baseCurrency = "USD" })).EnsureSuccessStatusCode();

        Assert.Equal(new Converted(Currency.USD, 100m, 1m, Today.AddDays(-1)), await ConvertedAsync(inDollars.Id));
        Assert.Equal(new Converted(Currency.USD, null, null, null), await ConvertedAsync(beforeAnyRate.Id));
    }

    [Fact]
    public async Task Changing_the_base_currency_converts_each_of_several_transactions_of_one_currency_and_date_by_its_own_amount()
    {
        await RatesAreAsync((Today.AddDays(-1), 25000m, 0.5m), (Today, 26000m, 0.8m));
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        var first = await AddAsync(browser, dollars, "100", Today);
        var second = await AddAsync(browser, dollars, "7.50", Today);
        var earlier = await AddAsync(browser, dollars, "7.50", Today.AddDays(-1));
        var inDong = await AddAsync(browser, await AddAccountAsync(browser, "In VND", "VND"), "65000", Today);

        (await browser.PatchAsync("/api/v1/me", new { baseCurrency = "EUR" })).EnsureSuccessStatusCode();

        Assert.Equal(new Converted(Currency.EUR, 80m, 0.8m, Today), await ConvertedAsync(first.Id));
        Assert.Equal(new Converted(Currency.EUR, 6m, 0.8m, Today), await ConvertedAsync(second.Id));
        Assert.Equal(new Converted(Currency.EUR, 3.75m, 0.5m, Today.AddDays(-1)), await ConvertedAsync(earlier.Id));
        Assert.Equal(2m, (await ConvertedAsync(inDong.Id)).Amount);
    }

    [Fact]
    public async Task A_transaction_edited_while_the_base_currency_changes_keeps_the_edit_and_is_converted_into_the_new_one()
    {
        await RatesAreAsync((Today, 25000m, 0.5m));
        var (browser, _) = await api.SignedInAsync();
        var expense = await AddAsync(browser, await AddAccountAsync(browser, "In USD", "USD"), "100", Today);
        api.Saves.BeforeNextSaveOf<Transaction>(() => EditAsync(browser, expense, new() { ["amount"] = "200.00" }));

        var response = await browser.PatchAsync("/api/v1/me", new { baseCurrency = "EUR" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("200.00", (await GetAsync(browser, expense.Id)).Amount);
        Assert.Equal(new Converted(Currency.EUR, 100m, 0.5m, Today), await ConvertedAsync(expense.Id));
    }

    [Fact]
    public async Task Changing_another_profile_setting_or_sending_the_same_base_currency_leaves_converted_amounts_as_they_are()
    {
        await RatesAreAsync((Today.AddDays(-1), 25000m));
        var (browser, _) = await api.SignedInAsync();
        var expense = await AddAsync(browser, await AddAccountAsync(browser, "In USD", "USD"), "100", Today);
        await RatesAreAsync((Today, 26000m));

        (await browser.PatchAsync("/api/v1/me", new { baseCurrency = "VND", timeZone = "Asia/Bangkok", locale = "en" })).EnsureSuccessStatusCode();

        Assert.Equal(new Converted(Currency.VND, 2_500_000m, 25000m, Today.AddDays(-1)), await ConvertedAsync(expense.Id));
        Assert.Equal(expense.RowVersion, (await GetAsync(browser, expense.Id)).RowVersion);
    }

    [Fact]
    public async Task One_users_change_of_base_currency_leaves_another_users_transactions_alone()
    {
        await RatesAreAsync((Today, 25000m));
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        var bobs = await AddAsync(bob, await AddAccountAsync(bob, "In USD", "USD"), "100", Today);

        (await alice.PatchAsync("/api/v1/me", new { baseCurrency = "USD" })).EnsureSuccessStatusCode();

        Assert.Equal(new Converted(Currency.VND, 2_500_000m, 25000m, Today), await ConvertedAsync(bobs.Id));
    }

    private async Task RatesAreAsync(params (DateOnly Date, decimal Dong)[] days)
    {
        foreach (var (date, dong) in days)
        {
            api.ExchangeRateSource.Quote(date, dong);
        }

        await api.FetchExchangeRatesAsync();
    }

    private async Task RatesAreAsync(params (DateOnly Date, decimal Dong, decimal Euros)[] days)
    {
        foreach (var (date, dong, euros) in days)
        {
            api.ExchangeRateSource.Quote(date, dong, euros);
        }

        await api.FetchExchangeRatesAsync();
    }

    /// <summary>
    /// Stores what the source quotes the way a fetch does, but stops there: as if the worker had stored the
    /// rates and not yet converted anything with them.
    /// </summary>
    private async Task StoreRatesWithoutConvertingAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PfmDbContext>();
        foreach (var day in await api.ExchangeRateSource.FetchAsync(DateOnly.MinValue, Today, CancellationToken.None))
        {
            db.AddRange(Pfm.Domain.ExchangeRates.ExchangeRate.BetweenEvery(day.QuotedAgainst, day.Date, day.Quotes));
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Saves the transaction with nothing changed, which still gives it a new row version: what any other save does to one read earlier.</summary>
    private async Task SaveAgainAsItIsAsync(Guid transactionId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PfmDbContext>().Database
            .ExecuteSqlAsync($"""UPDATE "Transactions" SET "Note" = "Note" WHERE "Id" = {transactionId}""");
    }

    /// <summary>What the saved transaction holds of its amount in the base currency, deleted or not.</summary>
    private async Task<Converted> ConvertedAsync(Guid transactionId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var transaction = await scope.ServiceProvider.GetRequiredService<PfmDbContext>().Set<Transaction>()
            .IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == transactionId);
        return new Converted(transaction.BaseCurrency, transaction.BaseCurrencyAmount, transaction.ExchangeRate, transaction.ExchangeRateDate);
    }

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd");

    private static async Task<TransactionBody> AddAsync(Browser browser, Guid accountId, string amount, DateOnly date)
    {
        var response = await browser.PostAsync(Transactions, new
        {
            type = "expense", accountId, amount, date = Text(date), categoryId = await CategoryAsync(browser, "Groceries"),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    /// <summary>Sends the transaction back with some of its fields changed.</summary>
    private static async Task<TransactionBody> EditAsync(Browser browser, TransactionBody transaction, Dictionary<string, object?> changes)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = transaction.Type, ["accountId"] = transaction.AccountId, ["amount"] = transaction.Amount, ["date"] = transaction.Date,
            ["categoryId"] = transaction.CategoryId, ["payee"] = transaction.Payee, ["note"] = transaction.Note,
        };
        foreach (var (field, value) in changes)
        {
            body[field] = value;
        }

        var response = await browser.PutAsync($"{Transactions}/{transaction.Id}", body, ifMatch: $"\"{transaction.RowVersion}\"");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionBody>())!;
    }

    private static async Task<TransactionBody> GetAsync(Browser browser, Guid id) =>
        (await browser.Http.GetFromJsonAsync<TransactionBody>($"{Transactions}/{id}"))!;

    /// <summary>A transaction's amount in the base currency; without an amount, it is not converted.</summary>
    private sealed record Converted(Currency? BaseCurrency, decimal? Amount, decimal? ExchangeRate, DateOnly? ExchangeRateDate);

    private sealed record TransactionBody(
        Guid Id, string Type, Guid AccountId, string Amount, string Currency, string Date, Guid CategoryId, string? Payee, string? Note, string RowVersion);
}
