using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Transactions;
using Pfm.Domain.Transactions;
using Pfm.Infrastructure.Persistence;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// The worker's nightly integrity check verifies that what was saved still adds up (spec sections 4 and 10). The
/// domain refuses to save anything that does not, so these tests break the rows in the database behind its
/// back, run the check the way the worker's job does, and read what it found for their own user: the check
/// reads every user, and other tests' users are saved at the same time.
/// </summary>
[Collection(ApiCollection.Name)]
public class IntegrityCheckTests(PfmApi api)
{
    private const string Transactions = "/api/v1/transactions";

    [Fact]
    public async Task Everything_a_user_records_adds_up_so_nothing_is_found_for_them()
    {
        var (browser, userId) = await SignedInUserAsync();
        await UserErasureFixtures.OwnSomethingOfEveryKindAsync(browser);
        var dollars = await AddAccountAsync(browser, "Dollars", "USD", "100");
        (await browser.PostAsync("/api/v1/transfers", new { fromAccountId = dollars, toAccountId = await AddAccountAsync(browser, "Dong"), amount = "40", toAmount = "1000000", date = "2026-11-05" }))
            .EnsureSuccessStatusCode();

        Assert.Empty(await DriftOfAsync(userId));
    }

    [Fact]
    public async Task A_split_whose_lines_no_longer_add_up_to_its_amount_is_found_with_its_user_and_both_amounts()
    {
        var (browser, userId) = await SignedInUserAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var split = await RecordAsync(browser, new
        {
            type = "expense", accountId = everyday, amount = "500000", date = "2026-11-03",
            lines = new object[] { new { categoryId = await CategoryAsync(browser, "Groceries"), amount = "320000" }, new { categoryId = await CategoryAsync(browser, "Household"), amount = "180000" } },
        });
        await BehindTheDomainsBackAsync($"""UPDATE "TransactionLines" SET "Amount" = 170000 WHERE "TransactionId" = {split} AND "Position" = 1""");

        var found = await DriftOfAsync(userId);

        Assert.Equal([new Drift.Lines(split, Amount: 500_000m, LinesTotal: 490_000m)], found);
    }

    [Fact]
    public async Task A_transfer_one_of_whose_legs_was_deleted_alone_is_found_with_both_legs()
    {
        var (browser, userId) = await SignedInUserAsync();
        var transfer = await TransferAsync(browser, await AddAccountAsync(browser, "Everyday"), await AddAccountAsync(browser, "Savings"));
        await BehindTheDomainsBackAsync($"""UPDATE "Transactions" SET "DeletedAt" = now() WHERE "Id" = {transfer.InTransactionId}""");

        var found = Assert.IsType<Drift.Transfer>(Assert.Single(await DriftOfAsync(userId)));

        Assert.Equal((transfer.Id, TransferDrift.OneLegDeleted), (found.TransferId, found.Problem));
        Assert.Equal(new HashSet<Guid> { transfer.OutTransactionId, transfer.InTransactionId }, found.LegIds.ToHashSet());
    }

    [Fact]
    public async Task A_transfer_between_accounts_in_one_currency_whose_legs_moved_different_amounts_is_found()
    {
        var (browser, userId) = await SignedInUserAsync();
        var transfer = await TransferAsync(browser, await AddAccountAsync(browser, "Everyday"), await AddAccountAsync(browser, "Savings"));
        await BehindTheDomainsBackAsync($"""UPDATE "Transactions" SET "Amount" = 1900000 WHERE "Id" = {transfer.InTransactionId}""");

        var found = Assert.IsType<Drift.Transfer>(Assert.Single(await DriftOfAsync(userId)));

        Assert.Equal((transfer.Id, TransferDrift.AmountsDiffer), (found.TransferId, found.Problem));
    }

    [Fact]
    public async Task A_transfer_whose_legs_are_on_different_dates_is_found()
    {
        var (browser, userId) = await SignedInUserAsync();
        var transfer = await TransferAsync(browser, await AddAccountAsync(browser, "Everyday"), await AddAccountAsync(browser, "Savings"));
        await BehindTheDomainsBackAsync($"""UPDATE "Transactions" SET "Date" = '2026-11-01' WHERE "Id" = {transfer.InTransactionId}""");

        var found = Assert.IsType<Drift.Transfer>(Assert.Single(await DriftOfAsync(userId)));

        Assert.Equal((transfer.Id, TransferDrift.DatesDiffer), (found.TransferId, found.Problem));
    }

    [Fact]
    public async Task A_transfer_whose_legs_are_on_one_account_is_found()
    {
        var (browser, userId) = await SignedInUserAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var transfer = await TransferAsync(browser, everyday, await AddAccountAsync(browser, "Savings"));
        await BehindTheDomainsBackAsync($"""UPDATE "Transactions" SET "AccountId" = {everyday} WHERE "Id" = {transfer.InTransactionId}""");

        var found = Assert.IsType<Drift.Transfer>(Assert.Single(await DriftOfAsync(userId)));

        Assert.Equal((transfer.Id, TransferDrift.OneAccount), (found.TransferId, found.Problem));
    }

    /// <summary>Runs the check as the worker's job does, and answers what it found for one user.</summary>
    private async Task<IReadOnlyList<Drift>> DriftOfAsync(Guid userId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<IntegrityCheck>().RunAsync();
        return [.. report.Drift.Where(found => found.UserId == userId).Select(found => found.Drift)];
    }

    private async Task BehindTheDomainsBackAsync(FormattableString sql)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PfmDbContext>().Database.ExecuteSqlAsync(sql);
    }

    private async Task<(Browser Browser, Guid UserId)> SignedInUserAsync()
    {
        var (browser, _) = await api.SignedInAsync();
        return (browser, (await browser.Http.GetFromJsonAsync<Identified>("/api/v1/me"))!.Id);
    }

    private static async Task<Guid> RecordAsync(Browser browser, object transaction)
    {
        var response = await browser.PostAsync(Transactions, transaction);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    private static async Task<TransferBody> TransferAsync(Browser browser, Guid from, Guid to)
    {
        var response = await browser.PostAsync("/api/v1/transfers", new { fromAccountId = from, toAccountId = to, amount = "2000000", date = "2026-11-02" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransferBody>())!;
    }

    private sealed record Identified(Guid Id);

    private sealed record TransferBody(Guid Id, Guid OutTransactionId, Guid InTransactionId);
}
