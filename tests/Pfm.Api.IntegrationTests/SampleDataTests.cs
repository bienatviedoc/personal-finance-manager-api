using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Transactions;
using Pfm.Infrastructure.SampleData;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// The seed script fills a local environment with a year of sample data for one user (spec section 13), the way
/// <c>Pfm.Api seed</c> does. What it made is read back over HTTP, signed in as that user.
/// </summary>
[Collection(ApiCollection.Name)]
public class SampleDataTests(PfmApi api)
{
    [Fact]
    public async Task The_seeded_user_signs_in_to_accounts_categories_a_budget_in_every_month_and_a_year_of_transactions()
    {
        var email = PfmApi.NewEmail();

        await api.Services.SeedSampleDataAsync(new SampleDataOptions(email, PfmApi.Password));

        var browser = await SignInAsync(email);
        var accounts = (await browser.Http.GetFromJsonAsync<ItemsBody<NamedBody>>("/api/v1/accounts"))!;
        Assert.Equal(["Cash", "Credit card", "Everyday", "Savings"], accounts.Items.Select(account => account.Name).Order());
        Assert.NotEmpty((await browser.Http.GetFromJsonAsync<ItemsBody<NamedBody>>("/api/v1/categories"))!.Items);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var monthsAgo in Enumerable.Range(0, 12))
        {
            var month = today.AddMonths(-monthsAgo).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            Assert.NotEmpty((await browser.Http.GetFromJsonAsync<ItemsBody<NamedBody>>($"/api/v1/budgets?month={month}"))!.Items);
        }

        var oldest = await OldestDateAsync(browser);
        Assert.InRange(oldest, today.AddYears(-1), today.AddYears(-1).AddDays(14));
    }

    [Fact]
    public async Task Asked_for_a_number_of_transactions_the_seeded_user_has_exactly_that_many_all_within_the_year()
    {
        var email = PfmApi.NewEmail();

        var seeded = await api.Services.SeedSampleDataAsync(new SampleDataOptions(email, PfmApi.Password, Transactions: 5_000));

        var browser = await SignInAsync(email);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var all = (await browser.Http.GetFromJsonAsync<ListBody>("/api/v1/transactions?limit=1"))!;
        Assert.Equal((5_000, 5_000), (seeded.Transactions, all.Total.Count));
        var withinTheYear = (await browser.Http.GetFromJsonAsync<ListBody>($"/api/v1/transactions?limit=1&from={seeded.From:yyyy-MM-dd}&to={seeded.To:yyyy-MM-dd}"))!;
        Assert.Equal(5_000, withinTheYear.Total.Count);
        Assert.InRange(seeded.From, today.AddYears(-1), today.AddYears(-1).AddDays(2));
    }

    [Fact]
    public async Task What_is_seeded_adds_up_so_the_integrity_check_finds_nothing_for_the_user()
    {
        var seeded = await api.Services.SeedSampleDataAsync(new SampleDataOptions(PfmApi.NewEmail(), PfmApi.Password));

        await using var scope = api.Services.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<IntegrityCheck>().RunAsync();
        Assert.DoesNotContain(report.Drift, found => found.UserId == seeded.UserId);
    }

    [Fact]
    public async Task The_seeded_spending_shows_in_the_budget_month()
    {
        var email = PfmApi.NewEmail();
        await api.Services.SeedSampleDataAsync(new SampleDataOptions(email, PfmApi.Password));

        var browser = await SignInAsync(email);
        var lastMonth = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var month = (await browser.Http.GetFromJsonAsync<BudgetMonthBody>($"/api/v1/budgets?month={lastMonth}"))!;
        Assert.True(decimal.Parse(month.Totals.Spent, CultureInfo.InvariantCulture) > 0, $"Spent {month.Totals.Spent} in {lastMonth}");
    }

    [Fact]
    public async Task Seeding_an_email_that_already_has_a_user_is_refused_and_leaves_that_user_as_they_were()
    {
        var (existing, email) = await api.SignedInAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.Services.SeedSampleDataAsync(new SampleDataOptions(email, "another password entirely")));

        Assert.Empty((await existing.Http.GetFromJsonAsync<ItemsBody<NamedBody>>("/api/v1/accounts"))!.Items);
        await SignInAsync(email);
    }

    [Fact]
    public void The_seed_command_reads_who_to_seed_and_how_many_transactions_and_seeds_the_sample_user_by_default()
    {
        Assert.Equal(
            new SampleDataOptions("me@example.com", "a long password here", 50_000),
            SampleDataCommand.Read(["--email", "me@example.com", "--transactions", "50000", "--password", "a long password here"], out _));
        Assert.Equal(
            new SampleDataOptions(SampleDataOptions.DefaultEmail, SampleDataOptions.DefaultPassword),
            SampleDataCommand.Read([], out _));
        Assert.Null(SampleDataCommand.Read(["--transactions", "lots"], out var problem));
        Assert.Contains("--transactions", problem, StringComparison.Ordinal);
        Assert.Null(SampleDataCommand.Read(["--colour", "red"], out _));
        Assert.Null(SampleDataCommand.Read(["--email"], out _));
    }

    private async Task<Browser> SignInAsync(string email)
    {
        var browser = api.CreateBrowser();
        (await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password })).EnsureSuccessStatusCode();
        return browser;
    }

    private static async Task<DateOnly> OldestDateAsync(Browser browser) =>
        (await browser.Http.GetFromJsonAsync<ItemsBody<DatedBody>>("/api/v1/transactions?sort=date&order=asc&limit=1"))!.Items.Single().Date;

    private sealed record ItemsBody<T>(IReadOnlyList<T> Items);

    private sealed record NamedBody(string Name);

    private sealed record DatedBody(DateOnly Date);

    private sealed record ListBody(TotalBody Total);

    private sealed record TotalBody(int Count);

    private sealed record BudgetMonthBody(BudgetTotalsBody Totals);

    private sealed record BudgetTotalsBody(string Spent);
}
