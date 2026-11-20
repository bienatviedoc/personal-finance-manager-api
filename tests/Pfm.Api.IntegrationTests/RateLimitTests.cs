using System.Net;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// Each test runs on its own copy of the API with a small limit, so they cannot use up each other's attempts, and
/// the copy every other test shares has a limit they never reach.
/// </summary>
[Collection(ApiCollection.Name)]
public class RateLimitTests(PfmApi api)
{
    private const int Limit = 3;

    [Fact]
    public async Task A_client_that_uses_up_its_sign_in_attempts_is_told_to_wait()
    {
        var browser = PfmApi.CreateBrowser(api.WithAuthRateLimit(Limit, TimeSpan.FromMinutes(1)));
        var (_, email) = await api.RegisteredAndVerifiedAsync();
        var allowed = await AttemptsAsync(browser, email, Limit);

        var refused = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

        Assert.All(allowed, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        Assert.InRange(refused.Headers.RetryAfter!.Delta!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
        // The right password got nowhere: the request was turned away before it was looked at.
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me")).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/auth/register")]
    [InlineData("/api/v1/auth/verify-email")]
    [InlineData("/api/v1/auth/resend-verification")]
    [InlineData("/api/v1/auth/forgot-password")]
    [InlineData("/api/v1/auth/reset-password")]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/me/password")]
    [InlineData("/api/v1/me/two-factor/setup")]
    [InlineData("/api/v1/me/two-factor/enable")]
    [InlineData("/api/v1/me/two-factor/disable")]
    [InlineData("/api/v1/me/two-factor/recovery-codes")]
    public async Task Every_endpoint_that_takes_a_password_a_code_or_a_link_or_sends_email_shares_the_limit(string path)
    {
        var browser = PfmApi.CreateBrowser(api.WithAuthRateLimit(Limit, TimeSpan.FromMinutes(1)));
        await AttemptsAsync(browser, PfmApi.NewEmail(), Limit);

        var response = await browser.PostAsync(path);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Everything_else_carries_on_for_a_client_that_is_over_the_limit()
    {
        var limited = api.WithAuthRateLimit(Limit, TimeSpan.FromMinutes(1));
        var (browser, _) = await api.SignedInAsync(PfmApi.CreateBrowser(limited));
        await AttemptsAsync(browser, PfmApi.NewEmail(), Limit);

        var me = await browser.GetAsync("/api/v1/me");
        var institution = await browser.PostAsync("/api/v1/institutions", new { name = "Vietcombank" });
        var signOut = await browser.PostAsync("/api/v1/auth/logout");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(HttpStatusCode.Created, institution.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
    }

    [Fact]
    public async Task The_attempts_come_back_when_the_window_is_over()
    {
        var browser = PfmApi.CreateBrowser(api.WithAuthRateLimit(Limit, TimeSpan.FromSeconds(5)));
        var (_, email) = await api.RegisteredAndVerifiedAsync();
        await AttemptsAsync(browser, email, Limit);
        var refused = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

        // Wait as long as the answer says to, as a client would.
        await Task.Delay(refused.Headers.RetryAfter?.Delta ?? TimeSpan.Zero);
        var afterwards = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, afterwards.StatusCode);
    }

    [Fact]
    public async Task One_client_using_up_its_attempts_leaves_another_clients_alone()
    {
        var limited = api.WithAuthRateLimit(Limit, TimeSpan.FromMinutes(1));
        var (_, email) = await api.RegisteredAndVerifiedAsync();
        await AttemptsAsync(PfmApi.CreateBrowser(limited, clientAddress: "203.0.113.7"), email, Limit + 1);

        var other = await PfmApi.CreateBrowser(limited, clientAddress: "203.0.113.8").PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

        Assert.Equal(HttpStatusCode.NoContent, other.StatusCode);
    }

    private static async Task<List<HttpResponseMessage>> AttemptsAsync(Browser browser, string email, int times)
    {
        var responses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < times; attempt++)
        {
            responses.Add(await browser.PostAsync("/api/v1/auth/login", new { email, password = "not the right password" }));
        }

        return responses;
    }
}
