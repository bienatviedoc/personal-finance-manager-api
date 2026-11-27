using System.Globalization;
using System.Net;
using System.Text;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// Taking the transactions elsewhere: <c>GET /export/transactions.csv</c> downloads every transaction that passes
/// the list's filters as CSV (spec sections 7 and 11, Export).
/// </summary>
[Collection(ApiCollection.Name)]
public class TransactionExportTests(PfmApi api)
{
    private const string Export = "/api/v1/export/transactions.csv";

    private static readonly DateOnly Day = new(2026, 11, 2);

    [Fact]
    public async Task The_export_is_a_CSV_download_named_with_the_users_date()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync(Export);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Matches(@"^transactions-\d{4}-\d{2}-\d{2}\.csv$", response.Content.Headers.ContentDisposition?.FileName);
        // A byte order mark tells a spreadsheet the text is UTF-8, so Vietnamese reads as written.
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), bytes[..3]);
        Assert.Equal(
            "date,type,isRefund,account,category,payee,note,tags,amount,currency\r\n",
            Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public async Task The_export_holds_every_transaction_that_passes_the_filters_not_just_one_page()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        for (var i = 1; i <= 120; i++)
        {
            await SpendAsync(browser, everyday, "Groceries", $"{i}", Day);
        }

        await SpendAsync(browser, savings, "Groceries", "999", Day);
        await SpendAsync(browser, everyday, "Groceries", "5000", Day.AddDays(-30));

        var rows = await RowsAsync(browser, $"?accountId={everyday}&from=2026-11-01&sort=amount&order=asc");

        Assert.Equal(120, rows.Count);
        Assert.Equal(Enumerable.Range(1, 120).Select(i => $"{i}.00"), rows.Select(row => row["amount"]));
    }

    [Fact]
    public async Task Amounts_are_plain_decimals_beside_their_currency_and_dates_are_calendar_dates_with_names_for_the_account_and_category()
    {
        var (browser, _) = await api.SignedInAsync();
        var travel = await AddAccountAsync(browser, "Travel, card", "USD");
        var everyday = await AddAccountAsync(browser, "Everyday");
        await RecordAsync(browser, "expense", travel, "Groceries", "1234.5", Day, payee: "Bánh mì \"Huỳnh Hoa\"");
        await RefundAsync(browser, everyday, "Groceries", "45000", Day.AddDays(-1));
        await SplitAsync(browser, everyday, "150000", Day.AddDays(-2), ("Groceries", "100000"), ("Household", "50000"));
        await RecordAsync(browser, "income", everyday, "Salary", "30000000", Day.AddDays(-3));

        var rows = await RowsAsync(browser, "");

        Assert.Equal(
            [
                ["2026-11-02", "expense", "false", "Travel, card", "Groceries", "Bánh mì \"Huỳnh Hoa\"", "", "", "1234.50", "USD"],
                ["2026-11-01", "expense", "true", "Everyday", "Groceries", "", "", "", "45000.00", "VND"],
                ["2026-10-31", "expense", "false", "Everyday", "Groceries; Household", "", "", "", "150000.00", "VND"],
                ["2026-10-30", "income", "false", "Everyday", "Salary", "", "", "", "30000000.00", "VND"],
            ],
            rows.Select(row => row.Values.ToArray()));
    }

    [Fact]
    public async Task Text_a_spreadsheet_would_run_as_a_formula_is_exported_as_text_behind_a_quote_and_amounts_stay_numbers()
    {
        var (browser, _) = await api.SignedInAsync();
        var account = await AddAccountAsync(browser, "@Everyday");
        foreach (var (payee, day) in new[] { ("=HYPERLINK(\"http://evil.example\")", 0), ("+1+2", 1), ("-2+3", 2), ("@SUM(A1)", 3), ("Phở = good", 4) })
        {
            await RecordAsync(browser, "expense", account, "Groceries", "1000", Day.AddDays(-day), payee: payee);
        }

        var rows = await RowsAsync(browser, "");

        Assert.Equal(
            ["'=HYPERLINK(\"http://evil.example\")", "'+1+2", "'-2+3", "'@SUM(A1)", "Phở = good"],
            rows.Select(row => row["payee"]));
        Assert.All(rows, row => Assert.Equal(("'@Everyday", "1000.00"), (row["account"], row["amount"])));
    }

    [Fact]
    public async Task Both_legs_of_a_transfer_are_exported_with_no_category()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var savings = await AddAccountAsync(browser, "Savings");
        await TransferAsync(browser, everyday, savings, "500000", Day);

        var rows = await RowsAsync(browser, "?sort=amount");

        Assert.Equal(
            [("transferIn", "Savings", ""), ("transferOut", "Everyday", "")],
            rows.Select(row => (row["type"], row["account"], row["category"])).Order());
    }

    [Fact]
    public async Task The_tag_filter_exports_only_the_transactions_with_the_tag_and_each_line_names_its_tags()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        var groceries = await CategoryAsync(browser, "Groceries");
        foreach (var (amount, tags) in new[] { ("100", new[] { "Tet", "family" }), ("200", new[] { "work" }), ("300", Array.Empty<string>()) })
        {
            var response = await browser.PostAsync("/api/v1/transactions", new { type = "expense", accountId = everyday, amount, date = "2026-11-02", categoryId = groceries, tags });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var tagged = await RowsAsync(browser, "?tag=TET");
        var all = await RowsAsync(browser, "?sort=amount&order=asc");

        Assert.Equal([("100.00", "family; tet")], tagged.Select(row => (row["amount"], row["tags"])));
        Assert.Equal(["family; tet", "work", ""], all.Select(row => row["tags"]));
    }

    [Fact]
    public async Task A_deleted_transaction_is_not_exported()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SpendAsync(browser, everyday, "Groceries", "100", Day);
        var deleted = await SpendAsync(browser, everyday, "Groceries", "200", Day);
        (await browser.DeleteAsync($"/api/v1/transactions/{deleted}")).EnsureSuccessStatusCode();

        var rows = await RowsAsync(browser, "");

        Assert.Equal(["100.00"], rows.Select(row => row["amount"]));
    }

    [Fact]
    public async Task A_filter_the_list_cannot_read_is_refused_by_name()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Export}?from=yesterday&type=gift&tag={new string('x', 51)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["from", "tag", "type"], await response.ErrorFieldsAsync());
    }

    /// <summary>The export's rows, each by column name, read by a CSV reader that knows quotes.</summary>
    private static async Task<List<Dictionary<string, string>>> RowsAsync(Browser browser, string query)
    {
        var response = await browser.GetAsync(Export + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lines = CsvFiles.Read(Encoding.UTF8.GetString((await response.Content.ReadAsByteArrayAsync())[3..]));
        return [.. lines.Skip(1).Select(line => lines[0].Zip(line).ToDictionary(field => field.First, field => field.Second))];
    }
}
