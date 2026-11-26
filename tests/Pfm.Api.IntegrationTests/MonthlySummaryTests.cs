using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// A user downloads a one-document summary of a month as a PDF (spec section 7, common behaviour: export): its
/// income, expenses, net and savings rate, spending by category and budget results, in their language.
/// </summary>
[Collection(ApiCollection.Name)]
public class MonthlySummaryTests(PfmApi api) : IAsyncLifetime
{
    private const string Summary = "/api/v1/reports/monthly-summary.pdf";

    private static readonly DateOnly March = new(2026, 3, 1);

    public Task InitializeAsync() => api.ForgetExchangeRatesAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_summary_of_a_month_is_a_PDF_of_its_income_expenses_net_savings_rate_spending_by_category_and_budgets()
    {
        var (browser, _) = await api.SignedInAsync();
        await RecordMarchAsync(browser);

        var response = await browser.GetAsync($"{Summary}?month=2026-03&locale=en");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("monthly-summary-2026-03.pdf", response.Content.Headers.ContentDisposition?.FileName);
        var text = await TextAsync(response);
        Assert.Contains("Monthly summary March 2026", text);
        Assert.Contains("Income ₫20,000,000 Expenses ₫6,000,000 Net ₫14,000,000 Savings rate 70%", text);
        Assert.Contains("Spending by category", text);
        Assert.Contains("Food & drink ₫5,000,000 83.3%", text);
        Assert.Contains("Transport ₫1,000,000 16.7%", text);
        Assert.Contains("Total spent ₫6,000,000", text);
        Assert.Contains("Budget vs actual", text);
        Assert.Contains("Food & drink ₫3,000,000 ₫5,000,000 -₫2,000,000 Over budget", text);
        Assert.Contains("Spent with no budget Category Spent Transport ₫1,000,000", text);
        // April is another month.
        Assert.DoesNotContain("99,000,000", text);
    }

    [Fact]
    public async Task In_Vietnamese_it_is_written_in_Vietnamese_with_amounts_written_the_Vietnamese_way()
    {
        var (browser, _) = await api.SignedInAsync();
        await RecordMarchAsync(browser);

        var text = await TextAsync(await browser.GetAsync($"{Summary}?month=2026-03&locale=vi"));

        Assert.Contains("Tổng kết tháng tháng 3 năm 2026", text);
        Assert.Contains("Thu nhập 20.000.000 ₫ Chi tiêu 6.000.000 ₫ Chênh lệch 14.000.000 ₫ Tỷ lệ tiết kiệm 70%", text);
        Assert.Contains("Chi tiêu theo danh mục", text);
        Assert.Contains("5.000.000 ₫ 83,3%", text);
        Assert.Contains("Vượt ngân sách", text);
    }

    [Fact]
    public async Task Without_a_locale_it_is_in_the_locale_of_the_users_profile()
    {
        var (browser, _) = await api.SignedInAsync(locale: "vi");

        var text = await TextAsync(await browser.GetAsync($"{Summary}?month=2026-03"));

        Assert.Contains("Tổng kết tháng tháng 3 năm 2026", text);
        Assert.Contains("Tỷ lệ tiết kiệm —", text);
        Assert.Contains("Không có khoản chi nào trong tháng này.", text);
    }

    [Fact]
    public async Task What_is_not_converted_is_said_to_be_left_out()
    {
        var (browser, _) = await api.SignedInAsync();
        var dollars = await AddAccountAsync(browser, "In USD", "USD");
        await SpendAsync(browser, dollars, "Groceries", "40", March);

        var text = await TextAsync(await browser.GetAsync($"{Summary}?month=2026-03&locale=en"));

        Assert.Contains("1 amount is not converted into your base currency yet and is left out.", text);
    }

    [Theory]
    [InlineData("", "month")]
    [InlineData("month=2026-13", "month")]
    [InlineData("month=March", "month")]
    [InlineData("month=2026-03&locale=fr", "locale")]
    public async Task A_month_that_cannot_be_read_or_a_locale_the_app_does_not_speak_is_refused(string query, string field)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync($"{Summary}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], (await response.Content.ReadFromJsonAsync<ValidationProblemBody>())!.Errors.Keys);
    }

    [Fact]
    public async Task The_summary_needs_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateBrowser().GetAsync($"{Summary}?month=2026-03")).StatusCode);
    }

    /// <summary>March: pay, two expenses, a budget overspent and a category with none; and pay in April, which is not March.</summary>
    private static async Task RecordMarchAsync(Browser browser)
    {
        var everyday = await AddAccountAsync(browser, "Everyday");
        await RecordAsync(browser, "income", everyday, "Salary", "20000000", March);
        await SpendAsync(browser, everyday, "Groceries", "5000000", March.AddDays(3));
        await SpendAsync(browser, everyday, "Transport", "1000000", March.AddDays(4));
        await RecordAsync(browser, "income", everyday, "Salary", "99000000", March.AddMonths(1));
        var budget = await browser.PutAsync($"/api/v1/budgets/2026-03/{await CategoryAsync(browser, "Food & drink")}", new { amount = "3000000" });
        Assert.Equal(HttpStatusCode.Created, budget.StatusCode);
    }

    /// <summary>The words of every page, in reading order, with any run of white space as one space.</summary>
    private static async Task<string> TextAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var words = pdf.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text);
        return Regex.Replace(string.Join(' ', words), @"\s+", " ");
    }

    private sealed record ValidationProblemBody(Dictionary<string, string[]> Errors);
}
