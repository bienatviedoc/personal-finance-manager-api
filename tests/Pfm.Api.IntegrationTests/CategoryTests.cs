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
        var (browser, _) = await api.SignedInAsync(locale: "en");

        var categories = await CategoriesAsync(browser);

        var food = Assert.Single(categories, category => category.Name == "Food & drink");
        Assert.Equal("expense", food.Kind);
        Assert.Contains(food.Subcategories, subcategory => subcategory.Name == "Groceries" && subcategory.Kind == "expense");
        Assert.Contains(categories, category => category.Name == "Salary" && category.Kind == "income");
    }

    [Fact]
    public async Task A_user_who_registers_in_Vietnamese_gets_the_default_categories_in_Vietnamese()
    {
        var (browser, _) = await api.SignedInAsync(locale: "vi");

        var categories = await CategoriesAsync(browser);

        var food = Assert.Single(categories, category => category.Name == "Ăn uống");
        Assert.Contains(food.Subcategories, subcategory => subcategory.Name == "Đi chợ");
        Assert.Contains(categories, category => category.Name == "Lương" && category.Kind == "income");
        Assert.DoesNotContain(categories, category => category.Name == "Food & drink");
    }

    [Fact]
    public async Task A_user_who_registers_without_a_locale_gets_the_default_categories_in_English()
    {
        var (browser, _) = await api.SignedInAsync();

        Assert.Contains(await CategoriesAsync(browser), category => category.Name == "Food & drink");
    }

    [Fact]
    public async Task Registering_in_a_locale_the_app_does_not_speak_is_refused()
    {
        var email = PfmApi.NewEmail();

        var response = await api.CreateBrowser().PostAsync("/api/v1/auth/register", new { email, password = PfmApi.Password, locale = "fr" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("locale", await ErrorFieldsAsync(response));
        Assert.Empty(api.Emails.To(email));
    }

    [Fact]
    public async Task A_user_can_add_a_category_and_a_subcategory_under_it()
    {
        var (browser, _) = await api.SignedInAsync();

        var pets = await CreateAsync(browser, new { name = "Pets", kind = "expense", icon = "paw-print", color = "#a16207" });
        var vet = await CreateAsync(browser, new { parentId = pets.Id, name = "Vet", kind = "expense", icon = "stethoscope", color = "#a16207" });

        var saved = Assert.Single(await CategoriesAsync(browser), category => category.Id == pets.Id);
        Assert.Equal(("Pets", "expense", "paw-print", "#a16207"), (saved.Name, saved.Kind, saved.Icon, saved.Color));
        Assert.Equal(vet.Id, Assert.Single(saved.Subcategories).Id);
        Assert.Equal(pets.Id, vet.ParentId);
    }

    [Fact]
    public async Task A_subcategory_of_another_kind_from_its_parent_is_refused()
    {
        var (browser, _) = await api.SignedInAsync();
        var salary = (await CategoriesAsync(browser)).Single(category => category.Name == "Salary");

        var response = await browser.PostAsync("/api/v1/categories", new { parentId = salary.Id, name = "Groceries", kind = "expense", icon = "shopping-cart", color = "#ef4444" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("kind", await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task A_category_three_levels_deep_is_refused()
    {
        var (browser, _) = await api.SignedInAsync();
        var groceries = (await CategoriesAsync(browser)).SelectMany(category => category.Subcategories).First(category => category.Name == "Groceries");

        var response = await browser.PostAsync("/api/v1/categories", new { parentId = groceries.Id, name = "Vegetables", kind = "expense", icon = "carrot", color = "#ef4444" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("parentId", await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task A_category_without_a_name_kind_icon_or_colour_is_refused()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync("/api/v1/categories", new { name = " ", icon = "", color = "red" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["color", "icon", "kind", "name"], (await ErrorFieldsAsync(response)).Order());
    }

    [Fact]
    public async Task A_user_can_rename_recolour_and_re_icon_a_category()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = (await CategoriesAsync(browser)).Single(category => category.Name == "Food & drink");

        var response = await browser.PutAsync($"/api/v1/categories/{food.Id}", new { name = "Food", kind = "expense", icon = "pizza", color = "#123abc" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await CategoriesAsync(browser)).Single(category => category.Id == food.Id);
        Assert.Equal(("Food", "pizza", "#123abc"), (saved.Name, saved.Icon, saved.Color));
    }

    [Fact]
    public async Task Changing_a_categorys_kind_changes_its_subcategories_too()
    {
        var (browser, _) = await api.SignedInAsync();
        var investments = (await CategoriesAsync(browser)).Single(category => category.Name == "Investments");

        var response = await browser.PutAsync($"/api/v1/categories/{investments.Id}", new { name = "Investments", kind = "expense", icon = investments.Icon, color = investments.Color });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await CategoriesAsync(browser)).Single(category => category.Id == investments.Id);
        Assert.Equal("expense", saved.Kind);
        Assert.All(saved.Subcategories, subcategory => Assert.Equal("expense", subcategory.Kind));
    }

    [Fact]
    public async Task A_subcategorys_kind_cannot_change_apart_from_its_parent()
    {
        var (browser, _) = await api.SignedInAsync();
        var groceries = (await CategoriesAsync(browser)).SelectMany(category => category.Subcategories).First(category => category.Name == "Groceries");

        var response = await browser.PutAsync($"/api/v1/categories/{groceries.Id}", new { name = "Groceries", kind = "income", icon = groceries.Icon, color = groceries.Color });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("kind", await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task An_archived_category_and_its_subcategories_are_hidden_from_pickers_but_kept()
    {
        var (browser, _) = await api.SignedInAsync();
        var food = (await CategoriesAsync(browser)).Single(category => category.Name == "Food & drink");

        var response = await browser.DeleteAsync($"/api/v1/categories/{food.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(await CategoriesAsync(browser), category => category.Id == food.Id);
        var archived = (await CategoriesAsync(browser, includeArchived: true)).Single(category => category.Id == food.Id);
        Assert.True(archived.IsArchived);
        Assert.NotEmpty(archived.Subcategories);
        Assert.All(archived.Subcategories, subcategory => Assert.True(subcategory.IsArchived));
    }

    [Fact]
    public async Task Another_users_categories_are_never_visible_or_changeable()
    {
        var (alice, _) = await api.SignedInAsync();
        var (bob, _) = await api.SignedInAsync();
        var bobsFood = (await CategoriesAsync(bob)).Single(category => category.Name == "Food & drink");

        var renamed = await alice.PutAsync($"/api/v1/categories/{bobsFood.Id}", new { name = "Mine", kind = "expense", icon = "pizza", color = "#123abc" });
        var archived = await alice.DeleteAsync($"/api/v1/categories/{bobsFood.Id}");
        var subcategory = await alice.PostAsync("/api/v1/categories", new { parentId = bobsFood.Id, name = "Mine", kind = "expense", icon = "pizza", color = "#123abc" });

        Assert.Equal(HttpStatusCode.NotFound, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, archived.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, subcategory.StatusCode);
        Assert.DoesNotContain(await CategoriesAsync(alice, includeArchived: true), category => category.Id == bobsFood.Id);
        var unchanged = (await CategoriesAsync(bob)).Single(category => category.Id == bobsFood.Id);
        Assert.Equal(("Food & drink", false), (unchanged.Name, unchanged.IsArchived));
        Assert.DoesNotContain(unchanged.Subcategories, sub => sub.Name == "Mine");
    }

    private static async Task<CategoryBody> CreateAsync(Browser browser, object category)
    {
        var response = await browser.PostAsync("/api/v1/categories", category);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryBody>())!;
    }

    private static async Task<List<CategoryBody>> CategoriesAsync(Browser browser, bool includeArchived = false)
    {
        var response = await browser.GetAsync($"/api/v1/categories?includeArchived={includeArchived}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryList>())!.Items;
    }

    private static async Task<string[]> ErrorFieldsAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. json.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name)];
    }

    private sealed record CategoryBody(Guid Id, Guid? ParentId, string Name, string Kind, string Icon, string Color, bool IsArchived, List<CategoryBody> Subcategories);

    private sealed record CategoryList(List<CategoryBody> Items);
}
