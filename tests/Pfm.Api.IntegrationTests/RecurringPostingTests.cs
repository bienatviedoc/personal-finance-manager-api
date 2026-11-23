using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Transactions.Recurring;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// The worker posts each occurrence of a recurring rule as a transaction on its due date (spec REC-2 and the
/// acceptance notes of section 5). These tests post the way the worker's job does, and read what users then see.
/// </summary>
[Collection(ApiCollection.Name)]
public class RecurringPostingTests(PfmApi api)
{
    private const string Rules = "/api/v1/recurring-rules";

    private const string Transactions = "/api/v1/transactions";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Each_occurrence_is_posted_on_its_due_date_as_a_transaction_that_says_what_the_rule_says_and_links_back_to_it()
    {
        var browser = await SignedInOnUtcAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "20000000");
        var rule = await AddRuleAsync(browser, "expense", everyday, "9000000", Today, payee: "Landlord", note: "flat");

        await PostDueAsync();

        var posted = Assert.Single(await TransactionsAsync(browser));
        Assert.Equal(
            new TransactionBody(posted.Id, "expense", everyday, "9000000.00", "VND", Text(Today), rule.CategoryId, "Landlord", "flat", rule.Id),
            posted);
        Assert.Equal("11000000.00", await BalanceAsync(browser, everyday));
        Assert.Equal(Text(Today.AddDays(7)), (await RuleAsync(browser, rule.Id)).NextRunDate);
    }

    [Fact]
    public async Task An_occurrence_is_posted_once_however_often_the_worker_runs_and_does_not_come_back_when_its_transaction_is_deleted()
    {
        var browser = await SignedInOnUtcAsync();
        var rule = await AddRuleAsync(browser, "income", await AddAccountAsync(browser, "Everyday"), "100", Today);
        await PostDueAsync();
        var posted = Assert.Single(await TransactionsAsync(browser));

        await PostDueAsync();
        Assert.Equal([posted], await TransactionsAsync(browser));

        (await browser.DeleteAsync($"{Transactions}/{posted.Id}")).EnsureSuccessStatusCode();
        await PostDueAsync();
        Assert.Empty(await TransactionsAsync(browser));
        Assert.Equal(Text(Today.AddDays(7)), (await RuleAsync(browser, rule.Id)).NextRunDate);
    }

    [Fact]
    public async Task A_rule_that_is_not_due_yet_posts_nothing()
    {
        var browser = await SignedInOnUtcAsync();
        var rule = await AddRuleAsync(browser, "expense", await AddAccountAsync(browser, "Everyday"), "100", Today.AddDays(1));

        await PostDueAsync();

        Assert.Empty(await TransactionsAsync(browser));
        Assert.Equal(Text(Today.AddDays(1)), (await RuleAsync(browser, rule.Id)).NextRunDate);
    }

    [Fact]
    public async Task Occurrences_missed_while_the_worker_was_down_are_posted_afterwards_each_with_its_original_due_date()
    {
        var browser = await SignedInOnUtcAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var rule = await AddRuleAsync(browser, "income", everyday, "15000000", Today.AddDays(-15));

        await PostDueAsync();

        var posted = await TransactionsAsync(browser);
        Assert.Equal([Text(Today.AddDays(-1)), Text(Today.AddDays(-8)), Text(Today.AddDays(-15))], posted.Select(transaction => transaction.Date));
        Assert.All(posted, transaction => Assert.Equal((rule.Id, "15000000.00"), (transaction.RecurringRuleId, transaction.Amount)));
        Assert.Equal("45000000.00", await BalanceAsync(browser, everyday));
        Assert.Equal(Text(Today.AddDays(6)), (await RuleAsync(browser, rule.Id)).NextRunDate);
    }

    [Fact]
    public async Task The_due_date_is_read_in_the_users_time_zone()
    {
        // Kiritimati (UTC+14) and Pago Pago (UTC-11) are 25 hours apart: today in the first is always tomorrow in the second.
        var todayInKiritimati = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(14));
        var (ahead, _) = await api.SignedInAsync();
        (await ahead.PatchAsync("/api/v1/me", new { timeZone = "Pacific/Kiritimati" })).EnsureSuccessStatusCode();
        var (behind, _) = await api.SignedInAsync();
        (await behind.PatchAsync("/api/v1/me", new { timeZone = "Pacific/Pago_Pago" })).EnsureSuccessStatusCode();
        await AddRuleAsync(ahead, "expense", await AddAccountAsync(ahead, "Everyday"), "10", todayInKiritimati);
        await AddRuleAsync(behind, "expense", await AddAccountAsync(behind, "Everyday"), "10", todayInKiritimati);

        await PostDueAsync();

        Assert.Equal([Text(todayInKiritimati)], (await TransactionsAsync(ahead)).Select(transaction => transaction.Date));
        Assert.Empty(await TransactionsAsync(behind));
    }

    [Fact]
    public async Task Nothing_is_posted_after_the_end_date_and_the_rule_is_then_due_no_more()
    {
        var browser = await SignedInOnUtcAsync();
        var rule = await AddRuleAsync(browser, "expense", await AddAccountAsync(browser, "Everyday"), "100", Today.AddDays(-14), endDate: Today.AddDays(-7));

        await PostDueAsync();

        Assert.Equal([Text(Today.AddDays(-7)), Text(Today.AddDays(-14))], (await TransactionsAsync(browser)).Select(transaction => transaction.Date));
        Assert.Null((await RuleAsync(browser, rule.Id)).NextRunDate);
    }

    [Fact]
    public async Task A_paused_rule_posts_nothing_and_once_resumed_skips_what_was_due_meanwhile()
    {
        var browser = await SignedInOnUtcAsync();
        var rule = await AddRuleAsync(browser, "expense", await AddAccountAsync(browser, "Everyday"), "100", Today.AddDays(-14));
        (await browser.PostAsync($"{Rules}/{rule.Id}/pause")).EnsureSuccessStatusCode();

        await PostDueAsync();
        Assert.Empty(await TransactionsAsync(browser));

        var resumed = await (await browser.PostAsync($"{Rules}/{rule.Id}/resume")).Content.ReadFromJsonAsync<RuleBody>();
        Assert.Equal(Text(Today), resumed!.NextRunDate);
        await PostDueAsync();
        Assert.Equal([Text(Today)], (await TransactionsAsync(browser)).Select(transaction => transaction.Date));
    }

    [Fact]
    public async Task An_occurrence_a_manual_transaction_would_be_refused_for_is_not_posted_and_waits_while_other_rules_carry_on()
    {
        var browser = await SignedInOnUtcAsync();
        var closed = await AddAccountAsync(browser, "Closed");
        var everyday = await AddAccountAsync(browser, "Everyday");
        var stuck = await AddRuleAsync(browser, "expense", closed, "100", Today.AddDays(-7));
        var fine = await AddRuleAsync(browser, "expense", everyday, "200", Today);
        (await browser.DeleteAsync($"/api/v1/accounts/{closed}")).EnsureSuccessStatusCode();

        var outcome = await PostDueAsync();

        Assert.Equal([fine.Id], (await TransactionsAsync(browser)).Select(transaction => transaction.RecurringRuleId));
        Assert.Equal(Text(Today.AddDays(-7)), (await RuleAsync(browser, stuck.Id)).NextRunDate);
        Assert.True(outcome.RulesNotPosted >= 1);
        (await browser.DeleteAsync($"{Rules}/{stuck.Id}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Editing_a_rule_leaves_the_transactions_it_posted_as_they_are_and_posts_no_occurrence_twice()
    {
        var browser = await SignedInOnUtcAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var rule = await AddRuleAsync(browser, "expense", everyday, "100", Today);
        await PostDueAsync();

        var edited = await browser.PutAsync($"{Rules}/{rule.Id}", new
        {
            type = "expense", accountId = everyday, amount = "250", categoryId = rule.CategoryId, frequency = "weekly", startDate = Text(Today.AddDays(-21)),
        }, (await browser.GetAsync($"{Rules}/{rule.Id}")).Headers.ETag!.Tag);
        await PostDueAsync();

        Assert.Equal(Text(Today.AddDays(7)), (await edited.Content.ReadFromJsonAsync<RuleBody>())!.NextRunDate);
        Assert.Equal(["100.00"], (await TransactionsAsync(browser)).Select(transaction => transaction.Amount));
    }

    [Fact]
    public async Task Deleting_a_rule_keeps_the_transactions_it_posted_no_longer_linked_to_a_rule()
    {
        var browser = await SignedInOnUtcAsync();
        var rule = await AddRuleAsync(browser, "expense", await AddAccountAsync(browser, "Everyday"), "100", Today.AddDays(-7));
        await PostDueAsync();

        (await browser.DeleteAsync($"{Rules}/{rule.Id}")).EnsureSuccessStatusCode();
        await PostDueAsync();

        var kept = await TransactionsAsync(browser);
        Assert.Equal(2, kept.Length);
        Assert.All(kept, transaction => Assert.Null(transaction.RecurringRuleId));
    }

    [Fact]
    public async Task Two_workers_posting_at_once_post_each_occurrence_once()
    {
        var browser = await SignedInOnUtcAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var rule = await AddRuleAsync(browser, "income", everyday, "100", Today.AddDays(-28));

        await Task.WhenAll(PostDueAsync(), PostDueAsync(), PostDueAsync());

        Assert.Equal(
            [Text(Today), Text(Today.AddDays(-7)), Text(Today.AddDays(-14)), Text(Today.AddDays(-21)), Text(Today.AddDays(-28))],
            (await TransactionsAsync(browser)).Select(transaction => transaction.Date));
        Assert.Equal("500.00", await BalanceAsync(browser, everyday));
        Assert.Equal(Text(Today.AddDays(7)), (await RuleAsync(browser, rule.Id)).NextRunDate);
    }

    [Fact]
    public async Task Nothing_is_posted_for_a_deactivated_user_and_what_was_due_meanwhile_is_posted_once_they_cancel_the_deletion()
    {
        var (browser, email) = await api.SignedInAsync();
        (await browser.PatchAsync("/api/v1/me", new { timeZone = "Etc/UTC" })).EnsureSuccessStatusCode();
        var userId = (await browser.Http.GetFromJsonAsync<Identified>("/api/v1/me"))!.Id;
        var rule = await AddRuleAsync(browser, "expense", await AddAccountAsync(browser, "Everyday"), "100", Today.AddDays(-7));
        (await browser.PostAsync("/api/v1/me/deletion", new { password = PfmApi.Password })).EnsureSuccessStatusCode();

        await PostDueAsync();
        Assert.DoesNotContain("Transactions", (await api.RowsOwnedByAsync(userId)).Keys);

        // Signing in within the 30 days cancels the deletion.
        (await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password })).EnsureSuccessStatusCode();
        await PostDueAsync();
        Assert.Equal([Text(Today), Text(Today.AddDays(-7))], (await TransactionsAsync(browser)).Select(transaction => transaction.Date));
        Assert.Equal(Text(Today.AddDays(7)), (await RuleAsync(browser, rule.Id)).NextRunDate);
    }

    [Fact]
    public async Task A_transaction_a_user_records_is_linked_to_no_rule()
    {
        var browser = await SignedInOnUtcAsync();

        var response = await browser.PostAsync(Transactions, new
        {
            type = "expense", accountId = await AddAccountAsync(browser, "Everyday"), amount = "10", date = Text(Today), categoryId = await CategoryAsync(browser, "Groceries"),
            recurringRuleId = Guid.CreateVersion7(),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<TransactionBody>())!.RecurringRuleId);
    }

    /// <summary>Posts what is due the way the worker's job does: for every user, acting for each in turn.</summary>
    private async Task<OccurrencesPosted> PostDueAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RecurringPosting>().PostDueAsync();
    }

    /// <summary>A new user whose calendar is UTC's, so that today is the same date for them and for the test.</summary>
    private async Task<Browser> SignedInOnUtcAsync()
    {
        var (browser, _) = await api.SignedInAsync();
        (await browser.PatchAsync("/api/v1/me", new { timeZone = "Etc/UTC" })).EnsureSuccessStatusCode();
        return browser;
    }

    /// <summary>A weekly rule on Rent or Salary.</summary>
    private static async Task<RuleBody> AddRuleAsync(
        Browser browser, string type, Guid accountId, string amount, DateOnly startDate, DateOnly? endDate = null, string? payee = null, string? note = null)
    {
        var response = await browser.PostAsync(Rules, new
        {
            type, accountId, amount, categoryId = await CategoryAsync(browser, type == "income" ? "Salary" : "Rent"), payee, note,
            frequency = "weekly", startDate = Text(startDate), endDate = endDate is { } end ? Text(end) : null,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RuleBody>())!;
    }

    private static async Task<RuleBody> RuleAsync(Browser browser, Guid id) => (await browser.Http.GetFromJsonAsync<RuleBody>($"{Rules}/{id}"))!;

    /// <summary>The user's transactions, newest date first.</summary>
    private static async Task<TransactionBody[]> TransactionsAsync(Browser browser) =>
        (await browser.Http.GetFromJsonAsync<TransactionList>(Transactions))!.Items;

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record Identified(Guid Id);

    private sealed record RuleBody(Guid Id, Guid CategoryId, string? NextRunDate);

    private sealed record TransactionList(TransactionBody[] Items);

    private sealed record TransactionBody(
        Guid Id, string Type, Guid AccountId, string Amount, string Currency, string Date, Guid CategoryId, string? Payee, string? Note, Guid? RecurringRuleId);
}
