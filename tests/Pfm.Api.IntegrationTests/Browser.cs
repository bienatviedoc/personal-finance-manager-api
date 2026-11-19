using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// One browser using the API the way the SPA does: over HTTPS, keeping cookies, and sending the
/// anti-forgery token with every request that changes something.
/// </summary>
public sealed class Browser(HttpClient http)
{
    public HttpClient Http => http;

    public async Task<HttpResponseMessage> GetAsync(string path, Traceparent? traceparent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync(request, traceparent);
    }

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null) =>
        PostAsync(path, JsonContent.Create(body ?? new { }), traceparent: null);

    /// <summary>Posts a body exactly as written: JSON a serializer would not produce, or not JSON at all.</summary>
    public Task<HttpResponseMessage> PostJsonAsync(string path, string body, Traceparent? traceparent = null) =>
        PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"), traceparent);

    private async Task<HttpResponseMessage> PostAsync(string path, HttpContent body, Traceparent? traceparent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body };
        request.Headers.Add("X-XSRF-TOKEN", await AntiforgeryTokenAsync());
        return await SendAsync(request, traceparent);
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Traceparent? traceparent)
    {
        if (traceparent is not null)
        {
            request.Headers.Add("traceparent", traceparent.Header);
        }

        return http.SendAsync(request);
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
