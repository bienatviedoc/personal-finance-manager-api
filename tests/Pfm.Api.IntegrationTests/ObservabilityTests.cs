using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pfm.Infrastructure.Observability;
using Serilog.Events;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ObservabilityTests(PfmApi api)
{
    /// <summary>Open to anyone and touches nothing, so it stands for any request.</summary>
    private const string AnyPath = "/api/v1/auth/antiforgery";

    [Fact]
    public async Task A_request_and_the_database_calls_it_makes_join_the_browsers_trace()
    {
        var (browser, _) = await api.SignedInAsync();
        var traceparent = Traceparent.New();

        var response = await browser.GetAsync("/api/v1/me", traceparent);

        response.EnsureSuccessStatusCode();
        var spans = await api.Telemetry.SpansOfTraceAsync(traceparent.TraceId, spans => spans.Any(IsDatabaseCall) && spans.Any(IsRequest));
        var request = Assert.Single(spans, IsRequest);
        Assert.Equal(traceparent.SpanId, request.ParentSpanId);
        Assert.Contains(spans, IsDatabaseCall);
    }

    [Fact]
    public async Task Each_request_is_logged_once_as_a_structured_event_in_the_browsers_trace()
    {
        var traceparent = Traceparent.New();

        var response = await api.CreateBrowser().GetAsync(AnyPath, traceparent);

        response.EnsureSuccessStatusCode();
        var logs = await api.Telemetry.LogsOfTraceAsync(traceparent.TraceId, logs => logs.Any(IsRequestLog));
        var requestLog = Assert.Single(logs, IsRequestLog);
        Assert.Equal("\"GET\"", requestLog.Properties["RequestMethod"].ToString());
        Assert.Equal($"\"{AnyPath}\"", requestLog.Properties["RequestPath"].ToString());
        Assert.Equal("200", requestLog.Properties["StatusCode"].ToString());
    }

    [Fact]
    public async Task A_health_probe_that_succeeds_is_neither_logged_nor_traced()
    {
        var browser = api.CreateBrowser();
        var probe = Traceparent.New();
        var after = Traceparent.New();

        (await browser.GetAsync("/health/live", probe)).EnsureSuccessStatusCode();
        (await browser.GetAsync(AnyPath, after)).EnsureSuccessStatusCode();

        // Once the later request's log and span are in, the probe's would be too.
        await api.Telemetry.LogsOfTraceAsync(after.TraceId, logs => logs.Any(IsRequestLog));
        await api.Telemetry.SpansOfTraceAsync(after.TraceId, spans => spans.Any(IsRequest));
        Assert.Empty(await api.Telemetry.LogsOfTraceAsync(probe.TraceId, _ => true));
        Assert.Empty(await api.Telemetry.SpansOfTraceAsync(probe.TraceId, _ => true));
    }

    [Fact]
    public async Task An_unhandled_error_is_reported_once_with_its_trace_and_no_financial_content()
    {
        await using var tracker = new FakeErrorTracker();
        await using var reporting = api.ReportingErrorsTo(tracker);
        var (browser, email) = await api.SignedInAsync(PfmApi.CreateBrowser(reporting));
        var traceparent = Traceparent.New();

        var response = await browser.PostJsonAsync(
            $"{PfmApi.ThrowingPath}?payee=Corner+Bakery&amount=4815.16",
            """{"payee":"Corner Bakery","amount":4815.16,"note":"birthday cake"}""",
            traceparent);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        // Stopping the API sends every report still queued, so a second one would be here too.
        await reporting.DisposeAsync();
        var report = Assert.Single(await tracker.ReportsAsync(reports => reports.Count > 0));
        Assert.Equal("System.InvalidOperationException", ExceptionType(report));
        var traceId = traceparent.TraceId.ToString();
        Assert.Equal(traceId, report.Event.GetProperty("tags").GetProperty("trace_id").GetString());
        Assert.Equal(traceId, report.Event.GetProperty("contexts").GetProperty("trace").GetProperty("trace_id").GetString());
        // Neither the query string, the body, the user, the exception's data, nor the values of the log event's properties.
        foreach (var leaked in new[] { "Corner", "Bakery", "4815", "birthday", email, $"\"{PfmApi.ThrowingPath}\"" })
        {
            Assert.DoesNotContain(leaked, report.Raw);
        }
    }

    [Fact]
    public async Task A_failed_background_job_is_reported_once()
    {
        await using var tracker = new FakeErrorTracker();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[ObservabilitySetup.ErrorTrackerDsnSetting] = tracker.Dsn;
        // The worker's own setup: Pfm.Worker's Program.
        builder.AddPfmObservability("pfm-worker");
        builder.Services.AddHostedService<FailingJob>();

        using (var worker = builder.Build())
        {
            // A failed job stops the host.
            await worker.RunAsync();
        }

        var report = Assert.Single(await tracker.ReportsAsync(reports => reports.Count > 0));
        Assert.Equal("System.InvalidOperationException", ExceptionType(report));
    }

    private static string? ExceptionType(FakeErrorTracker.Report report) =>
        report.Event.GetProperty("exception").GetProperty("values")[0].GetProperty("type").GetString();

    private static bool IsRequestLog(LogEvent log) => log.Properties.ContainsKey("StatusCode") && log.Properties.ContainsKey("Elapsed");

    private static bool IsRequest(Activity span) => span.Kind == ActivityKind.Server;

    private static bool IsDatabaseCall(Activity span) => span.Kind == ActivityKind.Client && (span.GetTagItem("db.system.name") ?? span.GetTagItem("db.system")) is "postgresql";

    private sealed class FailingJob : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("The job failed.");
        }
    }
}
