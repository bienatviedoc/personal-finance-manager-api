using System.Diagnostics;
using Sentry;
using Sentry.Serilog;
using Serilog.Events;

namespace Pfm.Infrastructure.Observability;

/// <summary>
/// Reports unhandled errors to the error tracker (Sentry). The tracker is a third party, so a
/// report says what failed and where, never what the user was doing it with: it carries the
/// exception (type, message, stack), the log message's template and the trace id. The request, the
/// user, log property values and rendered log messages stay behind, in our own logs and traces,
/// where the trace id finds them.
/// </summary>
/// <remarks>The Sentry SDK is process-wide; it is closed, flushing what is queued, when the host's services are disposed.</remarks>
internal sealed class ErrorReports(string dsn, string environment, Action<SentryOptions>? configureForHost) : IDisposable
{
    private IDisposable? _sdk;

    public void Start() => _sdk ??= SentrySdk.Init(Configure);

    public void Dispose() => _sdk?.Dispose();

    public static void ConfigureSink(SentrySerilogOptions sink)
    {
        sink.MinimumEventLevel = LogEventLevel.Error;
        // Breadcrumbs are earlier log lines, rendered with their values.
        sink.MinimumBreadcrumbLevel = LogEventLevel.Fatal;
    }

    private void Configure(SentryOptions sentry)
    {
        sentry.Dsn = dsn;
        sentry.Environment = environment;
        sentry.SendDefaultPii = false;
        sentry.MaxBreadcrumbs = 0;
        sentry.ServerName = "";
        sentry.SetBeforeSend(Scrub);
        sentry.UseSerilog();

        configureForHost?.Invoke(sentry);
    }

    /// <summary>Runs as the error is logged, so the trace it happened in is still current.</summary>
    private static SentryEvent? Scrub(SentryEvent report, SentryHint hint)
    {
        // Only errors that carry an exception: a failed request's own log line would repeat it.
        if (report.Exception is null)
        {
            return null;
        }

        // The template ("Transaction {Id} failed"), never the message rendered with its values.
        report.Message = report.Message?.Message is { } template ? new SentryMessage { Message = template } : null;
        // Exception.Data, copied into the report, holds whatever the thrower put there.
        foreach (var exception in report.SentryExceptions ?? [])
        {
            exception.Mechanism?.Data.Clear();
        }

        // Its "sentry:context:" entries become contexts of their own; only the SDK's contexts (runtime, OS, trace) stay.
        foreach (var (name, _) in report.Contexts.Where(context => context.Value.GetType().Assembly != typeof(SentryEvent).Assembly).ToList())
        {
            report.Contexts.Remove(name);
        }

        report.User = new SentryUser();
        report.Request = new SentryRequest();

        // Log properties arrive as extras and tags. Their names are code; their values may be anything.
        foreach (var key in report.Extra.Keys.ToList())
        {
            report.SetExtra(key, null);
        }

        foreach (var key in report.Tags.Keys.ToList())
        {
            report.UnsetTag(key);
        }

        if (Activity.Current is { } activity)
        {
            var traceId = activity.TraceId.ToString();
            report.SetTag("trace_id", traceId);
            report.Contexts.Trace.TraceId = SentryId.Parse(traceId);
            report.Contexts.Trace.SpanId = SpanId.Parse(activity.SpanId.ToString());
        }

        return report;
    }
}
