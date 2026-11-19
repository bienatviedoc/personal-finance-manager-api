using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry;
using Serilog.Core;
using Serilog.Events;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// The spans and log events the API exported, captured in memory alongside wherever the API sends
/// them. Only what the API's own telemetry pipeline exports arrives here.
/// </summary>
public sealed class CapturedTelemetry
{
    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly ConcurrentQueue<LogEvent> _logs = new();

    public SpanExporter Spans => new(_spans);

    public LogSink Logs => new(_logs);

    /// <summary>A traceparent header for a new trace, as the browser sends it, and that trace's id.</summary>
    public static (string Header, ActivityTraceId TraceId) NewTraceparent()
    {
        var traceId = ActivityTraceId.CreateRandom();
        return ($"00-{traceId}-{ActivitySpanId.CreateRandom()}-01", traceId);
    }

    /// <summary>
    /// The spans of one trace. A span is exported when it ends, which can be just after the response
    /// reaches the client, so this waits until <paramref name="until"/> holds or a few seconds pass.
    /// </summary>
    public Task<List<Activity>> SpansOfTraceAsync(ActivityTraceId traceId, Func<List<Activity>, bool> until) =>
        Eventually.ReadAsync(() => _spans.Where(span => span.TraceId == traceId).ToList(), until);

    /// <summary>The log events written while serving one trace, waiting as for spans.</summary>
    public Task<List<LogEvent>> LogsOfTraceAsync(ActivityTraceId traceId, Func<List<LogEvent>, bool> until) =>
        Eventually.ReadAsync(() => _logs.Where(log => log.TraceId == traceId).ToList(), until);

    public sealed class SpanExporter(ConcurrentQueue<Activity> spans) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var span in batch)
            {
                spans.Enqueue(span);
            }

            return ExportResult.Success;
        }
    }

    public sealed class LogSink(ConcurrentQueue<LogEvent> logs) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => logs.Enqueue(logEvent);
    }
}
