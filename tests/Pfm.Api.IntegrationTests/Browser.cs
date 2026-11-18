using System.Net.Http.Json;
using System.Text.Json;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// One browser using the API the way the SPA does: over HTTPS, keeping cookies, and sending the
/// anti-forgery token with every request that changes something.
/// </summary>
public sealed class Browser(HttpClient http)
{
    public HttpClient Http => http;

    public Task<HttpResponseMessage> GetAsync(string path) => http.GetAsync(path);

    public async Task<HttpResponseMessage> PostAsync(string path, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("X-XSRF-TOKEN", await AntiforgeryTokenAsync());
        return await http.SendAsync(request);
    }

    /// <summary>Fetched per request, because the token is tied to who is signed in.</summary>
    public async Task<string> AntiforgeryTokenAsync()
    {
        var response = await http.GetAsync("/api/v1/auth/antiforgery");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }
}
