using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Transactions.Recurring;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Budget notifications, in the app and by email, when a category moves into warning or over (spec BUD-7 and section 6).</summary>
[Collection(ApiCollection.Name)]
public class BudgetNotificationTests(PfmApi api) : IAsyncLifetime
{
    private const string Notifications = "/api/v1/budget-notifications";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly string ThisMonth = Today.ToString("yyyy-MM");

    private static readonly DateOnly FirstOfThisMonth = new(Today.Year, Today.Month, 1);

    private static readonly DateOnly InLastMonth = FirstOfThisMonth.AddMonths(-1);

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_category_that_moves_into_warning_raises_a_notification_in_the_app_and_by_email()
    {
        var (browser, email) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var transport = await BudgetAsync(browser, "Transport", "1000000");

        await SpendAsync(browser, everyday, "Transport", "850000", FirstOfThisMonth);

        var list = await ListAsync(browser);
        var notification = Assert.Single(list.Items);
        Assert.Equal(
            (ThisMonth, transport, "Transport", "warning", "1000000.00", "850000.00", 85, "VND", (DateTimeOffset?)null),
            (notification.Month, notification.Category.Id, notification.Category.Name, notification.State, notification.Budgeted, notification.Spent,
                notification.PercentSpent, notification.Currency, notification.ReadAt));
        Assert.Equal(1, list.Unread);

        var sent = Assert.Single(BudgetEmails(email));
        Assert.Contains("Transport", sent.Subject);
        Assert.Contains("85%", sent.Body);
        Assert.Contains($"{PfmApi.PublicUrl}/budgets/{ThisMonth}", sent.Body);
    }

    [Fact]
    public async Task A_category_that_moves_into_over_raises_an_over_notification_the_newest_first()
    {
        var (browser, email) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await BudgetAsync(browser, "Transport", "1000000");

        await SpendAsync(browser, everyday, "Transport", "850000", FirstOfThisMonth);
        await SpendAsync(browser, everyday, "Transport", "200000", FirstOfThisMonth);

        Assert.Equal(["over", "warning"], (await ListAsync(browser)).Items.Select(item => item.State));
        Assert.Equal(2, BudgetEmails(email).Count);
    }

    [Fact]
    public async Task A_category_that_jumps_straight_over_raises_only_over()
    {
        var (browser, _) = await api.SignedInAsync();
        await BudgetAsync(browser, "Transport", "1000000");

        await SpendAsync(browser, await AddAccountAsync(browser, "Everyday"), "Transport", "1200000", FirstOfThisMonth);

        Assert.Equal(["over"], (await ListAsync(browser)).Items.Select(item => item.State));
    }

    [Fact]
    public async Task A_category_on_track_raises_nothing()
    {
        var (browser, email) = await api.SignedInAsync();
        await BudgetAsync(browser, "Transport", "1000000");

        await SpendAsync(browser, await AddAccountAsync(browser, "Everyday"), "Transport", "790000", FirstOfThisMonth);

        Assert.Empty((await ListAsync(browser)).Items);
        Assert.Empty(BudgetEmails(email));
    }

    [Fact]
    public async Task Each_category_raises_at_most_one_warning_and_one_over_a_month_even_when_spent_drops_back_and_crosses_again()
    {
        var (browser, email) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await BudgetAsync(browser, "Transport", "1000000");

        var first = await SpendAsync(browser, everyday, "Transport", "850000", FirstOfThisMonth);
        await browser.DeleteAsync($"/api/v1/transactions/{first}");
        await SpendAsync(browser, everyday, "Transport", "900000", FirstOfThisMonth);
        await SpendAsync(browser, everyday, "Transport", "200000", FirstOfThisMonth);
        await RefundAsync(browser, everyday, "Transport", "300000", FirstOfThisMonth);
        await SpendAsync(browser, everyday, "Transport", "300000", FirstOfThisMonth);

        Assert.Equal(["over", "warning"], (await ListAsync(browser)).Items.Select(item => item.State));
        Assert.Equal(2, BudgetEmails(email).Count);
    }

