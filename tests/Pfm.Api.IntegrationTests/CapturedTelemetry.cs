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

    public CapturedTelemetry()
    {
        SpanExporter = new CapturingSpanExporter(_spans);
        LogSink = new CapturingLogSink(_logs);
    }

    /// <summary>Added to the API's tracing pipeline; fills <see cref="SpansOfTraceAsync"/>.</summary>
    public BaseExporter<Activity> SpanExporter { get; }

    /// <summary>Added to the API's logger; fills <see cref="LogsOfTraceAsync"/>.</summary>
    public ILogEventSink LogSink { get; }

    /// <summary>
    /// The spans of one trace. A span is exported when it ends, which can be just after the response
    /// reaches the client, so this waits until <paramref name="until"/> holds or a few seconds pass.
    /// </summary>
    public Task<List<Activity>> SpansOfTraceAsync(ActivityTraceId traceId, Func<List<Activity>, bool> until) =>
        Eventually.ReadAsync(() => _spans.Where(span => span.TraceId == traceId).ToList(), until);

    /// <summary>The log events written while serving one trace, waiting as for spans.</summary>
    public Task<List<LogEvent>> LogsOfTraceAsync(ActivityTraceId traceId, Func<List<LogEvent>, bool> until) =>
        Eventually.ReadAsync(() => _logs.Where(log => log.TraceId == traceId).ToList(), until);

    private sealed class CapturingSpanExporter(ConcurrentQueue<Activity> spans) : BaseExporter<Activity>
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

    private sealed class CapturingLogSink(ConcurrentQueue<LogEvent> logs) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => logs.Enqueue(logEvent);
    }
}
