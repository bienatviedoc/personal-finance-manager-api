using System.Diagnostics;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ObservabilityTests(PfmApi api)
{
    [Fact]
    public async Task A_request_and_the_database_calls_it_makes_join_the_browsers_trace()
    {
        var (browser, _) = await api.SignedInAsync();
        var (traceparent, traceId) = CapturedTelemetry.NewTraceparent();

        var response = await SendAsync(browser.Http, HttpMethod.Get, "/api/v1/me", traceparent);

        response.EnsureSuccessStatusCode();
        var spans = await api.Telemetry.SpansOfTraceAsync(traceId, spans => spans.Any(IsDatabaseCall) && spans.Any(IsRequest));
        var request = Assert.Single(spans, IsRequest);
        Assert.Equal(traceparent.Split('-')[2], request.ParentSpanId.ToString());
        Assert.Contains(spans, IsDatabaseCall);
    }

    [Fact]
    public async Task Each_request_is_logged_once_as_a_structured_event_in_the_browsers_trace()
    {
        var (traceparent, traceId) = CapturedTelemetry.NewTraceparent();

        var response = await SendAsync(api.CreateClient(), HttpMethod.Get, "/health/live", traceparent);

        response.EnsureSuccessStatusCode();
        var logs = await api.Telemetry.LogsOfTraceAsync(traceId, logs => logs.Any(IsRequestLog));
        var requestLog = Assert.Single(logs, IsRequestLog);
        Assert.Equal("\"GET\"", requestLog.Properties["RequestMethod"].ToString());
        Assert.Equal("\"/health/live\"", requestLog.Properties["RequestPath"].ToString());
        Assert.Equal("200", requestLog.Properties["StatusCode"].ToString());
    }

    [Fact]
    public async Task An_unhandled_error_is_reported_once_with_its_trace_and_no_financial_content()
    {
        await using var tracker = new FakeErrorTracker();
        await using var reporting = api.ReportingErrorsTo(tracker);
        var (browser, email) = await api.SignedInAsync(PfmApi.CreateBrowser(reporting));
        var (traceparent, traceId) = CapturedTelemetry.NewTraceparent();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{PfmApi.ThrowingPath}?payee=Corner+Bakery&amount=4815.16")
        {
            Content = new StringContent("""{"payee":"Corner Bakery","amount":4815.16,"note":"birthday cake"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("traceparent", traceparent);
        request.Headers.Add("X-XSRF-TOKEN", await browser.AntiforgeryTokenAsync());

        var response = await browser.Http.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        await tracker.ReportsAsync(reports => reports.Count > 0);
        // Anything else would have arrived by now, alongside the first.
        await Task.Delay(500);
        var report = Assert.Single(await tracker.ReportsAsync(_ => true));
        var exception = report.Event.GetProperty("exception").GetProperty("values")[0];
        Assert.Equal("System.InvalidOperationException", exception.GetProperty("type").GetString());
        Assert.Equal(traceId.ToString(), report.Event.GetProperty("tags").GetProperty("trace_id").GetString());
        Assert.Equal(traceId.ToString(), report.Event.GetProperty("contexts").GetProperty("trace").GetProperty("trace_id").GetString());
        // Neither the query string, the body, the user, nor the values of the log event's properties.
        foreach (var leaked in new[] { "Corner", "Bakery", "4815", "birthday", email, $"\"{PfmApi.ThrowingPath}\"" })
        {
            Assert.DoesNotContain(leaked, report.Raw);
        }
    }

    private static bool IsRequestLog(Serilog.Events.LogEvent log) => log.Properties.ContainsKey("StatusCode") && log.Properties.ContainsKey("Elapsed");

    private static bool IsRequest(Activity span) => span.Kind == ActivityKind.Server;

    private static bool IsDatabaseCall(Activity span) => span.Kind == ActivityKind.Client && (span.GetTagItem("db.system.name") ?? span.GetTagItem("db.system")) is "postgresql";

    private static Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path, string traceparent)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("traceparent", traceparent);
        return http.SendAsync(request);
    }
}