    [Fact]
    public async Task Spending_in_a_subcategory_raises_the_notification_of_the_budget_on_its_parent()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = await BudgetAsync(browser, "Food & drink", "1000000");

        await SpendAsync(browser, await AddAccountAsync(browser, "Everyday"), "Groceries", "900000", FirstOfThisMonth);

        Assert.Equal(food, Assert.Single((await ListAsync(browser)).Items).Category.Id);
    }

    [Fact]
    public async Task Email_can_be_switched_off_in_settings_and_the_notification_still_appears_in_the_app()
    {
        var (browser, email) = await api.SignedInAsync();
        await BudgetAsync(browser, "Transport", "1000000");

        var patched = await browser.PatchAsync("/api/v1/me", new { emailBudgetNotifications = false });
        await SpendAsync(browser, await AddAccountAsync(browser, "Everyday"), "Transport", "850000", FirstOfThisMonth);

        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.False((await browser.Http.GetFromJsonAsync<ProfileBody>("/api/v1/me"))!.EmailBudgetNotifications);
        Assert.Single((await ListAsync(browser)).Items);
        Assert.Empty(BudgetEmails(email));
    }

    [Fact]
    public async Task Budget_notifications_are_emailed_until_switched_off()
    {
        var (browser, _) = await api.SignedInAsync();

        Assert.True((await browser.Http.GetFromJsonAsync<ProfileBody>("/api/v1/me"))!.EmailBudgetNotifications);
    }

    [Fact]
    public async Task The_email_is_in_the_users_locale()
    {
        var (browser, email) = await api.SignedInAsync(locale: "vi");
        await BudgetAsync(browser, "Di chuyển", "1000000");

        await SpendAsync(browser, await AddAccountAsync(browser, "Everyday"), "Di chuyển", "1200000", FirstOfThisMonth);

        var sent = Assert.Single(BudgetEmails(email));
        Assert.Contains("Di chuyển", sent.Subject);
        Assert.Contains("vượt", sent.Subject);
    }

    [Fact]
    public async Task A_late_edit_that_moves_spent_raises_the_notification_in_the_month_it_lands_in()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var transport = await CategoryAsync(browser, "Transport");
        (await browser.PutAsync($"/api/v1/budgets/{InLastMonth:yyyy-MM}/{transport}", new { amount = "1000000" })).EnsureSuccessStatusCode();
        var expense = await SpendAsync(browser, everyday, "Transport", "100000", FirstOfThisMonth);

        var edited = await browser.PutAsync($"/api/v1/transactions/{expense}", new
        {
            type = "expense", accountId = everyday, amount = "1100000", date = InLastMonth.ToString("yyyy-MM-dd"), categoryId = transport,
        }, ifMatch: await TransactionRowVersionAsync(browser, expense));

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var notification = Assert.Single((await ListAsync(browser)).Items);
        Assert.Equal(($"{InLastMonth:yyyy-MM}", "over"), (notification.Month, notification.State));
    }

    [Fact]
    public async Task A_lower_budget_that_spent_is_now_over_raises_a_notification()
    {
        var (browser, _) = await api.SignedInAsync();
        var transport = await BudgetAsync(browser, "Transport", "1000000");
        await SpendAsync(browser, await AddAccountAsync(browser, "Everyday"), "Transport", "500000", FirstOfThisMonth);
        var rowVersion = (await browser.Http.GetFromJsonAsync<MonthBody>($"/api/v1/budgets?month={ThisMonth}"))!.Items[0].RowVersion;

        (await browser.PutAsync($"/api/v1/budgets/{ThisMonth}/{transport}", new { amount = "400000" }, ifMatch: $"\"{rowVersion}\"")).EnsureSuccessStatusCode();

        Assert.Equal(["over"], (await ListAsync(browser)).Items.Select(item => item.State));
    }

    [Fact]
    public async Task An_expense_converted_once_its_rate_arrives_raises_the_notification_then()
    {
        var (browser, _) = await api.SignedInAsync();
        await BudgetAsync(browser, "Transport", "1000000");
        await SpendAsync(browser, await AddAccountAsync(browser, "In USD", "USD"), "Transport", "40", FirstOfThisMonth);
        Assert.Empty((await ListAsync(browser)).Items);

        api.ExchangeRateSource.Quote(FirstOfThisMonth.AddDays(-1), 25000m);
        await api.FetchExchangeRatesAsync();

        Assert.Equal(["warning"], (await ListAsync(browser)).Items.Select(item => item.State));
    }

    [Fact]
    public async Task An_occurrence_the_worker_posts_raises_the_notification_too()
    {
        var (browser, email) = await api.SignedInAsync();
        await BudgetAsync(browser, "Transport", "1000000");
        (await browser.PostAsync("/api/v1/recurring-rules", new
        {
            type = "expense", accountId = await AddAccountAsync(browser, "Everyday"), amount = "1500000", categoryId = await CategoryAsync(browser, "Transport"),
            frequency = "monthly", startDate = FirstOfThisMonth.ToString("yyyy-MM-dd"),
        })).EnsureSuccessStatusCode();

        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RecurringPosting>().PostDueAsync();
        }

        Assert.Equal(["over"], (await ListAsync(browser)).Items.Select(item => item.State));
        Assert.Single(BudgetEmails(email));
    }

    [Fact]
    public async Task Restoring_a_deleted_expense_raises_the_notification_it_brings_spent_to()
    {
        var (browser, _) = await api.SignedInAsync();
        var expense = await SpendAsync(browser, await AddAccountAsync(browser, "Everyday"), "Transport", "900000", FirstOfThisMonth);
        (await browser.DeleteAsync($"/api/v1/transactions/{expense}")).EnsureSuccessStatusCode();
        await BudgetAsync(browser, "Transport", "1000000");

        (await browser.PostAsync($"/api/v1/transactions/{expense}/restore")).EnsureSuccessStatusCode();

        Assert.Equal(["warning"], (await ListAsync(browser)).Items.Select(item => item.State));
    }

    [Fact]
    public async Task Expenses_a_bulk_action_puts_on_a_category_raise_its_notification()
    {
        var (browser, _) = await api.SignedInAsync();
        await BudgetAsync(browser, "Transport", "1000000");
        var expense = await SpendAsync(browser, await AddAccountAsync(browser, "Everyday"), "Shopping", "1200000", FirstOfThisMonth);

        (await browser.PostAsync("/api/v1/transactions/bulk", new
        {
            action = "setCategory", transactionIds = new[] { expense }, categoryId = await CategoryAsync(browser, "Transport"),
        })).EnsureSuccessStatusCode();

        Assert.Equal(["over"], (await ListAsync(browser)).Items.Select(item => item.State));
    }

    [Fact]
    public async Task A_transfer_unmarked_into_an_expense_raises_the_notification_of_its_category()
    {
        var (browser, _) = await api.SignedInAsync();
        await BudgetAsync(browser, "Transport", "1000000");
        var created = await browser.PostAsync("/api/v1/transfers", new
        {
            fromAccountId = await AddAccountAsync(browser, "Everyday"), toAccountId = await AddAccountAsync(browser, "Savings"), amount = "900000",
            date = FirstOfThisMonth.ToString("yyyy-MM-dd"),
        });
        var transfer = (await created.Content.ReadFromJsonAsync<Identified>())!;

        (await browser.PostAsync($"/api/v1/transfers/{transfer.Id}/unmark", new
        {
            expenseCategoryId = await CategoryAsync(browser, "Transport"), incomeCategoryId = await CategoryAsync(browser, "Other income"),
        })).EnsureSuccessStatusCode();

        Assert.Equal(["warning"], (await ListAsync(browser)).Items.Select(item => item.State));
    }

    [Fact]
    public async Task A_user_reads_a_notification_and_then_all_of_them()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await BudgetAsync(browser, "Transport", "1000000");
        await BudgetAsync(browser, "Shopping", "1000000");
        await SpendAsync(browser, everyday, "Transport", "850000", FirstOfThisMonth);
        await SpendAsync(browser, everyday, "Shopping", "1200000", FirstOfThisMonth);
        var newest = (await ListAsync(browser)).Items[0];

        var read = await browser.PostAsync($"{Notifications}/{newest.Id}/read");

        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        var afterOne = await ListAsync(browser);
        Assert.Equal(1, afterOne.Unread);
        Assert.NotNull(afterOne.Items[0].ReadAt);
        Assert.Null(afterOne.Items[1].ReadAt);

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync($"{Notifications}/read")).StatusCode);
        var afterAll = await ListAsync(browser);
        Assert.Equal(0, afterAll.Unread);
        Assert.All(afterAll.Items, item => Assert.NotNull(item.ReadAt));
    }

    [Fact]
    public async Task Another_users_notification_cannot_be_seen_or_read()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        await BudgetAsync(alice, "Transport", "1000000");
        await SpendAsync(alice, await AddAccountAsync(alice, "Everyday"), "Transport", "850000", FirstOfThisMonth);
        var alices = Assert.Single((await ListAsync(alice)).Items);

        Assert.Empty((await ListAsync(bob)).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"{Notifications}/{alices.Id}/read")).StatusCode);
        Assert.Null(Assert.Single((await ListAsync(alice)).Items).ReadAt);
    }

    [Fact]
    public async Task Notifications_come_a_page_at_a_time()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await BudgetAsync(browser, "Transport", "1000000");
        await SpendAsync(browser, everyday, "Transport", "850000", FirstOfThisMonth);
        await SpendAsync(browser, everyday, "Transport", "200000", FirstOfThisMonth);

        var first = (await browser.Http.GetFromJsonAsync<ListBody>($"{Notifications}?limit=1"))!;
        var second = (await browser.Http.GetFromJsonAsync<ListBody>($"{Notifications}?limit=1&cursor={first.NextCursor}"))!;
        var wrong = await browser.Http.GetAsync($"{Notifications}?cursor=nonsense");

        Assert.Equal(["over"], first.Items.Select(item => item.State));
        Assert.Equal(["warning"], second.Items.Select(item => item.State));
        Assert.Null(second.NextCursor);
        Assert.Equal(["cursor"], await wrong.ErrorFieldsAsync());
    }

    private IReadOnlyList<Pfm.Application.Abstractions.EmailMessage> BudgetEmails(string email) =>
        [.. api.Emails.To(email).Where(message => message.Body.Contains($"{PfmApi.PublicUrl}/budgets/", StringComparison.Ordinal))];

    private static async Task<Guid> BudgetAsync(Browser browser, string category, string amount)
    {
        var categoryId = await CategoryAsync(browser, category);
        (await browser.PutAsync($"/api/v1/budgets/{ThisMonth}/{categoryId}", new { amount })).EnsureSuccessStatusCode();
        return categoryId;
    }

    private static async Task<string> TransactionRowVersionAsync(Browser browser, Guid id) =>
        $"\"{(await browser.Http.GetFromJsonAsync<Versioned>($"/api/v1/transactions/{id}"))!.RowVersion}\"";

    private static async Task<ListBody> ListAsync(Browser browser) => (await browser.Http.GetFromJsonAsync<ListBody>(Notifications))!;

    private sealed record Versioned(string RowVersion);

    private sealed record Identified(Guid Id);

    private sealed record ProfileBody(bool EmailBudgetNotifications);

    private sealed record CategoryBody(Guid Id, string Name);

    private sealed record NotificationBody(
        Guid Id, string Month, CategoryBody Category, string State, string Budgeted, string Spent, int? PercentSpent, string Currency,
        DateTimeOffset RaisedAt, DateTimeOffset? ReadAt);

    private sealed record ListBody(List<NotificationBody> Items, string? NextCursor, int Unread);

    private sealed record BudgetRow(string RowVersion);

    private sealed record MonthBody(List<BudgetRow> Items);
}
