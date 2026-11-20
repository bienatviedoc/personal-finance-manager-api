using System.Text.Json;

namespace Pfm.Api.IntegrationTests;

public static class ProblemResponses
{
    /// <summary>The fields a validation problem names, in alphabetical order.</summary>
    public static async Task<string[]> ErrorFieldsAsync(this HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. json.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).Order()];
    }

    /// <summary>The problem's type, null when it has none of its own.</summary>
    public static async Task<string?> ProblemTypeAsync(this HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("type", out var type) && type.GetString() is { } named && named.StartsWith("urn:", StringComparison.Ordinal) ? named : null;
    }

    public static async Task<string?> ProblemTitleAsync(this HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("title").GetString();
    }
}
