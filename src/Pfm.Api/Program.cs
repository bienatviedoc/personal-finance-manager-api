using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Pfm.Application;
using Pfm.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi("v1");

var app = builder.Build();

// `dotnet Pfm.Api.dll migrate` applies migrations and exits. Deployments run it as a
// separate step before the new API version starts; the API never migrates on start-up.
if (args is ["migrate"])
{
    await app.Services.MigrateDatabaseAsync();
    return;
}

// Every error leaves as RFC 9457 Problem Details: unhandled exceptions and bare status codes alike.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi("/openapi/{documentName}.json");

// Live: the process is up. Ready: its dependencies (the database) are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(InfrastructureServices.ReadyTag),
});

app.Run();

public partial class Program;
