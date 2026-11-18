using System.Net;
using System.Text.Json;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OpenApiTests(PfmApi api)
{
    [Fact]
    public async Task The_OpenAPI_document_is_served()
    {
        var response = await api.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.StartsWith("3.", document.GetProperty("openapi").GetString());
    }
}
