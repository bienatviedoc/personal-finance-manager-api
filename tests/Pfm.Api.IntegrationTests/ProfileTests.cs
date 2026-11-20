using System.Net;
using System.Net.Http.Json;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ProfileTests(PfmApi api)
{
    [Fact]
    public async Task A_new_user_starts_in_Vietnamese_dong_and_Vietnam_time_with_no_locale_chosen()
    {
        var (browser, email) = await api.SignedInAsync();

        var me = await (await browser.GetAsync("/api/v1/me")).Content.ReadFromJsonAsync<Profile>();

        Assert.Equal(new Profile(me!.Id, email, "VND", "Asia/Ho_Chi_Minh", null), me);
    }

    [Theory]
    [InlineData("vi")]
    [InlineData("en")]
    public async Task The_locale_a_user_registers_in_is_their_profiles_locale(string locale)
    {
        var (browser, _) = await api.SignedInAsync(locale: locale);

        var me = await (await browser.GetAsync("/api/v1/me")).Content.ReadFromJsonAsync<Profile>();

        Assert.Equal(locale, me!.Locale);
    }

    [Fact]
    public async Task A_user_can_change_their_base_currency_time_zone_and_locale()
    {
        var (browser, email) = await api.SignedInAsync();

        var patched = await browser.PatchAsync("/api/v1/me", new { baseCurrency = "EUR", timeZone = "Europe/Berlin", locale = "en" });
        var me = await (await browser.GetAsync("/api/v1/me")).Content.ReadFromJsonAsync<Profile>();

        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal(me, await patched.Content.ReadFromJsonAsync<Profile>());
        Assert.Equal(new Profile(me!.Id, email, "EUR", "Europe/Berlin", "en"), me);
    }

    [Fact]
    public async Task A_setting_left_out_of_a_change_stays_as_it_was()
    {
        var (browser, _) = await api.SignedInAsync();
        await browser.PatchAsync("/api/v1/me", new { baseCurrency = "USD", locale = "vi" });

        await browser.PatchAsync("/api/v1/me", new { timeZone = "America/New_York" });
        var me = await (await browser.GetAsync("/api/v1/me")).Content.ReadFromJsonAsync<Profile>();

        Assert.Equal(("USD", "America/New_York", "vi"), (me!.BaseCurrency, me.TimeZone, me.Locale));
    }

    [Theory]
    [InlineData("""{ "baseCurrency": "JPY" }""", "baseCurrency")]
    [InlineData("""{ "baseCurrency": "vnd" }""", "baseCurrency")]
    [InlineData("""{ "baseCurrency": "1" }""", "baseCurrency")]
    [InlineData("""{ "timeZone": "Mars/Olympus_Mons" }""", "timeZone")]
    [InlineData("""{ "timeZone": "" }""", "timeZone")]
    [InlineData("""{ "timeZone": "SE Asia Standard Time" }""", "timeZone")]
    [InlineData("""{ "locale": "fr" }""", "locale")]
    [InlineData("""{ "locale": "vi", "baseCurrency": "XXX", "timeZone": "nowhere" }""", "baseCurrency,timeZone")]
    public async Task An_unsupported_setting_is_refused_names_the_field_and_changes_nothing(string body, string fields)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PatchJsonAsync("/api/v1/me", body);
        var me = await (await browser.GetAsync("/api/v1/me")).Content.ReadFromJsonAsync<Profile>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var named = problem.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name).Order();
        Assert.Equal(fields.Split(',').Order(), named);
        Assert.Equal(("VND", "Asia/Ho_Chi_Minh", null), (me!.BaseCurrency, me.TimeZone, me.Locale));
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_cannot_change_a_profile()
    {
        var response = await api.CreateBrowser().PatchAsync("/api/v1/me", new { baseCurrency = "EUR" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record Profile(Guid Id, string Email, string BaseCurrency, string TimeZone, string? Locale);
}
