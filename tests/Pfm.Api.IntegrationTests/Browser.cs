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

    /// <summary>Posts with an <c>Idempotency-Key</c>, the way a client marks a request it may send again.</summary>
    public Task<HttpResponseMessage> PostAsync(string path, object body, string idempotencyKey) =>
        ChangeAsync(HttpMethod.Post, path, JsonContent.Create(body), traceparent: null, idempotencyKey: idempotencyKey);

    /// <summary>Posts a body exactly as written: JSON a serializer would not produce, or not JSON at all.</summary>
    public Task<HttpResponseMessage> PostJsonAsync(string path, string body, Traceparent? traceparent = null) =>
        PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"), traceparent);

    public Task<HttpResponseMessage> PatchAsync(string path, object body) =>
        ChangeAsync(HttpMethod.Patch, path, JsonContent.Create(body), traceparent: null);

    /// <summary>Patches with a body exactly as written.</summary>
    public Task<HttpResponseMessage> PatchJsonAsync(string path, string body) =>
        ChangeAsync(HttpMethod.Patch, path, new StringContent(body, Encoding.UTF8, "application/json"), traceparent: null);

    /// <param name="ifMatch">The <c>If-Match</c> header exactly as written, such as <c>"42"</c> in its double quotes; left out when null.</param>
    public Task<HttpResponseMessage> PutAsync(string path, object body, string? ifMatch = null) =>
        ChangeAsync(HttpMethod.Put, path, JsonContent.Create(body), traceparent: null, ifMatch);

    public Task<HttpResponseMessage> DeleteAsync(string path) =>
        ChangeAsync(HttpMethod.Delete, path, body: null, traceparent: null);

    private Task<HttpResponseMessage> PostAsync(string path, HttpContent body, Traceparent? traceparent) =>
        ChangeAsync(HttpMethod.Post, path, body, traceparent);

    private async Task<HttpResponseMessage> ChangeAsync(HttpMethod method, string path, HttpContent? body, Traceparent? traceparent, string? ifMatch = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

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
