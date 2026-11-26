using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Transactions.Recurring;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// A rule set to remind is never posted by the worker: each due occurrence is a reminder the user confirms,
/// optionally adjusting the amount, or dismisses (spec REC-2). The upcoming list shows every occurrence due in
/// the next days across all rules (REC-3).
/// </summary>
[Collection(ApiCollection.Name)]
public class RecurringReminderTests(PfmApi api)
{
    private const string Rules = "/api/v1/recurring-rules";

    private const string Transactions = "/api/v1/transactions";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task A_rule_posts_its_occurrences_unless_it_is_set_to_remind()
    {
        var browser = await SignedInOnUtcAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");

        var posts = await AddRuleAsync(browser, everyday, "100", Today.AddDays(1), mode: null);
        var reminds = await AddRuleAsync(browser, everyday, "100", Today.AddDays(1), mode: "remind");

        Assert.Equal(("autoPost", "remind"), (posts.Mode, reminds.Mode));
        Assert.Equal("remind", (await RuleAsync(browser, reminds.Id)).Mode);
    }

    [Fact]
    public async Task A_mode_that_is_not_one_is_refused()
    {
        var browser = await SignedInOnUtcAsync();

        var response = await browser.PostAsync(Rules, new
        {
            type = "expense", accountId = await AddAccountAsync(browser, "Everyday"), amount = "100", categoryId = await CategoryAsync(browser, "Rent"),
            frequency = "weekly", startDate = Text(Today), mode = "sometimes",
        });

        Assert.Equal(["mode"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task The_worker_posts_nothing_for_a_rule_that_reminds_and_each_due_occurrence_is_a_reminder_instead()
    {
        var browser = await SignedInOnUtcAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND");
        var rule = await AddRuleAsync(browser, everyday, "9000000", Today.AddDays(-7), mode: "remind", payee: "Landlord");

        await PostDueAsync();

        Assert.Empty(await TransactionsAsync(browser));
        Assert.Equal(
            [
                new OccurrenceBody(rule.Id, Text(Today.AddDays(-7)), "expense", everyday, "9000000.00", "VND", rule.CategoryId, "Landlord", null, "remind"),
                new OccurrenceBody(rule.Id, Text(Today), "expense", everyday, "9000000.00", "VND", rule.CategoryId, "Landlord", null, "remind"),
            ],
            await RemindersAsync(browser));
    }

    [Fact]
    public async Task Confirming_a_reminder_records_its_transaction_on_its_due_date_and_the_reminder_is_gone()
    {
        var browser = await SignedInOnUtcAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var rule = await AddRuleAsync(browser, everyday, "100", Today.AddDays(-7), mode: "remind");

        var response = await browser.PostAsync($"{Rules}/{rule.Id}/reminders/{Text(Today.AddDays(-7))}/confirm", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Text(Today), (await response.Content.ReadFromJsonAsync<RuleBody>())!.NextRunDate);
        var posted = Assert.Single(await TransactionsAsync(browser));
        Assert.Equal((Text(Today.AddDays(-7)), "100.00", rule.Id), (posted.Date, posted.Amount, posted.RecurringRuleId));
        Assert.Equal([Text(Today)], (await RemindersAsync(browser)).Select(reminder => reminder.DueDate));
    }

    [Fact]
    public async Task A_reminder_can_be_confirmed_with_another_amount_and_the_rule_keeps_its_own()
    {
        var browser = await SignedInOnUtcAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "USD");
        var rule = await AddRuleAsync(browser, everyday, "100", Today, mode: "remind");

        (await browser.PostAsync($"{Rules}/{rule.Id}/reminders/{Text(Today)}/confirm", new { amount = "123.45" })).EnsureSuccessStatusCode();

        Assert.Equal(["123.45"], (await TransactionsAsync(browser)).Select(transaction => transaction.Amount));
        Assert.Equal("100.00", (await RuleAsync(browser, rule.Id)).Amount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    public async Task A_reminder_is_confirmed_with_an_amount_greater_than_zero(string amount)
    {
        var browser = await SignedInOnUtcAsync();
        var rule = await AddRuleAsync(browser, await AddAccountAsync(browser, "Everyday"), "100", Today, mode: "remind");

        var response = await browser.PostAsync($"{Rules}/{rule.Id}/reminders/{Text(Today)}/confirm", new { amount });

        Assert.Equal(["amount"], await response.ErrorFieldsAsync());
        Assert.Empty(await TransactionsAsync(browser));
    }

    [Fact]
    public async Task Dismissing_a_reminder_records_nothing_and_the_reminder_is_gone()
    {
        var browser = await SignedInOnUtcAsync();
        var rule = await AddRuleAsync(browser, await AddAccountAsync(browser, "Everyday"), "100", Today, mode: "remind");

        var response = await browser.PostAsync($"{Rules}/{rule.Id}/reminders/{Text(Today)}/dismiss");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Text(Today.AddDays(7)), (await response.Content.ReadFromJsonAsync<RuleBody>())!.NextRunDate);
        Assert.Empty(await RemindersAsync(browser));
        await PostDueAsync();
        Assert.Empty(await TransactionsAsync(browser));
    }

    [Fact]
    public async Task A_rule_s_reminders_are_confirmed_or_dismissed_earliest_first()
    {
        var browser = await SignedInOnUtcAsync();
        var rule = await AddRuleAsync(browser, await AddAccountAsync(browser, "Everyday"), "100", Today.AddDays(-7), mode: "remind");

        var response = await browser.PostAsync($"{Rules}/{rule.Id}/reminders/{Text(Today)}/confirm", new { });

        Assert.Equal(["dueDate"], await response.ErrorFieldsAsync());
        Assert.Empty(await TransactionsAsync(browser));
        Assert.Equal(["dueDate"], await (await browser.PostAsync($"{Rules}/{rule.Id}/reminders/{Text(Today)}/dismiss")).ErrorFieldsAsync());
    }

    [Fact]
    public async Task Only_a_reminder_the_user_has_can_be_confirmed_or_dismissed()
    {
        var browser = await SignedInOnUtcAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var reminds = await AddRuleAsync(browser, everyday, "100", Today, mode: "remind");
        var posts = await AddRuleAsync(browser, everyday, "100", Today, mode: null);
        var stranger = await SignedInOnUtcAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync($"{Rules}/{reminds.Id}/reminders/{Text(Today.AddDays(7))}/confirm", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync($"{Rules}/{posts.Id}/reminders/{Text(Today)}/confirm", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"{Rules}/{reminds.Id}/reminders/{Text(Today)}/confirm", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"{Rules}/{reminds.Id}/reminders/{Text(Today)}/dismiss")).StatusCode);
        Assert.Equal(["dueDate"], await (await browser.PostAsync($"{Rules}/{reminds.Id}/reminders/not-a-date/dismiss")).ErrorFieldsAsync());
        Assert.Single(await RemindersAsync(browser));
        Assert.Empty(await RemindersAsync(stranger));
    }

    [Fact]
    public async Task A_paused_rule_has_no_reminders()
    {
        var browser = await SignedInOnUtcAsync();
        var rule = await AddRuleAsync(browser, await AddAccountAsync(browser, "Everyday"), "100", Today, mode: "remind");

        (await browser.PostAsync($"{Rules}/{rule.Id}/pause")).EnsureSuccessStatusCode();

        Assert.Empty(await RemindersAsync(browser));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync($"{Rules}/{rule.Id}/reminders/{Text(Today)}/confirm", new { })).StatusCode);
    }

