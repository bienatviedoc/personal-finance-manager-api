using Pfm.Application;
using Pfm.Infrastructure;
using Pfm.Infrastructure.Observability;
using Pfm.Worker;
using Pfm.Worker.Transactions;

var builder = Host.CreateApplicationBuilder(args);

builder.AddPfmObservability("pfm-worker")
    .WithMetrics(metrics => metrics.AddMeter(IntegrityCheckJob.MeterName));

// The worker shares the API's use cases and database, so a job follows the same rules as a request.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddUsers();

builder.Services.AddJobs();

builder.Build().Run();
