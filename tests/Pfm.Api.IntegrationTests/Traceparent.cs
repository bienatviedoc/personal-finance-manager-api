using System.Diagnostics;

namespace Pfm.Api.IntegrationTests;

/// <summary>The trace and span a request comes from, as the browser sends them in the traceparent header.</summary>
public sealed record Traceparent(ActivityTraceId TraceId, ActivitySpanId SpanId)
{
    /// <summary>A new trace, sampled, as the SPA starts one for each request.</summary>
    public static Traceparent New() => new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom());

    public string Header => $"00-{TraceId}-{SpanId}-01";
}
