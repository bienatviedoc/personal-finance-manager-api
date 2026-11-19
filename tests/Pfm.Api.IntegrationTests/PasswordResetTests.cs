using System.Net;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PasswordResetTests(PfmApi api)
{
    private const string NewPassword = "a brand new passphrase";

    [Fact]
    public async Task Requesting_a_reset_emails_the_user_a_reset_link()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();

        var response = await browser.PostAsync("/api/v1/auth/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"{PfmApi.PublicUrl}/reset-password", api.LastLinkTo(email).GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task After_a_reset_the_user_signs_in_with_the_new_password_and_not_the_old_one()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();
        await browser.PostAsync("/api/v1/auth/forgot-password", new { email });

        var reset = await browser.PostAsync("/api/v1/auth/reset-password", ResetFrom(api.LastLinkTo(email), NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        var withOld = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });
        var withNew = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = NewPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, withNew.StatusCode);
    }

    [Fact]
    public async Task A_reset_link_works_only_once()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();
        await browser.PostAsync("/api/v1/auth/forgot-password", new { email });
        var link = api.LastLinkTo(email);
        await browser.PostAsync("/api/v1/auth/reset-password", ResetFrom(link, NewPassword));

        var again = await browser.PostAsync("/api/v1/auth/reset-password", ResetFrom(link, "an attacker's passphrase"));

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(ResetLinkInvalid, await ProblemTypeAsync(again));
        var withNew = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, withNew.StatusCode);
    }

    [Fact]
    public async Task An_expired_reset_link_is_refused_and_changes_nothing()
    {
        var expiring = PfmApi.CreateBrowser(api.WhereResetLinksExpireAtOnce());
        var (browser, email) = await api.RegisteredAndVerifiedAsync(expiring);
        await browser.PostAsync("/api/v1/auth/forgot-password", new { email });

        var reset = await browser.PostAsync("/api/v1/auth/reset-password", ResetFrom(api.LastLinkTo(email), NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal(ResetLinkInvalid, await ProblemTypeAsync(reset));
        var withOld = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });
        Assert.Equal(HttpStatusCode.NoContent, withOld.StatusCode);
    }

    [Fact]
    public async Task Requesting_a_reset_gives_the_same_answer_for_an_email_with_no_user_and_sends_nothing()
    {
        var (browser, registered) = await api.RegisteredAndVerifiedAsync();
        var unknown = PfmApi.NewEmail();

        var forRegistered = await browser.PostAsync("/api/v1/auth/forgot-password", new { email = registered });
        var forUnknown = await browser.PostAsync("/api/v1/auth/forgot-password", new { email = unknown });

        Assert.Equal(HttpStatusCode.Accepted, forRegistered.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, forUnknown.StatusCode);
        Assert.Equal(await forRegistered.Content.ReadAsStringAsync(), await forUnknown.Content.ReadAsStringAsync());
        Assert.Empty(api.Emails.To(unknown));
    }

    [Fact]
    public async Task Resetting_the_password_ends_every_session_the_user_had()
    {
        var (signedIn, email) = await api.SignedInAsync();
        await signedIn.PostAsync("/api/v1/auth/forgot-password", new { email });

        await api.CreateBrowser().PostAsync("/api/v1/auth/reset-password", ResetFrom(api.LastLinkTo(email), NewPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task A_new_password_the_policy_refuses_is_named_and_leaves_the_link_usable()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();
        await browser.PostAsync("/api/v1/auth/forgot-password", new { email });
        var link = api.LastLinkTo(email);

        var tooShort = await browser.PostAsync("/api/v1/auth/reset-password", ResetFrom(link, "too short"));
        var retried = await browser.PostAsync("/api/v1/auth/reset-password", ResetFrom(link, NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(["password"], await ErrorFieldsAsync(tooShort));
        Assert.Equal(HttpStatusCode.NoContent, retried.StatusCode);
    }

    [Fact]
    public async Task A_tampered_reset_link_is_refused_and_changes_nothing()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();
        await browser.PostAsync("/api/v1/auth/forgot-password", new { email });
        var query = System.Web.HttpUtility.ParseQueryString(api.LastLinkTo(email).Query);

        var reset = await browser.PostAsync("/api/v1/auth/reset-password", new { userId = query["userId"], token = "tampered" + query["token"], password = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal(ResetLinkInvalid, await ProblemTypeAsync(reset));
        var withOld = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });
        Assert.Equal(HttpStatusCode.NoContent, withOld.StatusCode);
    }

    private static async Task<string[]> ErrorFieldsAsync(HttpResponseMessage response)
    {
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. json.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name)];
    }

    private const string ResetLinkInvalid = "urn:pfm:problem:reset-link-invalid";

    private static async Task<string?> ProblemTypeAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null;
    }

    /// <summary>What the SPA's reset screen posts: the user id and token from the emailed link, and the new password.</summary>
    private static object ResetFrom(Uri link, string password)
    {
        var query = System.Web.HttpUtility.ParseQueryString(link.Query);
        return new { userId = query["userId"], token = query["token"], password };
    }
}
