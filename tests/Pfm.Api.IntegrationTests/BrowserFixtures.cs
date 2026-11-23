using System.Net.Http.Json;

namespace Pfm.Api.IntegrationTests;

/// <summary>What tests of several modules set up through the API before they start: an account, an institution, a category to use.</summary>
public static class BrowserFixtures
{
    /// <summary>A current account that opened at the start of 2026.</summary>
    public static async Task<Guid> AddAccountAsync(Browser browser, string name, string currency = "VND", string openingBalance = "0")
    {
        var response = await browser.PostAsync("/api/v1/accounts", new { name, type = "current", currency, openingBalance, openingDate = "2026-01-01" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    public static async Task<string> BalanceAsync(Browser browser, Guid accountId) =>
        (await browser.Http.GetFromJsonAsync<Balanced>($"/api/v1/accounts/{accountId}"))!.Balance;

    public static async Task<Guid> AddInstitutionAsync(Browser browser, string name)
    {
        var response = await browser.PostAsync("/api/v1/institutions", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    /// <summary>One of the default categories (or one added since, archived or not), top-level or a subcategory, by name.</summary>
    public static async Task<Guid> CategoryAsync(Browser browser, string name)
    {
        var tree = (await browser.Http.GetFromJsonAsync<CategoryTree>("/api/v1/categories?includeArchived=true"))!.Items;
        return tree.Concat(tree.SelectMany(category => category.Subcategories)).Single(category => category.Name == name).Id;
    }

    /// <summary>Records an expense on one category, as the user would.</summary>
    public static Task<Guid> SpendAsync(Browser browser, Guid accountId, string category, string amount, DateOnly date, string? payee = null) =>
        RecordAsync(browser, "expense", accountId, category, amount, date, payee: payee);

    /// <summary>Records a refund: an expense with the refund flag, money coming back from a purchase.</summary>
    public static Task<Guid> RefundAsync(Browser browser, Guid accountId, string category, string amount, DateOnly date) =>
        RecordAsync(browser, "expense", accountId, category, amount, date, isRefund: true);

    /// <summary>Records income or an expense on one category.</summary>
    public static async Task<Guid> RecordAsync(
        Browser browser, string type, Guid accountId, string category, string amount, DateOnly date, bool isRefund = false, string? payee = null)
    {
        var response = await browser.PostAsync("/api/v1/transactions", new
        {
            type, accountId, amount, date = date.ToString("yyyy-MM-dd"), categoryId = await CategoryAsync(browser, category), isRefund, payee,
        });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    /// <summary>Records one expense split across categories, each line with its part of the amount.</summary>
    public static async Task<Guid> SplitAsync(Browser browser, Guid accountId, string amount, DateOnly date, params (string Category, string Amount)[] lines)
    {
        var parts = new List<object>();
        foreach (var line in lines)
        {
            parts.Add(new { categoryId = await CategoryAsync(browser, line.Category), amount = line.Amount });
        }

        var response = await browser.PostAsync("/api/v1/transactions", new { type = "expense", accountId, amount, date = date.ToString("yyyy-MM-dd"), lines = parts });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    /// <summary>Moves money between two of the user's accounts in one currency.</summary>
    public static async Task TransferAsync(Browser browser, Guid fromAccountId, Guid toAccountId, string amount, DateOnly date)
    {
        var response = await browser.PostAsync("/api/v1/transfers", new { fromAccountId, toAccountId, amount, date = date.ToString("yyyy-MM-dd") });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
    }

    private sealed record Identified(Guid Id);

    private sealed record Balanced(string Balance);

    private sealed record CategoryNode(Guid Id, string Name, List<CategoryNode> Subcategories);

    private sealed record CategoryTree(List<CategoryNode> Items);
}
