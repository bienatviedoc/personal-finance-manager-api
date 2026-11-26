using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Transactions;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// A deleted transaction can be restored for 30 days (spec section 3, rule 10): it is listed as recently deleted
/// until then, and the worker removes it for good after that.
/// </summary>
[Collection(ApiCollection.Name)]
public class RecentlyDeletedTests(PfmApi api)
{
    private const string Transactions = "/api/v1/transactions";
    private const string RecentlyDeleted = "/api/v1/transactions/deleted";

    [Fact]
    public async Task Deleted_transactions_are_listed_as_recently_deleted_last_deleted_first_with_when_they_can_be_restored_until()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(alice, "Everyday");
        var kept = await SpendAsync(alice, everyday, "Groceries", "100", new DateOnly(2026, 11, 1), "Co.opmart");
        var first = await SpendAsync(alice, everyday, "Groceries", "200", new DateOnly(2026, 11, 2), "Bach Hoa Xanh");
        var second = await SpendAsync(alice, everyday, "Groceries", "300", new DateOnly(2026, 10, 2));
        await DeleteAsync(alice, first);
        await DeleteAsync(alice, second);
        await DeleteAsync(bob, await SpendAsync(bob, await AddAccountAsync(bob, "Bob's"), "Groceries", "1", new DateOnly(2026, 11, 1)));

        var listed = (await alice.Http.GetFromJsonAsync<DeletedList>(RecentlyDeleted))!;

        Assert.Equal([second, first], listed.Items.Select(deleted => deleted.Id));
        Assert.DoesNotContain(kept, listed.Items.Select(deleted => deleted.Id));
        var item = listed.Items[1];
        Assert.Equal(("expense", everyday, "200.00", "Bach Hoa Xanh"), (item.Type, item.AccountId, item.Amount, item.Payee));
        Assert.InRange(DateTimeOffset.UtcNow - item.DeletedAt, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        Assert.Equal(item.DeletedAt.AddDays(30), item.RestorableUntil);
        Assert.Null(listed.NextCursor);
    }

    [Fact]
    public async Task Recently_deleted_comes_in_pages_each_pointing_to_the_next()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var deleted = new List<Guid>();
        foreach (var amount in new[] { "1", "2", "3" })
        {
            var id = await SpendAsync(browser, everyday, "Groceries", amount, new DateOnly(2026, 11, 1));
            await DeleteAsync(browser, id);
            deleted.Insert(0, id);
        }

        var firstPage = (await browser.Http.GetFromJsonAsync<DeletedList>($"{RecentlyDeleted}?limit=2"))!;
        var secondPage = (await browser.Http.GetFromJsonAsync<DeletedList>($"{RecentlyDeleted}?limit=2&cursor={firstPage.NextCursor}"))!;

