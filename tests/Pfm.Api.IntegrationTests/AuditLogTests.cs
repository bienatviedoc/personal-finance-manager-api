using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pfm.Application.Abstractions;
using Pfm.Domain.Identity;
using Pfm.Infrastructure.Persistence;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// The audit log (spec section 8, Auditability; section 10, AuditEntry): sign-ins, import commits, bulk edits and
/// deletes each leave an entry the user can read in settings, and nobody can change or remove.
/// </summary>
[Collection(ApiCollection.Name)]
public class AuditLogTests(PfmApi api)
{
    private const string AuditLog = "/api/v1/me/audit-log";
    private const string Bulk = "/api/v1/transactions/bulk";
    private static readonly DateOnly November2 = new(2026, 11, 2);

    [Fact]
    public async Task Signing_in_leaves_an_audit_entry_naming_the_user_when_and_a_short_summary()
    {
        var before = DateTimeOffset.UtcNow;
        var (browser, _) = await api.SignedInAsync();
        var me = (await browser.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();

        var entry = Assert.Single((await ListAsync(browser)).Items);

        Assert.Equal(("signIn", "user", me), (entry.Action, entry.EntityType, entry.EntityId));
        Assert.InRange(entry.At, before, DateTimeOffset.UtcNow);
        Assert.Equal("Signed in with a password.", entry.Summary);
    }

    [Fact]
    public async Task A_sign_in_with_two_factors_says_which_second_factor_was_used()
    {
        var (browser, email) = await api.SignedInAsync();
        var (key, recoveryCodes) = await api.TurnOnTwoFactorAsync(browser);

        (await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, twoFactorCode = Totp.CodeFor(key) })).EnsureSuccessStatusCode();
        (await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, recoveryCode = recoveryCodes[0] })).EnsureSuccessStatusCode();

        Assert.Equal(
            ["Signed in with a password and a recovery code.", "Signed in with a password and an authenticator code.", "Signed in with a password."],
            (await ListAsync(browser)).Items.Select(entry => entry.Summary));
    }

    [Fact]
    public async Task A_sign_in_that_cancels_the_users_deletion_says_so_in_the_users_locale()
    {
        var (browser, email) = await api.SignedInAsync(locale: "vi");
        (await browser.PostAsync("/api/v1/me/deletion", new { password = PfmApi.Password })).EnsureSuccessStatusCode();

        var again = api.CreateBrowser();
        (await again.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password })).EnsureSuccessStatusCode();

        Assert.Equal("Đã đăng nhập bằng mật khẩu. Việc đăng nhập đã hủy yêu cầu xóa hồ sơ.", (await ListAsync(again)).Items[0].Summary);
    }

    [Fact]
    public async Task A_sign_in_refused_leaves_no_entry()
    {
        var (browser, email) = await api.SignedInAsync();

        await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = "not the password" });

        Assert.Single((await ListAsync(browser)).Items);
    }

    [Fact]
    public async Task The_log_is_read_a_page_at_a_time_the_newest_first()
    {
        var (browser, email) = await api.SignedInAsync();
        for (var i = 0; i < 2; i++)
        {
            (await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password })).EnsureSuccessStatusCode();
        }

        var all = (await ListAsync(browser)).Items;
        var first = await ListAsync(browser, "?limit=2");
        var second = await ListAsync(browser, $"?limit=2&cursor={first.NextCursor}");

        Assert.Equal(3, all.Length);
        Assert.Equal(all.Select(entry => entry.At).OrderDescending(), all.Select(entry => entry.At));
        Assert.Equal(all.Select(entry => entry.Id), first.Items.Concat(second.Items).Select(entry => entry.Id));
        Assert.Null(second.NextCursor);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync($"{AuditLog}?cursor=not-a-cursor")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync($"{AuditLog}?limit=0")).StatusCode);
    }

    [Fact]
    public async Task The_log_needs_a_signed_in_user_and_has_no_way_to_change_or_remove_an_entry()
    {
        var (browser, _) = await api.SignedInAsync();
        var entry = (await ListAsync(browser)).Items[0];

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateBrowser().GetAsync(AuditLog)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await browser.DeleteAsync(AuditLog)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.DeleteAsync($"{AuditLog}/{entry.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PutAsync($"{AuditLog}/{entry.Id}", new { summary = "Nothing happened." })).StatusCode);
    }

    [Fact]
    public async Task Persistence_refuses_to_save_a_change_or_delete_of_an_entry()
    {
        var (browser, _) = await api.SignedInAsync();
        var id = (await ListAsync(browser)).Items[0].Id;
        var userId = (await browser.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();

        await using var scope = api.Services.CreateAsyncScope();
        using var acting = scope.ServiceProvider.GetRequiredService<IActForUser>().ActFor(userId);
        var db = scope.ServiceProvider.GetRequiredService<PfmDbContext>();
        var entry = await db.Set<AuditEntry>().SingleAsync(row => row.Id == id);
        db.Entry(entry).Property(row => row.Summary).CurrentValue = "Nothing happened.";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        db.Remove(await db.Set<AuditEntry>().SingleAsync(row => row.Id == id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await Assert.ThrowsAsync<PostgresException>(() => db.Set<AuditEntry>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Summary, "Nothing happened.")));
    }

    [Fact]
    public async Task The_database_refuses_to_change_or_delete_an_entry_of_a_user_who_is_not_being_erased()
    {
        var (browser, _) = await api.SignedInAsync();
        var id = (await ListAsync(browser)).Items[0].Id;

        await using var connection = new NpgsqlConnection(api.ConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[]
                 {
                     """UPDATE "AuditEntries" SET "Summary" = 'Nothing happened.' WHERE "Id" = @id""",
                     """DELETE FROM "AuditEntries" WHERE "Id" = @id""",
                     """TRUNCATE "AuditEntries" """,
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", id);
            await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }

        Assert.Equal(id, (await ListAsync(browser)).Items[0].Id);
    }

    [Fact]
    public async Task The_database_refuses_to_delete_an_entry_of_a_user_who_asked_to_be_deleted_until_erasing_them_removes_it_with_them()
    {
        var (browser, _) = await api.SignedInAsync();
        var userId = (await browser.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var id = (await ListAsync(browser)).Items[0].Id;
        (await browser.PostAsync("/api/v1/me/deletion", new { password = PfmApi.Password })).EnsureSuccessStatusCode();

        await using (var connection = new NpgsqlConnection(api.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""DELETE FROM "AuditEntries" WHERE "Id" = @id""", connection);
            command.Parameters.AddWithValue("id", id);
            await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }

        Assert.Contains("AuditEntries", (await api.RowsOwnedByAsync(userId)).Keys);
        await api.EraseDeactivatedUsersAsync(after: TimeSpan.FromDays(31));
        Assert.Empty(await api.RowsOwnedByAsync(userId));
    }

    [Fact]
    public async Task A_bulk_edit_leaves_one_entry_saying_what_it_did_to_how_many_transactions()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        var first = await SpendAsync(browser, everyday, "Groceries", "1000", November2);
        var second = await SpendAsync(browser, everyday, "Transport", "2000", November2);
        var both = new[] { first, second };

        await BulkAsync(browser, new { action = "setCategory", transactionIds = both, categoryId = await CategoryAsync(browser, "Eating out") });
        await BulkAsync(browser, new { action = "addTag", transactionIds = both, tag = "Tet-2027" });
        await BulkAsync(browser, new { action = "removeTag", transactionIds = new[] { first }, tag = "tet-2027" });
        await BulkAsync(browser, new { action = "moveToAccount", transactionIds = both, accountId = savings });

        var entries = (await ListAsync(browser)).Items.Take(4).Reverse().ToList();
        Assert.All(entries, entry => Assert.Equal(("bulkEdit", "transaction", (Guid?)null), (entry.Action, entry.EntityType, entry.EntityId)));
        Assert.Equal(
            [
                "Put 2 transactions on the category Eating out.",
                "Added the tag tet-2027 to 2 transactions.",
                "Removed the tag tet-2027 from 1 transaction.",
                "Moved 2 transactions to the account Savings.",
            ],
            entries.Select(entry => entry.Summary));
    }

    [Fact]
    public async Task A_bulk_delete_leaves_one_entry_counting_the_legs_of_the_transfers_it_took_with_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await SpendAsync(browser, everyday, "Groceries", "1000", November2);
        await TransferAsync(browser, everyday, await AddAccountAsync(browser, "Savings"), "500", November2);
        var leg = (await browser.Http.GetFromJsonAsync<JsonElement>("/api/v1/transactions?type=transferOut")).GetProperty("items")[0].GetProperty("id").GetGuid();
        (await browser.PatchAsync("/api/v1/me", new { locale = "vi" })).EnsureSuccessStatusCode();

        await BulkAsync(browser, new { action = "delete", transactionIds = new[] { groceries, leg } });

        var entry = (await ListAsync(browser)).Items[0];
        Assert.Equal(("bulkDelete", "transaction", (Guid?)null, "Đã xóa 3 giao dịch."), (entry.Action, entry.EntityType, entry.EntityId, entry.Summary));
    }

    [Fact]
    public async Task A_bulk_action_refused_leaves_no_entry()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var salary = await RecordAsync(browser, "income", everyday, "Salary", "5000", November2);

        var response = await browser.PostAsync(Bulk, new { action = "setCategory", transactionIds = new[] { salary }, categoryId = await CategoryAsync(browser, "Eating out") });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(["signIn"], (await ListAsync(browser)).Items.Select(entry => entry.Action));
    }

    [Fact]
    public async Task A_bulk_action_whose_filter_matches_nothing_leaves_no_entry()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");

        await BulkAsync(browser, new { action = "delete", filter = new { accountId = everyday } });

        Assert.Equal(["signIn"], (await ListAsync(browser)).Items.Select(entry => entry.Action));
    }

    [Fact]
    public async Task Deleting_a_transaction_leaves_an_entry_naming_it_with_its_amount_and_date()
    {
        var (browser, _) = await api.SignedInAsync();
        var lunch = await SpendAsync(browser, await AddAccountAsync(browser, "Everyday"), "Groceries", "45000", November2);

        (await browser.DeleteAsync($"/api/v1/transactions/{lunch}")).EnsureSuccessStatusCode();

        var entry = (await ListAsync(browser)).Items[0];
        Assert.Equal(("delete", "transaction", (Guid?)lunch, "Deleted a transaction of ₫45,000 dated 2026-11-02."), (entry.Action, entry.EntityType, entry.EntityId, entry.Summary));
    }

    [Fact]
    public async Task Deleting_a_transfer_or_one_of_its_legs_leaves_an_entry_naming_the_transfer()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        await TransferAsync(browser, everyday, savings, "200000", November2);
        await TransferAsync(browser, everyday, savings, "300000", November2);
        var legs = (await browser.Http.GetFromJsonAsync<JsonElement>("/api/v1/transactions?type=transferOut")).GetProperty("items").EnumerateArray()
            .Select(leg => (Id: leg.GetProperty("id").GetGuid(), TransferId: leg.GetProperty("transferId").GetGuid(), Amount: leg.GetProperty("amount").GetString()))
            .OrderBy(leg => leg.Amount, StringComparer.Ordinal)
            .ToList();

        (await browser.DeleteAsync($"/api/v1/transfers/{legs[0].TransferId}")).EnsureSuccessStatusCode();
        (await browser.DeleteAsync($"/api/v1/transactions/{legs[1].Id}")).EnsureSuccessStatusCode();

        Assert.Equal(
            [
                ("delete", "transfer", (Guid?)legs[1].TransferId, "Deleted a transfer of ₫300,000 dated 2026-11-02."),
                ("delete", "transfer", (Guid?)legs[0].TransferId, "Deleted a transfer of ₫200,000 dated 2026-11-02."),
            ],
            (await ListAsync(browser)).Items.Take(2).Select(entry => (entry.Action, entry.EntityType, entry.EntityId, entry.Summary)));
    }

    [Fact]
    public async Task Committing_a_statement_leaves_one_entry_naming_the_batch_and_its_file_and_none_of_its_text()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "Techcombank");
        var upload = await browser.PostFileAsync(
            "/api/v1/imports", "august.csv", "Date;Details;Amount\n02-08-2026;HIGHLANDS COFFEE;-55.000,00\n06-08-2026;GRABFOOD HCM;-146.000,00\n"u8.ToArray(),
            new Dictionary<string, string> { ["accountId"] = account.ToString() });
        var batch = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var commit = new
        {
            mapping = new { separator = "semicolon", skipFirstRows = 0, hasHeaderRow = true, skipLastRows = 0, dateColumn = 0, descriptionColumn = 1, amountColumn = 2, dateFormat = "dayMonthYear", decimalSeparator = "comma" },
            rows = new[] { 2, 3 },
            expenseCategoryId = await CategoryAsync(browser, "Food & drink"),
        };

        Assert.Equal(HttpStatusCode.Accepted, (await browser.PostAsync($"/api/v1/imports/{batch}/commit", commit)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsync($"/api/v1/imports/{batch}/commit", commit)).StatusCode);

        var entries = (await ListAsync(browser)).Items;
        Assert.Equal(["importCommit", "signIn"], entries.Select(entry => entry.Action));
        Assert.Equal(("importBatch", (Guid?)batch, "Committed the statement august.csv: 2 rows to import."), (entries[0].EntityType, entries[0].EntityId, entries[0].Summary));
    }

    [Fact]
    public async Task Undoing_an_import_leaves_an_entry_naming_the_batch_its_file_and_how_many_transactions_it_deleted()
    {
        var (browser, _) = await api.SignedInAsync();
        var batch = await CommitStatementAsync(browser);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            while ((await scope.ServiceProvider.GetRequiredService<Pfm.Application.Imports.ImportCommits>().ImportNextAsync()).Rows > 0)
            {
            }
        }

        Assert.Equal(HttpStatusCode.OK, (await browser.DeleteAsync($"/api/v1/imports/{batch}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.DeleteAsync($"/api/v1/imports/{batch}")).StatusCode);

        var entries = (await ListAsync(browser)).Items;
        Assert.Equal(["importUndo", "importCommit", "signIn"], entries.Select(entry => entry.Action));
        Assert.Equal(("importBatch", (Guid?)batch, "Undid the import of the statement august.csv: 2 transactions removed."), (entries[0].EntityType, entries[0].EntityId, entries[0].Summary));
    }

    [Fact]
    public async Task Removing_an_attachment_leaves_an_entry_naming_it_and_its_file()
    {
        var (browser, _) = await api.SignedInAsync();
        var lunch = await SpendAsync(browser, await AddAccountAsync(browser, "Everyday"), "Groceries", "45000", November2);
        var added = await browser.PostFileAsync($"/api/v1/transactions/{lunch}/attachments", "receipt.pdf", "%PDF-1.7\n% a receipt\n"u8.ToArray(), "application/pdf");
        var attachment = (await added.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await browser.DeleteAsync($"/api/v1/attachments/{attachment}")).EnsureSuccessStatusCode();

        var entry = (await ListAsync(browser)).Items[0];
        Assert.Equal(("delete", "attachment", (Guid?)attachment, "Removed the attachment receipt.pdf from a transaction."), (entry.Action, entry.EntityType, entry.EntityId, entry.Summary));
    }

    [Fact]
    public async Task Removing_a_budget_or_an_overall_budget_leaves_an_entry_naming_its_month_and_amount()
    {
        var (browser, _) = await api.SignedInAsync();
        var groceries = await CategoryAsync(browser, "Groceries");
        (await browser.PutAsync($"/api/v1/budgets/2026-11/{groceries}", new { amount = "300000" })).EnsureSuccessStatusCode();
        (await browser.PutAsync("/api/v1/budgets/2026-11/overall", new { amount = "5000000" })).EnsureSuccessStatusCode();

        (await browser.DeleteAsync($"/api/v1/budgets/2026-11/{groceries}")).EnsureSuccessStatusCode();
        (await browser.DeleteAsync("/api/v1/budgets/2026-11/overall")).EnsureSuccessStatusCode();

        var entries = (await ListAsync(browser)).Items.Take(2).ToList();
        Assert.All(entries, entry => Assert.Equal(("delete", "budget"), (entry.Action, entry.EntityType)));
        Assert.All(entries, entry => Assert.NotNull(entry.EntityId));
        Assert.Equal(
            ["Removed the overall budget of ₫5,000,000 for 2026-11.", "Removed the budget of ₫300,000 on Groceries for 2026-11."],
            entries.Select(entry => entry.Summary));
    }

    [Fact]
    public async Task Deleting_a_recurring_rule_leaves_an_entry_naming_it_with_its_amount_and_payee()
    {
        var (browser, _) = await api.SignedInAsync();
        var accountId = await AddAccountAsync(browser, "Everyday");
        var created = await browser.PostAsync("/api/v1/recurring-rules", new
        {
            type = "expense", accountId, amount = "9000000", categoryId = await CategoryAsync(browser, "Rent"), payee = "Landlord", frequency = "monthly",
            startDate = "2099-01-31",
        });
        var rule = (await created.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await browser.DeleteAsync($"/api/v1/recurring-rules/{rule}")).EnsureSuccessStatusCode();

        var entry = (await ListAsync(browser)).Items[0];
        Assert.Equal(("delete", "recurringRule", (Guid?)rule, "Deleted a recurring expense of ₫9,000,000 (Landlord)."), (entry.Action, entry.EntityType, entry.EntityId, entry.Summary));
    }

    [Fact]
    public async Task Deleting_a_categorisation_rule_leaves_an_entry_naming_what_it_matched()
    {
        var (browser, _) = await api.SignedInAsync();
        var rule = await CategorisationRuleTests.AddAsync(browser, "grab", "contains", await CategoryAsync(browser, "Transport"));
        (await browser.PatchAsync("/api/v1/me", new { locale = "vi" })).EnsureSuccessStatusCode();

        (await browser.DeleteAsync($"/api/v1/category-rules/{rule.Id}")).EnsureSuccessStatusCode();

        var entry = (await ListAsync(browser)).Items[0];
        Assert.Equal(("delete", "categorisationRule", (Guid?)rule.Id, "Đã xóa quy tắc phân loại cho \"grab\"."), (entry.Action, entry.EntityType, entry.EntityId, entry.Summary));
    }

    [Fact]
    public async Task Asking_to_delete_the_user_leaves_an_entry_saved_with_the_deactivation()
    {
        var (browser, email) = await api.SignedInAsync();
        var userId = (await browser.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();

        (await browser.PostAsync("/api/v1/me/deletion", new { password = PfmApi.Password })).EnsureSuccessStatusCode();
        (await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password })).EnsureSuccessStatusCode();

        var entries = (await ListAsync(browser)).Items;
        Assert.Equal(["signIn", "deletionRequest", "signIn"], entries.Select(entry => entry.Action));
        Assert.Equal(
            ("user", (Guid?)userId, "Asked for their user to be deleted: everything of theirs is erased after 30 days unless they sign in again."),
            (entries[1].EntityType, entries[1].EntityId, entries[1].Summary));
    }

    [Fact]
    public async Task Asking_to_delete_the_user_with_the_wrong_password_leaves_no_entry()
    {
        var (browser, _) = await api.SignedInAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/api/v1/me/deletion", new { password = "not the right password" })).StatusCode);

        Assert.Equal(["signIn"], (await ListAsync(browser)).Items.Select(entry => entry.Action));
    }

    private static async Task<Guid> CommitStatementAsync(Browser browser)
    {
        var account = await AddAccountAsync(browser, "Techcombank");
        var upload = await browser.PostFileAsync(
            "/api/v1/imports", "august.csv", "Date;Details;Amount\n02-08-2026;HIGHLANDS COFFEE;-55.000,00\n06-08-2026;GRABFOOD HCM;-146.000,00\n"u8.ToArray(),
            new Dictionary<string, string> { ["accountId"] = account.ToString() });
        var batch = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var commit = new
        {
            mapping = new { separator = "semicolon", skipFirstRows = 0, hasHeaderRow = true, skipLastRows = 0, dateColumn = 0, descriptionColumn = 1, amountColumn = 2, dateFormat = "dayMonthYear", decimalSeparator = "comma" },
            rows = new[] { 2, 3 },
            expenseCategoryId = await CategoryAsync(browser, "Food & drink"),
        };
        Assert.Equal(HttpStatusCode.Accepted, (await browser.PostAsync($"/api/v1/imports/{batch}/commit", commit)).StatusCode);
        return batch;
    }

    private static async Task BulkAsync(Browser browser, object request) =>
        (await browser.PostAsync(Bulk, request)).EnsureSuccessStatusCode();

    private static async Task<AuditLogBody> ListAsync(Browser browser, string query = "")
    {
        var response = await browser.GetAsync(AuditLog + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuditLogBody>())!;
    }

    private sealed record AuditLogBody(AuditEntryBody[] Items, string? NextCursor);

    private sealed record AuditEntryBody(Guid Id, string Action, string EntityType, Guid? EntityId, DateTimeOffset At, string Summary);
}
