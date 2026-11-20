using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Domain;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;

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

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_transaction_in_another_currency_is_converted_into_the_base_currency_at_the_rate_of_its_date_or_the_nearest_earlier_one()
    {
        await RatesAreAsync((Today.AddDays(-3), 24000m), (Today.AddDays(-1), 25000m), (Today, 26000m));
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "USD");

        var yesterday = await AddAsync(browser, dollars, "100", Today.AddDays(-1));
        var twoDaysAgo = await AddAsync(browser, dollars, "39.50", Today.AddDays(-2));

        Assert.Equal(new Converted(Currency.VND, 2_500_000m, 25000m, Today.AddDays(-1)), await ConvertedAsync(yesterday.Id));
        Assert.Equal(new Converted(Currency.VND, 948_000m, 24000m, Today.AddDays(-3)), await ConvertedAsync(twoDaysAgo.Id));
    }

    [Fact]
    public async Task A_transaction_in_the_base_currency_needs_no_rate()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "VND");

        var expense = await AddAsync(browser, everyday, "125000", Today.AddDays(-1));

        Assert.Equal(new Converted(Currency.VND, 125_000m, 1m, Today.AddDays(-1)), await ConvertedAsync(expense.Id));
    }

    [Fact]
    public async Task With_no_rate_on_or_before_its_date_a_transaction_is_still_saved_and_is_flagged_not_converted()
    {
        await RatesAreAsync((Today, 26000m));
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "USD");

        var expense = await AddAsync(browser, dollars, "100", Today.AddDays(-1));

        Assert.Equal(("100.00", "USD"), (expense.Amount, expense.Currency));
        Assert.Equal(new Converted(Currency.VND, null, null, null), await ConvertedAsync(expense.Id));
        Assert.Equal("-100.00", await BalanceAsync(browser, dollars));
    }

    [Fact]
    public async Task A_converted_amount_is_not_revalued_when_a_newer_rate_arrives_or_when_the_payee_note_category_or_type_is_edited()
    {
        await RatesAreAsync((Today.AddDays(-1), 25000m));
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "USD");
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
        var dollars = await AddAccountAsync(browser, "USD");
        var euros = await AddAccountAsync(browser, "EUR");
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
        var dollars = await AddAccountAsync(browser, "USD");
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
        var alicesDollars = await AddAccountAsync(alice, "USD");
        var today = await AddAsync(alice, alicesDollars, "100", Today);
        var yesterday = await AddAsync(alice, alicesDollars, "10", Today.AddDays(-1));
        var deleted = await AddAsync(alice, alicesDollars, "1", Today);
        (await alice.DeleteAsync($"{Transactions}/{deleted.Id}")).EnsureSuccessStatusCode();
        var bobs = await AddAsync(bob, await AddAccountAsync(bob, "USD"), "2", Today);
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
        var converted = await AddAsync(browser, await AddAccountAsync(browser, "USD"), "100", Today);
        var waiting = await AddAsync(browser, await AddAccountAsync(browser, "EUR"), "100", Today);

        await RatesAreAsync((Today, 26000m, 0.8m));

        Assert.Equal(new Converted(Currency.VND, 2_500_000m, 25000m, Today.AddDays(-1)), await ConvertedAsync(converted.Id));
        Assert.Equal(new Converted(Currency.VND, 3_250_000m, 32500m, Today), await ConvertedAsync(waiting.Id));
        Assert.Equal(converted.RowVersion, (await GetAsync(browser, converted.Id)).RowVersion);
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

    private static async Task<Guid> AddAccountAsync(Browser browser, string currency)
    {
        var response = await browser.PostAsync("/api/v1/accounts", new { name = $"In {currency}", type = "current", currency, openingBalance = "0", openingDate = "2026-01-01" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    private static async Task<string> BalanceAsync(Browser browser, Guid accountId) =>
        (await browser.Http.GetFromJsonAsync<AccountBody>($"/api/v1/accounts/{accountId}"))!.Balance;

    /// <summary>One of the default categories, by name.</summary>
    private static async Task<Guid> CategoryAsync(Browser browser, string name)
    {
        var tree = (await browser.Http.GetFromJsonAsync<CategoryList>("/api/v1/categories"))!.Items;
        return tree.Concat(tree.SelectMany(category => category.Subcategories)).Single(category => category.Name == name).Id;
    }

    /// <summary>A transaction's amount in the base currency; without an amount, it is not converted.</summary>
    private sealed record Converted(Currency? BaseCurrency, decimal? Amount, decimal? ExchangeRate, DateOnly? ExchangeRateDate);

    private sealed record Identified(Guid Id);

    private sealed record AccountBody(Guid Id, string Balance);

    private sealed record CategoryBody(Guid Id, string Name, List<CategoryBody> Subcategories);

    private sealed record CategoryList(List<CategoryBody> Items);

    private sealed record TransactionBody(
        Guid Id, string Type, Guid AccountId, string Amount, string Currency, string Date, Guid CategoryId, string? Payee, string? Note, string RowVersion);
}
