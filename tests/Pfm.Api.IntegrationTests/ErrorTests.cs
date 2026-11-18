using System.Net;
using System.Text.Json;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ErrorTests(PfmApi api)
{
    [Fact]
    public async Task An_unknown_route_returns_problem_details()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblem(response);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task An_unhandled_exception_returns_problem_details_without_leaking_the_cause()
    {
        var response = await api.CreateClient().GetAsync(PfmApi.ThrowingPath);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var problem = await ReadProblem(response);
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.DoesNotContain("secret internal detail", body);
    }

    private static async Task<JsonElement> ReadProblem(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