        Assert.Equal(deleted, firstPage.Items.Concat(secondPage.Items).Select(item => item.Id));
        Assert.Null(secondPage.NextCursor);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync($"{RecentlyDeleted}?cursor=nonsense")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync($"{RecentlyDeleted}?limit=0")).StatusCode);
    }

    [Fact]
    public async Task Restoring_a_deleted_transaction_returns_it_to_the_list_and_the_balance_and_takes_it_out_of_recently_deleted()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var transaction = await SpendAsync(browser, everyday, "Groceries", "250000", new DateOnly(2026, 11, 2));
        await DeleteAsync(browser, transaction);

        var response = await browser.PostAsync($"{Transactions}/{transaction}/restore");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((transaction, "250000.00"), await IdAndAmountAsync(response));
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal("750000.00", await BalanceAsync(browser, everyday));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync($"{Transactions}/{transaction}")).StatusCode);
        Assert.Contains(transaction, (await browser.Http.GetFromJsonAsync<DeletedList>(Transactions))!.Items.Select(item => item.Id));
        Assert.Empty((await browser.Http.GetFromJsonAsync<DeletedList>(RecentlyDeleted))!.Items);
    }

    [Fact]
    public async Task Restoring_a_leg_of_a_deleted_transfer_restores_both_legs_and_both_balances()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday", "VND", "1000000");
        var savings = await AddAccountAsync(browser, "Savings", "VND", "0");
        await TransferAsync(browser, everyday, savings, "400000", new DateOnly(2026, 11, 2));
        var legs = (await browser.Http.GetFromJsonAsync<DeletedList>(Transactions))!.Items;
        await DeleteAsync(browser, legs[0].Id);
        Assert.Equal(2, (await browser.Http.GetFromJsonAsync<DeletedList>(RecentlyDeleted))!.Items.Length);

        var response = await browser.PostAsync($"{Transactions}/{legs.Single(leg => leg.Type == "transferIn").Id}/restore");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("600000.00", "400000.00"), (await BalanceAsync(browser, everyday), await BalanceAsync(browser, savings)));
        Assert.Equal(legs.Select(leg => leg.Id).Order(), (await browser.Http.GetFromJsonAsync<DeletedList>(Transactions))!.Items.Select(item => item.Id).Order());
        Assert.Empty((await browser.Http.GetFromJsonAsync<DeletedList>(RecentlyDeleted))!.Items);
    }

    [Fact]
    public async Task Only_a_transaction_of_the_user_deleted_in_the_last_30_days_can_be_restored()
    {
        var (alice, email) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(alice, "Everyday");
        var notDeleted = await SpendAsync(alice, everyday, "Groceries", "1", new DateOnly(2026, 11, 1));
        var deleted = await SpendAsync(alice, everyday, "Groceries", "2", new DateOnly(2026, 11, 1));
        await DeleteAsync(alice, deleted);

        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsync($"{Transactions}/{notDeleted}/restore")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsync($"{Transactions}/{Guid.CreateVersion7()}/restore")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"{Transactions}/{deleted}/restore")).StatusCode);

        // Thirty days and a minute later it is no longer listed, and no longer restored, even before the worker removes it.
        var later = PfmApi.CreateBrowser(api.After(TimeSpan.FromDays(30) + TimeSpan.FromMinutes(1)));
        (await later.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password })).EnsureSuccessStatusCode();
        Assert.Empty((await later.Http.GetFromJsonAsync<DeletedList>(RecentlyDeleted))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await later.PostAsync($"{Transactions}/{deleted}/restore")).StatusCode);
    }

    [Fact]
    public async Task Transactions_deleted_more_than_30_days_ago_are_removed_for_good_with_their_attachments_and_the_rest_are_kept()
    {
        var (browser, _) = await api.SignedInAsync();
        var userId = await IdOfAsync(browser);
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        var kept = await SpendAsync(browser, everyday, "Groceries", "1", new DateOnly(2026, 11, 1));
        var removed = await SpendAsync(browser, everyday, "Groceries", "2", new DateOnly(2026, 11, 1));
        var attached = await browser.PostFileAsync($"{Transactions}/{removed}/attachments", "receipt.pdf", "%PDF-1.7\n"u8.ToArray(), "application/pdf");
        var attachmentId = (await attached.Content.ReadFromJsonAsync<Identified>())!.Id;
        var receipt = (await browser.Http.GetFromJsonAsync<SignedUrl>($"/api/v1/attachments/{attachmentId}"))!.Url;
        await TransferAsync(browser, everyday, savings, "3", new DateOnly(2026, 11, 1));
        var transferLeg = (await browser.Http.GetFromJsonAsync<DeletedList>($"{Transactions}?type=transferOut"))!.Items.Single().Id;
        await DeleteAsync(browser, removed);
        await DeleteAsync(browser, transferLeg);

        await api.PurgeDeletedTransactionsAsync(after: TimeSpan.FromDays(30) - TimeSpan.FromMinutes(1));
        Assert.Equal(new Dictionary<string, int> { ["Transactions"] = 4, ["TransactionLines"] = 2, ["Attachments"] = 1 }, await TransactionRowsAsync(userId));

        await api.PurgeDeletedTransactionsAsync(after: TimeSpan.FromDays(30) + TimeSpan.FromMinutes(1));

        Assert.Equal(new Dictionary<string, int> { ["Transactions"] = 1, ["TransactionLines"] = 1, ["Attachments"] = 0 }, await TransactionRowsAsync(userId));
        Assert.Equal([kept], (await browser.Http.GetFromJsonAsync<DeletedList>(Transactions))!.Items.Select(item => item.Id));
        using var anyone = new HttpClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync(receipt)).StatusCode);
    }

    private async Task<Dictionary<string, int>> TransactionRowsAsync(Guid userId)
    {
        var rows = await api.RowsOwnedByAsync(userId);
        return new[] { "Transactions", "TransactionLines", "Attachments" }.ToDictionary(table => table, table => rows.GetValueOrDefault(table));
    }

    private static async Task DeleteAsync(Browser browser, Guid transactionId) =>
        (await browser.DeleteAsync($"{Transactions}/{transactionId}")).EnsureSuccessStatusCode();

    private static async Task<(Guid, string)> IdAndAmountAsync(HttpResponseMessage response)
    {
        var body = (await response.Content.ReadFromJsonAsync<DeletedItem>())!;
        return (body.Id, body.Amount);
    }

    private static async Task<Guid> IdOfAsync(Browser browser) =>
        (await browser.Http.GetFromJsonAsync<Identified>("/api/v1/me"))!.Id;

    private sealed record Identified(Guid Id);

    private sealed record SignedUrl(Uri Url);

    private sealed record DeletedList(DeletedItem[] Items, string? NextCursor);

    private sealed record DeletedItem(
        Guid Id, string Type, Guid AccountId, string Amount, string? Payee, Guid? TransferId, DateTimeOffset DeletedAt, DateTimeOffset RestorableUntil);
}
