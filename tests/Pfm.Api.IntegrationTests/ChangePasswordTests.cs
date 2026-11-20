using System.Net;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ChangePasswordTests(PfmApi api)
{
    private const string NewPassword = "a brand new passphrase";

    [Fact]
    public async Task After_a_change_the_user_signs_in_with_the_new_password_and_not_the_old_one()
    {
        var (browser, email) = await api.SignedInAsync();

        var response = await browser.PostAsync("/api/v1/me/password", new { currentPassword = PfmApi.Password, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var withOld = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });
        var withNew = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = NewPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, withNew.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_keeps_this_session_and_ends_the_users_others()
    {
        var (browser, email) = await api.SignedInAsync();
        var other = api.CreateBrowser();
        await other.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

        await browser.PostAsync("/api/v1/me/password", new { currentPassword = PfmApi.Password, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/me")).StatusCode);
    }

    [Theory]
    [InlineData("""{ "currentPassword": "not the right password", "newPassword": "a brand new passphrase" }""", "currentPassword")]
    [InlineData("""{ "currentPassword": "correct horse battery staple", "newPassword": "too short" }""", "newPassword")]
    [InlineData("""{ "newPassword": "a brand new passphrase" }""", "currentPassword")]
    [InlineData("{}", "currentPassword,newPassword")]
    public async Task A_wrong_current_password_or_a_new_one_the_policy_refuses_is_named_and_changes_nothing(string body, string fields)
    {
        var (browser, email) = await api.SignedInAsync();

        var response = await browser.PostJsonAsync("/api/v1/me/password", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(fields.Split(','), await response.ErrorFieldsAsync());
        var withOld = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });
        Assert.Equal(HttpStatusCode.NoContent, withOld.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_needs_a_signed_in_user()
    {
        var response = await api.CreateBrowser().PostAsync("/api/v1/me/password", new { currentPassword = PfmApi.Password, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