    [Fact]
    public async Task The_upcoming_list_shows_every_occurrence_due_in_the_next_30_days_across_all_rules_earliest_first()
    {
        var browser = await SignedInOnUtcAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var weekly = await AddRuleAsync(browser, everyday, "100", Today.AddDays(-7), mode: null);
        var reminds = await AddRuleAsync(browser, everyday, "50", Today.AddDays(10), mode: "remind", frequency: "monthly");
        var paused = await AddRuleAsync(browser, everyday, "75", Today.AddDays(3), mode: null);
        (await browser.PostAsync($"{Rules}/{paused.Id}/pause")).EnsureSuccessStatusCode();
        await PostDueAsync();

        var upcoming = await UpcomingAsync(browser, "?days=30");

        Assert.Equal(
            [
                (weekly.Id, Text(Today.AddDays(7)), "autoPost"), (reminds.Id, Text(Today.AddDays(10)), "remind"), (weekly.Id, Text(Today.AddDays(14)), "autoPost"),
                (weekly.Id, Text(Today.AddDays(21)), "autoPost"), (weekly.Id, Text(Today.AddDays(28)), "autoPost"),
            ],
            upcoming.Select(occurrence => (occurrence.RecurringRuleId, occurrence.DueDate, occurrence.Mode)));
        Assert.Equal(upcoming, await UpcomingAsync(browser, string.Empty));
        Assert.Equal([Text(Today.AddDays(7))], (await UpcomingAsync(browser, "?days=7")).Select(occurrence => occurrence.DueDate));
    }

