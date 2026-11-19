using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CategoryTests(PfmApi api)
{
    [Fact]
    public async Task A_new_user_starts_with_default_income_and_expense_categories()
    {
        var (browser, _) = await api.SignedInAsync(language: "en");

        var categories = await CategoriesAsync(browser);

        var food = Assert.Single(categories, category => category.Name == "Food & drink");
        Assert.Equal("expense", food.Kind);
        Assert.Contains(food.Subcategories, subcategory => subcategory.Name == "Groceries" && subcategory.Kind == "expense");
        Assert.Contains(categories, category => category.Name == "Salary" && category.Kind == "income");
    }

    [Fact]
    public async Task A_user_who_registers_in_Vietnamese_gets_the_default_categories_in_Vietnamese()
    {
        var (browser, _) = await api.SignedInAsync(language: "vi");

        var categories = await CategoriesAsync(browser);

        var food = Assert.Single(categories, category => category.Name == "Ăn uống");
        Assert.Contains(food.Subcategories, subcategory => subcategory.Name == "Đi chợ");
        Assert.Contains(categories, category => category.Name == "Lương" && category.Kind == "income");
        Assert.DoesNotContain(categories, category => category.Name == "Food & drink");
    }

    [Fact]
    public async Task A_user_who_registers_without_a_language_gets_the_default_categories_in_English()
    {
        var (browser, _) = await api.SignedInAsync();

        Assert.Contains(await CategoriesAsync(browser), category => category.Name == "Food & drink");
    }

    [Fact]
    public async Task Registering_in_a_language_the_app_does_not_speak_is_refused()
    {
        var email = PfmApi.NewEmail();

        var response = await api.CreateBrowser().PostAsync("/api/v1/auth/register", new { email, password = PfmApi.Password, language = "fr" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("language", await ErrorFieldsAsync(response));
        Assert.Empty(api.Emails.To(email));
    }

    private static async Task<List<CategoryBody>> CategoriesAsync(Browser browser, bool includeArchived = false)
    {
        var response = await browser.GetAsync($"/api/v1/categories?includeArchived={includeArchived}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<CategoryBody>>())!;
    }

    private static async Task<string[]> ErrorFieldsAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. json.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name)];
    }

    private sealed record CategoryBody(Guid Id, Guid? ParentId, string Name, string Kind, string Icon, string Color, bool IsArchived, List<CategoryBody> Subcategories);
}
