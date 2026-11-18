using Pfm.Application;
using Pfm.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

// The worker shares the API's use cases and database, so a job follows the same rules as a request.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Background jobs are registered here as the slices that need them land.

builder.Build().Run();
