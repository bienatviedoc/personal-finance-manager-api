using System.Net;
using System.Net.Http.Json;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>Where the money went: expenses per category over a report period (spec section 7, RPT-2 and common behaviour).</summary>
[Collection(ApiCollection.Name)]
public class SpendingByCategoryTests(PfmApi api) : IAsyncLifetime
{
    private const string Report = "/api/v1/reports/spending-by-category";

    private static readonly DateOnly March = new(2026, 3, 1);

    private static readonly DateOnly February = new(2026, 2, 1);

    private const string InMarch = "from=2026-03-01&to=2026-03-31";

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_report_shows_the_expense_total_and_each_top_level_categorys_spending_and_share_ranked_in_the_base_currency()
    {
        var (browser, _) = await api.SignedInAsync();
        var everyday = await AddAccountAsync(browser, "Everyday");
        await SpendAsync(browser, everyday, "Groceries", "500000", March);
        await SpendAsync(browser, everyday, "Coffee", "100000", March.AddDays(30));
        await SpendAsync(browser, everyday, "Transport", "200000", March.AddDays(10));
        await SpendAsync(browser, everyday, "Personal care", "200000", March.AddDays(5));
        await SpendAsync(browser, everyday, "Rent", "9000000", February);

        var report = await ReportAsync(browser, InMarch);

        Assert.Equal((new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "VND", "1000000.00", 0), (report.From, report.To, report.Currency, report.Total, report.NotConverted));
        Assert.Null(report.Parent);
        Assert.Null(report.Previous);
        Assert.Equal(
            [("Food & drink", "600000.00", 60.0m, "subcategories"), ("Personal care", "200000.00", 20.0m, "transactions"), ("Transport", "200000.00", 20.0m, "transactions")],
            report.Items.Select(item => (item.Category.Name, item.Spent, item.Share!.Value, item.DrillsInto)));
        var food = report.Items[0].Category;
        Assert.Equal((await CategoryAsync(browser, "Food & drink"), (Guid?)null, "utensils", "#f97316", false), (food.Id, food.ParentId, food.Icon, food.Color, food.IsArchived));
    }

    private static async Task<ReportBody> ReportAsync(Browser browser, string query)
    {
        var response = await browser.GetAsync($"{Report}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReportBody>())!;
    }

    private sealed record CategoryBody(Guid Id, Guid? ParentId, string Name, string Icon, string Color, bool IsArchived);

    private sealed record ItemBody(CategoryBody Category, string Spent, decimal? Share, int NotConverted, string DrillsInto, string? PreviousSpent);

    private sealed record PreviousBody(DateOnly From, DateOnly To, string Total, int NotConverted);

    private sealed record ReportBody(
        DateOnly From, DateOnly To, string Currency, CategoryBody? Parent, string Total, int NotConverted, List<ItemBody> Items, PreviousBody? Previous);
}
