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

    [Fact]
    public async Task Every_operation_that_changes_something_documents_its_bad_request_answer()
    {
        // Any of them can answer 400 for a missing anti-forgery token or a body that cannot be read.
        var response = await api.CreateClient().GetAsync("/openapi/v1.json");
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        var undocumented = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Name is "post" or "put" or "patch" or "delete")
                .Where(operation => !operation.Value.GetProperty("responses").TryGetProperty("400", out _))
                .Select(operation => $"{operation.Name.ToUpperInvariant()} {path.Name}"))
            .ToArray();

        Assert.Empty(undocumented);
    }

    [Fact]
    public async Task The_contract_names_no_Application_type()
    {
        // Requests and responses are the API's own types, so changing a use case cannot change the contract unseen.
        var response = await api.CreateClient().GetAsync("/openapi/v1.json");
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var applicationTypes = typeof(Pfm.Application.ApplicationServices).Assembly.GetTypes().Select(type => type.Name).ToHashSet();

        var schemas = document.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(schema => schema.Name);

        Assert.DoesNotContain(schemas, applicationTypes.Contains);
    }
}
