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
        Assert.Equal(["README.txt", "accounts.csv", "categories.csv", "institutions.csv", "profile.json", "transaction-lines.csv", "transactions.csv"], files.Keys.Order(StringComparer.Ordinal));
        // Every row the user owns is there, under one line of column names.
        Assert.Equal(
            owned,
            files.Where(file => file.Key.EndsWith(".csv", StringComparison.Ordinal)).ToDictionary(file => TableOf(file.Key), file => Csv(file.Value).Count - 1));
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
                ["id"] = id!, ["email"] = email, ["baseCurrency"] = "USD", ["timeZone"] = "Europe/Stockholm", ["locale"] = "vi", ["twoFactorSignIn"] = "False",
            },
            profile.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.ToString()));
    }

    [Fact]
    public async Task Transactions_are_CSV_with_their_text_as_it_was_entered_and_amounts_as_plain_numbers()
    {
        var (browser, _) = await api.SignedInAsync();
        await UserErasureFixtures.OwnSomethingOfEveryKindAsync(browser);

        var files = await FilesAsync(await browser.GetAsync(Export));

        var transactions = Csv(files["transactions.csv"]);
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
        var lines = Csv(files["transaction-lines.csv"]).Skip(1).Select(line => Csv(files["transaction-lines.csv"])[0].Zip(line).ToDictionary(cell => cell.First, cell => cell.Second)).ToList();
        Assert.Equal("Co.opmart", split["payee"]);
        Assert.Equal(
            [(320000m, "Rice"), (180000m, "")],
            lines.Where(line => line["transactionId"] == split["id"]).OrderBy(line => line["position"], StringComparer.Ordinal)
                .Select(line => (decimal.Parse(line["amount"], System.Globalization.CultureInfo.InvariantCulture), line["note"])));
        Assert.Single(lines, line => line["transactionId"] == kept["id"] && line["categoryId"] == kept["categoryId"]);
        // An account, an institution and a category are named by the id their own file lists them under.
        var account = Row(Csv(files["accounts.csv"]), 1);
        Assert.Equal(account["id"], kept["accountId"]);
        Assert.Equal("Everyday", account["name"]);
        Assert.Equal("current", account["type"]);
        Assert.Equal(Row(Csv(files["institutions.csv"]), 1)["id"], account["institutionId"]);
        Assert.Contains(Csv(files["categories.csv"]).Skip(1), category => category.Contains(kept["categoryId"]) && category.Contains("Street food"));
    }

    [Fact]
    public async Task The_export_holds_no_column_that_only_says_whose_the_row_is_or_which_version_it_is()
    {
        var (browser, _) = await api.SignedInAsync();

        var files = await FilesAsync(await browser.GetAsync(Export));

        Assert.All(files.Where(file => file.Key.EndsWith(".csv", StringComparison.Ordinal)), file =>
        {
            var columns = Csv(file.Value)[0];
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

        Assert.Single(Csv(files["transactions.csv"]));
        Assert.Contains("payee", Csv(files["transactions.csv"])[0]);
        Assert.Single(Csv(files["transaction-lines.csv"]));
    }

    [Fact]
    public async Task An_export_holds_nothing_of_another_user()
    {
        var (other, otherEmail) = await api.SignedInAsync();
        var othersAccount = await BrowserFixtures.AddAccountAsync(other, "Somebody else's savings");
        var (browser, _) = await api.SignedInAsync();
        await BrowserFixtures.AddAccountAsync(browser, "Everyday");

        var files = await FilesAsync(await browser.GetAsync(Export));

        Assert.Equal(2, Csv(files["accounts.csv"]).Count);
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

    /// <summary>Reads CSV as RFC 4180 writes it: fields that hold a comma, a quote or a line break are quoted, and a quote inside is doubled.</summary>
    private static List<string[]> Csv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add([.. row]);
                row.Clear();
                i++;
            }
            else
            {
                field.Append(c);
            }
        }

        Assert.True(field.Length == 0 && row.Count == 0, "The last line of a CSV file ends with a line break.");
        return rows;
    }
}
