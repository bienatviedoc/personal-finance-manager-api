using System.Net;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SecurityHeadersTests(PfmApi api)
{
    [Theory]
    [InlineData("/api/v1/auth/antiforgery", false, HttpStatusCode.OK)]
    [InlineData("/api/v1/me", false, HttpStatusCode.Unauthorized)]
    [InlineData("/api/v1/me", true, HttpStatusCode.OK)]
    [InlineData("/api/v1/no-such-thing", true, HttpStatusCode.NotFound)]
    [InlineData(PfmApi.ThrowingPath, true, HttpStatusCode.InternalServerError)]
    [InlineData("/health/live", false, HttpStatusCode.OK)]
    [InlineData("/openapi/v1.json", false, HttpStatusCode.OK)]
    public async Task Every_answer_tells_the_browser_to_treat_it_strictly(string path, bool signedIn, HttpStatusCode status)
    {
        var browser = signedIn ? (await api.SignedInAsync()).Browser : api.CreateBrowser();

        var response = await browser.GetAsync(path);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("max-age=31536000", Header(response, "Strict-Transport-Security"));
    }

    [Fact]
    public async Task The_content_security_policy_lets_an_answer_load_run_and_be_framed_by_nothing()
    {
        var response = await api.CreateBrowser().GetAsync("/api/v1/auth/antiforgery");

        var directives = Header(response, "Content-Security-Policy")!.Split(';', StringSplitOptions.TrimEntries);
        Assert.Contains("default-src 'none'", directives);
        Assert.Contains("frame-ancestors 'none'", directives);
    }

    [Fact]
    public async Task An_answer_that_was_refused_carries_the_headers_too()
    {
        // Without the anti-forgery token: refused by middleware, before any endpoint.
        var response = await api.CreateBrowser().Http.PostAsync("/api/v1/auth/login", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.NotNull(Header(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task What_the_API_answers_is_never_kept_in_a_cache()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync("/api/v1/me");

        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task In_development_over_plain_http_no_promise_to_use_https_is_made()
    {
        var response = await api.CreateDevelopmentBrowserOverHttp().GetAsync("/api/v1/auth/antiforgery");

        Assert.Null(Header(response, "Strict-Transport-Security"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values) ? string.Join(",", values) : null;
}
