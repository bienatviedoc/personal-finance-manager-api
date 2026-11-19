using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Sentry;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Core;
using Serilog.Formatting.Compact;

namespace Pfm.Infrastructure.Observability;

public static class ObservabilitySetup
{
    /// <summary>Where traces, metrics and logs are sent over OTLP. Nothing is sent when it is not set.</summary>
    public const string OtlpEndpointSetting = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>The error tracker's (Sentry) DSN. No errors are reported when it is not set.</summary>
    public const string ErrorTrackerDsnSetting = "Sentry:Dsn";

    /// <summary>
    /// Structured logs through Serilog, and traces and metrics through OpenTelemetry, shared by the
    /// API and the worker. A host adds the instrumentation only it has (the API adds incoming
    /// requests) through the returned builder.
    /// </summary>
    /// <param name="configureErrorReports">Host-specific Sentry options for error reports, such as exceptions not worth reporting.</param>
    public static OpenTelemetryBuilder AddPfmObservability(this IHostApplicationBuilder builder, string serviceName, Action<SentryOptions>? configureErrorReports = null)
    {
        builder.AddLogging(serviceName, configureErrorReports);

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddRuntimeInstrumentation()
                .AddHttpClientInstrumentation()
                .AddNpgsqlInstrumentation());

        if (IsOtlpConfigured(builder.Configuration))
        {
            telemetry.UseOtlpExporter();
        }

        return telemetry;
    }

    /// <summary>
    /// Every log event is structured and carries the trace and span it was written in. Levels come
    /// from the <c>Serilog</c> configuration section; sinks a host registers as
    /// <see cref="ILogEventSink"/> are added too.
    /// </summary>
    private static void AddLogging(this IHostApplicationBuilder builder, string serviceName, Action<SentryOptions>? configureErrorReports)
    {
        var configuration = builder.Configuration;
        var development = builder.Environment.IsDevelopment();

        var errorTrackerDsn = configuration[ErrorTrackerDsnSetting];
        if (!string.IsNullOrWhiteSpace(errorTrackerDsn))
        {
            // Built by the container, not handed to it, so the container disposes it, flushing queued reports.
            var environment = builder.Environment.EnvironmentName;
            builder.Services.AddSingleton(_ => new ErrorReports(errorTrackerDsn, environment, configureErrorReports));
        }

        // The static Log.Logger is left alone, so several hosts can run in one process (tests do).
        builder.Services.AddSerilog((services, logger) =>
        {
            logger
                .ReadFrom.Configuration(configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();

            // Readable on a developer's console; one JSON object per event everywhere else.
            if (development)
            {
                logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj} {TraceId}{NewLine}{Exception}");
            }
            else
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }

            if (IsOtlpConfigured(configuration))
            {
                logger.WriteTo.OpenTelemetry(otlp =>
                {
                    otlp.Endpoint = configuration[OtlpEndpointSetting];
                    otlp.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = serviceName };
                });
            }

            // Started with the logger, so it is ready for the first error.
            if (services.GetService<ErrorReports>() is { } errorReports)
            {
                errorReports.Start();
                logger.WriteTo.Sentry(ErrorReports.ConfigureSink);
            }
        }, preserveStaticLogger: true);
    }

    private static bool IsOtlpConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[OtlpEndpointSetting]);
}
