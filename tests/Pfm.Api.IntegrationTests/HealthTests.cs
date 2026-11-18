using System.Net;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class HealthTests(PfmApi api)
{
    [Fact]
    public async Task Live_reports_healthy()
    {
        var response = await api.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_reports_healthy_when_the_database_is_reachable()
    {
        var response = await api.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_reports_unavailable_when_the_database_is_unreachable_while_live_stays_healthy()
    {
        await using var broken = api.WithUnreachableDatabase();
        var client = broken.CreateClient();

        var ready = await client.GetAsync("/health/ready");
        var live = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }
}
