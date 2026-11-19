using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Pfm.Api;
using Pfm.Api.Accounts;
using Pfm.Api.Identity;
using Pfm.Api.Transactions;
using Pfm.Application;
using Pfm.Infrastructure;
using Pfm.Infrastructure.Observability;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Health probes arrive every few seconds and say nothing new while they succeed, so they are not traced.
// Not reporting BadHttpRequestException: see the exception handler below.
builder.AddPfmObservability("pfm-api", configureErrorReports: sentry => sentry.AddExceptionFilterForType<BadHttpRequestException>())
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation(requests => requests.Filter = request => !IsHealthProbe(request)))
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
var cookies = CookiePolicy.For(builder.Environment);
builder.Services.AddPfmAuthentication(cookies);
builder.Services.AddPfmAntiforgery(cookies);
builder.Services.AddPfmJson();
builder.Services.AddProblemDetails();
// Enums travel as camelCase names ("income"), never as numbers.
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddOpenApi("v1");

var app = builder.Build();

// `dotnet Pfm.Api.dll migrate` applies migrations and exits. Deployments run it as a
// separate step before the new API version starts; the API never migrates on start-up.
if (args is ["migrate"])
{
    await app.Services.MigrateDatabaseAsync();
    return;
}

// One structured event per request, carrying its method, path (never the query string), status and time.
// A health probe is logged only when it fails.
app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.GetLevel = (request, _, exception) =>
        exception is not null || request.Response.StatusCode >= StatusCodes.Status500InternalServerError ? LogEventLevel.Error
        : IsHealthProbe(request) ? LogEventLevel.Verbose
        : LogEventLevel.Information;
});

// Every error leaves as RFC 9457 Problem Details: unhandled exceptions and bare status codes alike.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // A body that cannot be read is the caller's mistake. Development throws for it (to show the
    // cause in the log) but the caller still gets a 400, as in production.
    StatusCodeSelector = exception => exception is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();
app.UseApiAntiforgery();

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

// Live: the process is up. Ready: its dependencies (the database) are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(InfrastructureServices.ReadyTag),
}).AllowAnonymous();

app.MapGroup("/api/v1")
    .DocumentAntiforgeryRefusals()
    .MapIdentityEndpoints()
    .MapProfileEndpoints()
    .MapInstitutionEndpoints()
    .MapCategoryEndpoints();

app.Run();

static bool IsHealthProbe(HttpContext request) => request.Request.Path.StartsWithSegments("/health");

public partial class Program;
