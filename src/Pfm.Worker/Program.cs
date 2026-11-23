using Pfm.Application;
using Pfm.Infrastructure;
using Pfm.Infrastructure.Observability;
using Pfm.Worker.ExchangeRates;
using Pfm.Worker.Identity;

var builder = Host.CreateApplicationBuilder(args);

builder.AddPfmObservability("pfm-worker");

// The worker shares the API's use cases and database, so a job follows the same rules as a request.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddUsers();

// Background jobs are registered here as the slices that need them land.
builder.Services.AddHostedService<ExchangeRateFetchJob>();
builder.Services.AddHostedService<UserErasureJob>();

builder.Build().Run();
