using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Pfm.Api.IntegrationTests;

/// <summary>A user downloads everything the app holds about them as a ZIP of CSV and JSON files (spec sections 8 and 11).</summary>
[Collection(ApiCollection.Name)]
public class DataExportTests(PfmApi api)
{
    private const string Export = "/api/v1/export/all.zip";

    [Fact]
    public async Task A_full_export_is_a_ZIP_download_with_a_file_for_the_profile_and_one_for_each_kind_of_data()
    {
        var (browser, _) = await api.SignedInAsync();
        var owned = await UserErasureFixtures.OwnSomethingOfEveryKindAsync(browser);

        var response = await browser.GetAsync(Export);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Matches(@"^personal-finance-manager-\d{4}-\d{2}-\d{2}\.zip$", response.Content.Headers.ContentDisposition?.FileName);
        var files = await FilesAsync(response);
        Assert.Equal(
            ["README.txt", "accounts.csv", "attachments.csv", "budgets.csv", "categories.csv", "institutions.csv", "profile.json", "reconciliations.csv", "recurring-rules.csv", "tags.csv", "transaction-lines.csv", "transaction-tags.csv", "transactions.csv"],
            files.Keys.Where(file => !file.StartsWith("attachments/", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        // Every row the user owns is there, under one line of column names.
        Assert.Equal(
            owned,
            files.Where(file => file.Key.EndsWith(".csv", StringComparison.Ordinal)).ToDictionary(file => TableOf(file.Key), file => CsvFiles.Read(file.Value).Count - 1));
    }

    [Fact]
    public async Task The_files_a_user_attached_are_in_the_export_each_named_by_its_attachment_and_its_own_name()
    {
        var (browser, _) = await api.SignedInAsync();
        await UserErasureFixtures.OwnSomethingOfEveryKindAsync(browser);

        var files = await FilesAsync(await browser.GetAsync(Export));

        var attachment = Row(CsvFiles.Read(files["attachments.csv"]), 1);
        Assert.Equal((UserErasureFixtures.ReceiptName, "application/pdf"), (attachment["fileName"], attachment["contentType"]));
        var file = Assert.Single(files, file => file.Key.StartsWith("attachments/", StringComparison.Ordinal));
        Assert.Equal($"attachments/{attachment["id"]}-{UserErasureFixtures.ReceiptName}", file.Key);
        Assert.Equal(Encoding.UTF8.GetString(UserErasureFixtures.Receipt), file.Value);
        Assert.Contains("attachments/", files["README.txt"]);
    }

    [Fact]
    public async Task The_attachments_of_a_deleted_transaction_are_exported_like_the_transaction_and_a_removed_one_is_not()
    {
        var (browser, _) = await api.SignedInAsync();
        var accountId = await BrowserFixtures.AddAccountAsync(browser, "Everyday");
        var groceries = await BrowserFixtures.CategoryAsync(browser, "Groceries");
        var recorded = await browser.PostAsync("/api/v1/transactions", new { type = "expense", accountId, amount = "45000", date = "2026-11-02", categoryId = groceries });
        var transaction = (await recorded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var kept = await browser.PostFileAsync($"/api/v1/transactions/{transaction}/attachments", "kept.pdf", UserErasureFixtures.Receipt);
        var removed = await browser.PostFileAsync($"/api/v1/transactions/{transaction}/attachments", "removed.pdf", UserErasureFixtures.Receipt);
        (await browser.DeleteAsync($"/api/v1/attachments/{(await removed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()}")).EnsureSuccessStatusCode();
        (await browser.DeleteAsync($"/api/v1/transactions/{transaction}")).EnsureSuccessStatusCode();

        var files = await FilesAsync(await browser.GetAsync(Export));

        var keptId = (await kept.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.Equal([$"attachments/{keptId}-kept.pdf"], files.Keys.Where(file => file.StartsWith("attachments/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_profile_is_JSON_with_the_users_email_address_and_settings_and_nothing_secret()
    {
        var (browser, email) = await api.SignedInAsync(locale: "vi");
        await browser.PatchAsync("/api/v1/me", new { baseCurrency = "USD", timeZone = "Europe/Stockholm" });
        var id = (await browser.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetString();

        var profile = JsonDocument.Parse((await FilesAsync(await browser.GetAsync(Export)))["profile.json"]).RootElement;

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["id"] = id!, ["email"] = email, ["baseCurrency"] = "USD", ["timeZone"] = "Europe/Stockholm", ["locale"] = "vi", ["budgetWarningPercent"] = "80", ["twoFactorSignIn"] = "False",
            },
            profile.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.ToString()));
    }

    [Fact]
    public async Task Transactions_are_CSV_with_their_text_as_it_was_entered_and_amounts_as_plain_numbers()
    {
        var (browser, _) = await api.SignedInAsync();
        await UserErasureFixtures.OwnSomethingOfEveryKindAsync(browser);

        var files = await FilesAsync(await browser.GetAsync(Export));

        var transactions = CsvFiles.Read(files["transactions.csv"]);
        var kept = Row(transactions, 1);
        var deleted = Row(transactions, 2);
        Assert.Equal("expense", kept["type"]);
        Assert.Equal(45000m, decimal.Parse(kept["amount"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("VND", kept["currency"]);
        Assert.Equal("2026-11-02", kept["date"]);
        Assert.Equal("Bánh mì Huỳnh Hoa", kept["payee"]);
        Assert.Equal("Lunch, with \"extra\" chilli", kept["note"]);
        Assert.Equal("", kept["deletedAt"]);
        // What the user deleted is still held for a while, so it is theirs to take too.
        Assert.Equal("Phở Thìn", deleted["payee"]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$", deleted["deletedAt"]);
        // A refund is an expense flagged as money coming back.
        Assert.Equal("false", kept["isRefund"]);
        Assert.Equal(("expense", "true"), (Row(transactions, 3)["type"], Row(transactions, 3)["isRefund"]));
        // A split's lines are in a file of their own, each naming its transaction and its category.
        var split = Row(transactions, 4);
        var lines = CsvFiles.Read(files["transaction-lines.csv"]).Skip(1).Select(line => CsvFiles.Read(files["transaction-lines.csv"])[0].Zip(line).ToDictionary(cell => cell.First, cell => cell.Second)).ToList();
        Assert.Equal("Co.opmart", split["payee"]);
        Assert.Equal(
            [(320000m, "Rice"), (180000m, "")],
            lines.Where(line => line["transactionId"] == split["id"]).OrderBy(line => line["position"], StringComparer.Ordinal)
                .Select(line => (decimal.Parse(line["amount"], System.Globalization.CultureInfo.InvariantCulture), line["note"])));
        Assert.Single(lines, line => line["transactionId"] == kept["id"] && line["categoryId"] == kept["categoryId"]);
        // A transfer is its two legs, each on its own account, sharing a transfer id and without a category.
        var legs = new[] { Row(transactions, 5), Row(transactions, 6) }.OrderByDescending(leg => leg["type"], StringComparer.Ordinal).ToArray();
        Assert.Equal(["transferOut", "transferIn"], legs.Select(leg => leg["type"]));
        Assert.Equal(legs[0]["transferId"], legs[1]["transferId"]);
        Assert.NotEqual("", legs[0]["transferId"]);
        Assert.Equal(("", kept["transferId"]), (legs[0]["categoryId"], ""));
        Assert.Equal(kept["accountId"], legs[0]["accountId"]);
        Assert.NotEqual(kept["accountId"], legs[1]["accountId"]);
        Assert.All(legs, leg => Assert.Equal((200000m, "For Tet"), (decimal.Parse(leg["amount"], System.Globalization.CultureInfo.InvariantCulture), leg["note"])));
        Assert.DoesNotContain(lines, line => line["transactionId"] == legs[0]["id"] || line["transactionId"] == legs[1]["id"]);
        // A budget names its category and its budget month by the month's first day.
        var budget = Row(CsvFiles.Read(files["budgets.csv"]), 1);
        Assert.Equal(3000000m, decimal.Parse(budget["amount"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(("VND", $"{DateTime.UtcNow:yyyy-MM}-01"), (budget["currency"], budget["month"]));
        Assert.Contains(CsvFiles.Read(files["categories.csv"]).Skip(1), category => category.Contains(budget["categoryId"]) && category.Contains("Food & drink"));
        // An account, an institution and a category are named by the id their own file lists them under.
        var account = Row(CsvFiles.Read(files["accounts.csv"]), 1);
        Assert.Equal(account["id"], kept["accountId"]);
        Assert.Equal("Everyday", account["name"]);
        Assert.Equal("current", account["type"]);
        Assert.Equal(Row(CsvFiles.Read(files["institutions.csv"]), 1)["id"], account["institutionId"]);
        Assert.Contains(CsvFiles.Read(files["categories.csv"]).Skip(1), category => category.Contains(kept["categoryId"]) && category.Contains("Street food"));
    }

    [Fact]
    public async Task The_export_holds_no_column_that_only_says_whose_the_row_is_or_which_version_it_is()
    {
        var (browser, _) = await api.SignedInAsync();

        var files = await FilesAsync(await browser.GetAsync(Export));

        Assert.All(files.Where(file => file.Key.EndsWith(".csv", StringComparison.Ordinal)), file =>
        {
            var columns = CsvFiles.Read(file.Value)[0];
            Assert.DoesNotContain("userId", columns);
            Assert.DoesNotContain("xmin", columns);
            Assert.DoesNotContain("rowVersion", columns);
        });
    }

    [Fact]
    public async Task A_user_with_nothing_of_a_kind_gets_its_file_with_the_column_names_alone()
    {
        var (browser, _) = await api.SignedInAsync();

        var files = await FilesAsync(await browser.GetAsync(Export));

        Assert.Single(CsvFiles.Read(files["transactions.csv"]));
        Assert.Contains("payee", CsvFiles.Read(files["transactions.csv"])[0]);
        Assert.Single(CsvFiles.Read(files["transaction-lines.csv"]));
    }

    [Fact]
    public async Task An_export_holds_nothing_of_another_user()
    {
        var (other, otherEmail) = await api.SignedInAsync();
        var othersAccount = await BrowserFixtures.AddAccountAsync(other, "Somebody else's savings");
        var (browser, _) = await api.SignedInAsync();
        await BrowserFixtures.AddAccountAsync(browser, "Everyday");

        var files = await FilesAsync(await browser.GetAsync(Export));

        Assert.Equal(2, CsvFiles.Read(files["accounts.csv"]).Count);
        Assert.All(files.Values, text =>
        {
            Assert.DoesNotContain("Somebody else's savings", text);
            Assert.DoesNotContain(othersAccount.ToString(), text);
            Assert.DoesNotContain(otherEmail, text);
        });
    }

    [Fact]
    public async Task Only_a_signed_in_user_can_export()
    {
        var response = await api.CreateBrowser().GetAsync(Export);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string TableOf(string file) =>
        string.Concat(file[..^4].Split('-').Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    /// <summary>The files in the ZIP by name, each read as UTF-8 text.</summary>
    private static async Task<Dictionary<string, string>> FilesAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        var files = new Dictionary<string, string>();
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            files[entry.FullName] = await reader.ReadToEndAsync();
        }

        return files;
    }

    private static Dictionary<string, string> Row(List<string[]> csv, int row) =>
        csv[0].Zip(csv[row]).ToDictionary(cell => cell.First, cell => cell.Second);
}
