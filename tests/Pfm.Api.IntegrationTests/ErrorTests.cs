using System.Net;
using System.Text.Json;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ErrorTests(PfmApi api)
{
    [Fact]
    public async Task An_unknown_route_asks_a_visitor_to_sign_in_without_revealing_whether_it_exists()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ReadProblem(response);
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task An_unknown_route_returns_problem_details_to_a_signed_in_user()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblem(response);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task An_unhandled_exception_returns_problem_details_without_leaking_the_cause()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.GetAsync(PfmApi.ThrowingPath);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var problem = await ReadProblem(response);
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.DoesNotContain("secret internal detail", body);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("{ not json", false)]
    [InlineData("", true)]
    [InlineData("{ not json", true)]
    public async Task A_request_whose_body_cannot_be_read_is_a_bad_request_not_a_server_error(string body, bool development)
    {
        var browser = development ? api.CreateDevelopmentBrowserOverHttp() : api.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await browser.AntiforgeryTokenAsync());

        var response = await browser.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(400, (await ReadProblem(response)).GetProperty("status").GetInt32());
    }

    private static async Task<JsonElement> ReadProblem(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
