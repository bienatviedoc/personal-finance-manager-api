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

    private sealed record Identified(Guid Id);

    private sealed record Balanced(string Balance);

    private sealed record CategoryNode(Guid Id, string Name, List<CategoryNode> Subcategories);

    private sealed record CategoryTree(List<CategoryNode> Items);
}
