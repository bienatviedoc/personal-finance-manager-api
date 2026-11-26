using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Application.Abstractions;
using Pfm.Application.Identity;

namespace Pfm.Api.IntegrationTests;

/// <summary>What tests about erasing deactivated users need from the running API.</summary>
public static class UserErasureFixtures
{
    extension(PfmApi api)
    {
        /// <summary>
        /// Erases the users whose 30 days are over the way the worker's job does, as it would this much later.
        /// Answers how many it erased.
        /// </summary>
        public async Task<int> EraseDeactivatedUsersAsync(TimeSpan after)
        {
            await using var scope = api.After(after).Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<EraseDeactivatedUsersHandler>().HandleAsync(new EraseDeactivatedUsersCommand());
            return result.Outcome.Users;
        }

        /// <summary>
        /// Removes for good the transactions deleted more than 30 days ago the way the worker's job does, as it would
        /// this much later. Answers how many it removed.
        /// </summary>
        public async Task<int> PurgeDeletedTransactionsAsync(TimeSpan after)
        {
            await using var scope = api.After(after).Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<Pfm.Application.Transactions.DeletedTransactionPurge>().RunAsync()).Transactions;
        }

        /// <summary>How many rows a user owns in each table that holds any of theirs, as a job acting for them reads it.</summary>
        public async Task<Dictionary<string, int>> RowsOwnedByAsync(Guid userId)
        {
            await using var scope = api.Services.CreateAsyncScope();
            using var acting = scope.ServiceProvider.GetRequiredService<IActForUser>().ActFor(userId);
            var tables = await scope.ServiceProvider.GetRequiredService<IUserData>().ReadEverythingAsync(CancellationToken.None);
            return tables.Where(table => table.Rows.Count > 0).ToDictionary(table => table.Name, table => table.Rows.Count);
        }
    }

    /// <summary>
    /// Gives the user signed in on this browser something of every kind a user can own, and answers how many
    /// rows of each table that comes to. A new kind of user data belongs here, so that the export and the
    /// erasure are tested on it.
    /// </summary>
    public static async Task<Dictionary<string, int>> OwnSomethingOfEveryKindAsync(Browser browser)
    {
        var institutionId = await BrowserFixtures.AddInstitutionAsync(browser, "Vietcombank");
        var accountId = await BrowserFixtures.AddAccountAsync(browser, "Everyday");
        (await browser.PutAsync($"/api/v1/accounts/{accountId}", new { name = "Everyday", type = "current", currency = "VND", openingBalance = "1500000", openingDate = "2026-01-01", institutionId }, await RowVersionAsync(browser, accountId))).EnsureSuccessStatusCode();
        var food = await BrowserFixtures.CategoryAsync(browser, "Food & drink");
        (await browser.PostAsync("/api/v1/categories", new { parentId = food, name = "Street food", kind = "expense", icon = "utensils", color = "#ef4444" })).EnsureSuccessStatusCode();
        var streetFood = await BrowserFixtures.CategoryAsync(browser, "Street food");
        var lunch = await RecordAsync(browser, accountId, streetFood, "45000", "Bánh mì Huỳnh Hoa");
        (await browser.PostFileAsync($"/api/v1/transactions/{lunch}/attachments", ReceiptName, Receipt, "application/pdf")).EnsureSuccessStatusCode();
        var deleted = await RecordAsync(browser, accountId, food, "120000", "Phở Thìn");
        (await browser.DeleteAsync($"/api/v1/transactions/{deleted}")).EnsureSuccessStatusCode();
        await RecordAsync(browser, accountId, streetFood, "15000", "Bánh mì Huỳnh Hoa", isRefund: true);
        (await browser.PostAsync("/api/v1/transactions", new
        {
            type = "expense", accountId, amount = "500000", date = "2026-11-03", payee = "Co.opmart", tags = new[] { "tet-2027", "groceries" },
            lines = new object[] { new { categoryId = food, amount = "320000", note = "Rice" }, new { categoryId = streetFood, amount = "180000" } },
        })).EnsureSuccessStatusCode();
        // Set below what November already spent, so it raises a budget notification.
        (await browser.PutAsync($"/api/v1/budgets/2026-11/{food}", new { amount = "300000" })).EnsureSuccessStatusCode();

        // Due far in the future, so it posts nothing meanwhile.
        (await browser.PostAsync("/api/v1/recurring-rules", new { type = "expense", accountId, amount = "9000000", categoryId = food, payee = "Landlord", frequency = "monthly", startDate = "2099-01-31" })).EnsureSuccessStatusCode();

        // A completed reconciliation, which locks the lunch it ticked: the transaction references it.
        var started = await browser.PostAsync($"/api/v1/accounts/{accountId}/reconciliations", new { statementDate = "2026-11-02", statementBalance = "1455000" });
        var reconciliation = (await started.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<Identified>())!.Id;
        (await browser.PutAsync($"/api/v1/reconciliations/{reconciliation}/transactions/{lunch}", new { })).EnsureSuccessStatusCode();
        (await browser.PostAsync($"/api/v1/reconciliations/{reconciliation}/complete")).EnsureSuccessStatusCode();

        // A transfer, recorded last: its two legs are the last two transactions, and have no lines.
        var savingsId = await BrowserFixtures.AddAccountAsync(browser, "Savings");
        (await browser.PostAsync("/api/v1/transfers", new { fromAccountId = accountId, toAccountId = savingsId, amount = "200000", date = "2026-11-04", note = "For Tet" }))
            .EnsureSuccessStatusCode();

        var categories = (await browser.Http.GetFromJsonAsync<CategoryTree>("/api/v1/categories"))!.Items;
        return new()
        {
            ["Accounts"] = 2,
            ["Attachments"] = 1,
            ["BudgetNotifications"] = 1,
            ["Budgets"] = 1,
            ["Categories"] = categories.Length + categories.Sum(category => category.Subcategories.Length),
            ["Institutions"] = 1,
            ["Reconciliations"] = 1,
            ["RecurringRules"] = 1,
            ["Tags"] = 2,
            // One line for each income or expense, and two for the split; none for the legs of the transfer.
            ["TransactionLines"] = 5,
            ["Transactions"] = 6,

            // The split's two tags.
            ["TransactionTags"] = 2,
        };
    }

    /// <summary>The receipt <see cref="OwnSomethingOfEveryKindAsync"/> attaches to a transaction: its name, and the file itself.</summary>
    public const string ReceiptName = "Bánh mì.pdf";

    public static readonly byte[] Receipt = "%PDF-1.7\n% a receipt for one banh mi\n"u8.ToArray();

    private static async Task<Guid> RecordAsync(Browser browser, Guid accountId, Guid categoryId, string amount, string payee, bool isRefund = false)
    {
        var response = await browser.PostAsync("/api/v1/transactions", new { type = "expense", accountId, amount, date = "2026-11-02", categoryId, payee, isRefund, note = "Lunch, with \"extra\" chilli" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    private static async Task<string> RowVersionAsync(Browser browser, Guid accountId) =>
        $"\"{(await browser.Http.GetFromJsonAsync<Versioned>($"/api/v1/accounts/{accountId}"))!.RowVersion}\"";

    private sealed record Identified(Guid Id);

    private sealed record Versioned(string RowVersion);

    private sealed record CategoryNode(Guid Id, CategoryNode[] Subcategories);

    private sealed record CategoryTree(CategoryNode[] Items);
}
