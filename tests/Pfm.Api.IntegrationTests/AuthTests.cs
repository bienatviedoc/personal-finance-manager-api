using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(PfmApi api)
{
    [Fact]
    public async Task A_visitor_who_is_not_signed_in_cannot_see_the_signed_in_user()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Registering_sends_a_verification_link_to_the_email_address()
    {
        var email = NewEmail();

        var response = await api.CreateBrowser().PostAsync("/api/v1/auth/register", new { email, password = Password });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = Assert.Single(api.Emails.To(email));
        var link = Assert.Single(CapturedEmails.Links(message));
        Assert.Equal($"{PfmApi.PublicUrl}/verify-email", link.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task A_request_that_changes_something_is_refused_without_an_anti_forgery_token()
    {
        var email = NewEmail();

        var response = await api.CreateBrowser().Http.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(api.Emails.To(email));
    }

    [Fact]
    public async Task Signing_in_is_refused_until_the_email_address_is_confirmed()
    {
        var browser = api.CreateBrowser();
        var email = NewEmail();
        await browser.PostAsync("/api/v1/auth/register", new { email, password = Password });

        var response = await browser.PostAsync("/api/v1/auth/login", new { email, password = Password });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(EmailNotVerified, await ProblemTypeAsync(response));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task A_user_who_confirms_their_email_can_sign_in_and_see_who_they_are()
    {
        var browser = api.CreateBrowser();
        var email = NewEmail();
        await browser.PostAsync("/api/v1/auth/register", new { email, password = Password });

        var verified = await browser.PostAsync("/api/v1/auth/verify-email", VerificationFrom(LastLinkTo(email)));
        var signedIn = await browser.PostAsync("/api/v1/auth/login", new { email, password = Password });
        var me = await browser.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(email, (await me.Content.ReadFromJsonAsync<MeBody>())!.Email);
    }

    [Fact]
    public async Task Signing_in_sets_a_session_cookie_that_scripts_cannot_read_and_other_sites_cannot_send()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();

        var response = await browser.PostAsync("/api/v1/auth/login", new { email, password = Password });

        var session = Assert.Single(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("__Host-pfm-session=", StringComparison.Ordinal));
        var attributes = session.Split(';').Skip(1).Select(attribute => attribute.Trim().ToLowerInvariant()).ToArray();
        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.Contains("path=/", attributes);
        Assert.DoesNotContain(attributes, attribute => attribute.StartsWith("domain=", StringComparison.Ordinal));

        // Nothing a script could store: the session lives only in the cookie.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync("/api/v1/auth/logout");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task A_user_who_has_not_confirmed_can_ask_for_a_new_link_that_works()
    {
        var browser = api.CreateBrowser();
        var email = NewEmail();
        await browser.PostAsync("/api/v1/auth/register", new { email, password = Password });

        var resent = await browser.PostAsync("/api/v1/auth/resend-verification", new { email });

        Assert.Equal(HttpStatusCode.Accepted, resent.StatusCode);
        Assert.Equal(2, api.Emails.To(email).Count);
        var verified = await browser.PostAsync("/api/v1/auth/verify-email", VerificationFrom(LastLinkTo(email)));
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
    }

    [Fact]
    public async Task Asking_for_a_new_link_gives_the_same_answer_for_any_email_and_sends_nothing_needless()
    {
        var unknown = NewEmail();
        var (browser, confirmed) = await api.RegisteredAndVerifiedAsync();
        var sentBefore = api.Emails.To(confirmed).Count;

        var forUnknown = await browser.PostAsync("/api/v1/auth/resend-verification", new { email = unknown });
        var forConfirmed = await browser.PostAsync("/api/v1/auth/resend-verification", new { email = confirmed });

        Assert.Equal(HttpStatusCode.Accepted, forUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, forConfirmed.StatusCode);
        Assert.Empty(api.Emails.To(unknown));
        Assert.Equal(sentBefore, api.Emails.To(confirmed).Count);
    }

    [Fact]
    public async Task Registering_an_email_that_already_has_a_user_looks_the_same_and_changes_nothing()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();
        var sentBefore = api.Emails.To(email).Count;

        var response = await api.CreateBrowser().PostAsync("/api/v1/auth/register", new { email, password = "an attacker's new password" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var notice = Assert.Single(api.Emails.To(email).Skip(sentBefore));
        Assert.DoesNotContain(CapturedEmails.Links(notice), link => link.AbsolutePath == "/verify-email");
        Assert.Contains(CapturedEmails.Links(notice), link => link.AbsoluteUri == $"{PfmApi.PublicUrl}/login");

        var withNewPassword = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = "an attacker's new password" });
        var withOriginalPassword = await browser.PostAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, withNewPassword.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, withOriginalPassword.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email", Password, "email")]
    [InlineData("", Password, "email")]
    [InlineData("   ", Password, "email")]
    [InlineData("Someone <someone@example.com>", Password, "email")]
    [InlineData("someone@example.com", "too short", "password")]
    public async Task Registering_with_invalid_details_names_the_field_to_fix(string email, string password, string field)
    {
        var response = await api.CreateBrowser().PostAsync("/api/v1/auth/register", new { email, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task A_short_password_is_refused_the_same_way_whether_or_not_the_email_has_a_user()
    {
        var (_, email) = await api.RegisteredAndVerifiedAsync();

        var response = await api.CreateBrowser().PostAsync("/api/v1/auth/register", new { email, password = "too short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["password"], await ErrorFieldsAsync(response));
    }

    [Theory]
    [InlineData("/api/v1/auth/register", "{}", "email,password")]
    [InlineData("/api/v1/auth/register", "{\"email\":null,\"password\":\"correct horse battery staple\"}", "email")]
    [InlineData("/api/v1/auth/login", "{}", "email,password")]
    [InlineData("/api/v1/auth/login", "{\"email\":\"someone@example.com\"}", "password")]
    [InlineData("/api/v1/auth/login", "{\"email\":\"   \",\"password\":\"correct horse battery staple\"}", "email")]
    [InlineData("/api/v1/auth/resend-verification", "{}", "email")]
    [InlineData("/api/v1/auth/resend-verification", "{\"email\":\"   \"}", "email")]
    [InlineData("/api/v1/auth/verify-email", "{}", "token,userId")]
    [InlineData("/api/v1/auth/verify-email", "{\"userId\":\"0196f3a0-0000-7000-8000-000000000000\",\"token\":\"\"}", "token")]
    [InlineData("/api/v1/auth/verify-email", "{\"userId\":\"00000000-0000-0000-0000-000000000000\",\"token\":\"a-token\"}", "userId")]
    public async Task A_missing_field_is_named_in_a_bad_request(string path, string body, string fields)
    {
        var browser = api.CreateBrowser();

        var response = await browser.PostJsonAsync(path, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(fields.Split(','), (await ErrorFieldsAsync(response)).Order());
    }

    private static async Task<string[]> ErrorFieldsAsync(HttpResponseMessage response)
    {
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. json.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name)];
    }

    [Fact]
    public async Task A_tampered_verification_link_is_refused_and_confirms_nothing()
    {
        var browser = api.CreateBrowser();
        var email = NewEmail();
        await browser.PostAsync("/api/v1/auth/register", new { email, password = Password });
        var query = System.Web.HttpUtility.ParseQueryString(LastLinkTo(email).Query);

        var response = await browser.PostAsync("/api/v1/auth/verify-email", new { userId = query["userId"], token = "tampered" + query["token"] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var signIn = await browser.PostAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, signIn.StatusCode);
    }

    [Fact]
    public void Only_registering_signing_in_and_their_helpers_are_open_to_visitors_who_are_not_signed_in()
    {
        var open = api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => $"{string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])} {endpoint.RoutePattern.RawText}")
            .Order()
            .ToArray();

        Assert.Equal(
            [
                "* /health/live",
                "* /health/ready",
                "GET /api/v1/auth/antiforgery",
                "GET /openapi/{documentName}.json",
                "POST /api/v1/auth/login",
                "POST /api/v1/auth/register",
                "POST /api/v1/auth/resend-verification",
                "POST /api/v1/auth/verify-email",
            ],
            open);
    }

    [Fact]
    public async Task In_development_over_plain_http_a_user_can_register_confirm_and_sign_in()
    {
        var browser = api.CreateDevelopmentBrowserOverHttp();
        var email = NewEmail();

        Assert.Equal(HttpStatusCode.Accepted, (await browser.PostAsync("/api/v1/auth/register", new { email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/v1/auth/verify-email", VerificationFrom(LastLinkTo(email)))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/v1/auth/login", new { email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task A_session_ends_once_the_users_security_stamp_changes()
    {
        var (browser, email) = await api.SignedInAsync();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/me")).StatusCode);

        // What a password reset or deactivation does to every session the user has.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Pfm.Infrastructure.Identity.User>>();
            await users.UpdateSecurityStampAsync((await users.FindByEmailAsync(email))!);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task An_email_with_surrounding_spaces_is_the_same_address()
    {
        var browser = api.CreateBrowser();
        var email = NewEmail();

        await browser.PostAsync("/api/v1/auth/register", new { email = $"  {email} ", password = Password });
        await browser.PostAsync("/api/v1/auth/register", new { email, password = "a different password" });

        var sent = api.Emails.To(email);
        Assert.Equal(2, sent.Count);
        Assert.Contains(CapturedEmails.Links(sent[0]), link => link.AbsolutePath == "/verify-email");
        Assert.DoesNotContain(CapturedEmails.Links(sent[1]), link => link.AbsolutePath == "/verify-email");

        await browser.PostAsync("/api/v1/auth/verify-email", VerificationFrom(CapturedEmails.Links(sent[0]).Single()));
        var signIn = await browser.PostAsync("/api/v1/auth/login", new { email = $" {email}  ", password = Password });
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
    }

    [Fact]
    public async Task Registering_the_same_email_at_the_same_moment_answers_every_request_the_same_way()
    {
        var email = NewEmail();
        var browsers = Enumerable.Range(0, 12).Select(_ => api.CreateBrowser()).ToList();
        var tokens = await Task.WhenAll(browsers.Select(browser => browser.AntiforgeryTokenAsync()));

        var responses = await Task.WhenAll(browsers.Select((browser, i) =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register") { Content = JsonContent.Create(new { email, password = Password }) };
            request.Headers.Add("X-XSRF-TOKEN", tokens[i]);
            return browser.Http.SendAsync(request);
        }));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
    }

    [Fact]
    public async Task Registering_an_existing_email_takes_about_as_long_as_registering_a_new_one()
    {
        // Without this, response time alone would reveal which emails have a user. Medians over several
        // runs; the bar is loose (half as long) because hashing a password dominates both paths.
        var (_, existing) = await api.RegisteredAndVerifiedAsync();
        var browser = api.CreateBrowser();
        await TimeAsync(browser, NewEmail());

        var forExisting = new List<double>();
        var forNew = new List<double>();
        for (var run = 0; run < 7; run++)
        {
            forExisting.Add(await TimeAsync(browser, existing));
            forNew.Add(await TimeAsync(browser, NewEmail()));
        }

        Assert.True(Median(forExisting) >= Median(forNew) / 2, $"existing {Median(forExisting):F1} ms, new {Median(forNew):F1} ms");
    }

    private static async Task<double> TimeAsync(Browser browser, string email)
    {
        var token = await browser.AntiforgeryTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register") { Content = JsonContent.Create(new { email, password = Password }) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var response = await browser.Http.SendAsync(request);
        clock.Stop();
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return clock.Elapsed.TotalMilliseconds;
    }

    private static double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);

    private sealed record MeBody(Guid Id, string Email);

    private const string EmailNotVerified = "urn:pfm:problem:email-not-verified";

    private static async Task<string?> ProblemTypeAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null;
    }

    private const string Password = PfmApi.Password;

    private static string NewEmail() => PfmApi.NewEmail();

    private Uri LastLinkTo(string email) => api.LastLinkTo(email);

    private static object VerificationFrom(Uri link) => PfmApi.VerificationFrom(link);
}