    [Theory]
    [InlineData("?days=0")]
    [InlineData("?days=91")]
    public async Task The_upcoming_list_looks_1_to_90_days_ahead(string query)
    {
        var browser = await SignedInOnUtcAsync();

        Assert.Equal(["days"], await (await browser.GetAsync($"{Rules}/upcoming{query}")).ErrorFieldsAsync());
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_sees_no_reminders_or_upcoming_occurrences()
    {
        var visitor = api.CreateBrowser();

        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync($"{Rules}/upcoming")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync($"{Rules}/reminders")).StatusCode);
    }

    /// <summary>Posts what is due the way the worker's job does.</summary>
    private async Task PostDueAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RecurringPosting>().PostDueAsync();
    }

    /// <summary>A new user whose calendar is UTC's, so that today is the same date for them and for the test.</summary>
    private async Task<Browser> SignedInOnUtcAsync()
    {
        var (browser, _) = await api.SignedInAsync();
        (await browser.PatchAsync("/api/v1/me", new { timeZone = "Etc/UTC" })).EnsureSuccessStatusCode();
        return browser;
    }

    /// <summary>A rule on Rent, weekly unless told otherwise; a null mode is left out.</summary>
    private static async Task<RuleBody> AddRuleAsync(
        Browser browser, Guid accountId, string amount, DateOnly startDate, string? mode, string frequency = "weekly", string? payee = null)
    {
        var response = await browser.PostAsync(Rules, new
        {
            type = "expense", accountId, amount, categoryId = await CategoryAsync(browser, "Rent"), payee, frequency, startDate = Text(startDate), mode,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RuleBody>())!;
    }

    private static async Task<RuleBody> RuleAsync(Browser browser, Guid id) => (await browser.Http.GetFromJsonAsync<RuleBody>($"{Rules}/{id}"))!;

    private static async Task<OccurrenceBody[]> RemindersAsync(Browser browser) =>
        (await browser.Http.GetFromJsonAsync<OccurrenceList>($"{Rules}/reminders"))!.Items;

    private static async Task<OccurrenceBody[]> UpcomingAsync(Browser browser, string query) =>
        (await browser.Http.GetFromJsonAsync<OccurrenceList>($"{Rules}/upcoming{query}"))!.Items;

    private static async Task<TransactionBody[]> TransactionsAsync(Browser browser) =>
        (await browser.Http.GetFromJsonAsync<TransactionList>(Transactions))!.Items;

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record RuleBody(Guid Id, Guid CategoryId, string Amount, string? NextRunDate, string Mode);

    private sealed record OccurrenceList(OccurrenceBody[] Items);

    private sealed record OccurrenceBody(
        Guid RecurringRuleId, string DueDate, string Type, Guid AccountId, string Amount, string Currency, Guid CategoryId, string? Payee, string? Note,
        string Mode);

    private sealed record TransactionList(TransactionBody[] Items);

    private sealed record TransactionBody(Guid Id, string Date, string Amount, Guid? RecurringRuleId);
}
