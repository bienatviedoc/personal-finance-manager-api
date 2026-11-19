using System.Net;
using System.Net.Http.Json;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ProfileTests(PfmApi api)
{
    [Fact]
    public async Task A_new_user_starts_in_Vietnamese_dong_and_Vietnam_time_with_no_language_chosen()
    {
        var (browser, email) = await api.SignedInAsync();

        var me = await (await browser.GetAsync("/api/v1/me")).Content.ReadFromJsonAsync<Profile>();

        Assert.Equal(new Profile(me!.Id, email, "VND", "Asia/Ho_Chi_Minh", null), me);
    }

    private sealed record Profile(Guid Id, string Email, string BaseCurrency, string TimeZone, string? Locale);
}
