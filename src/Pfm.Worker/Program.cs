using Pfm.Application;
using Pfm.Infrastructure;
using Pfm.Infrastructure.Observability;

var builder = Host.CreateApplicationBuilder(args);

builder.AddPfmObservability("pfm-worker");

// The worker shares the API's use cases and database, so a job follows the same rules as a request.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Background jobs are registered here as the slices that need them land.

builder.Build().Run();
