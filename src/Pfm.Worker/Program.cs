using Pfm.Application;
using Pfm.Infrastructure;
using Pfm.Infrastructure.Observability;
using Pfm.Worker.ExchangeRates;
using Pfm.Worker.Recurring;

var builder = Host.CreateApplicationBuilder(args);

builder.AddPfmObservability("pfm-worker");

// The worker shares the API's use cases and database, so a job follows the same rules as a request.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddUsers();

// Background jobs are registered here as the slices that need them land.
builder.Services.AddHostedService<ExchangeRateFetchJob>();
builder.Services.AddOptions<RecurringPostingOptions>()
    .BindConfiguration(RecurringPostingOptions.Section)
    .Validate(options => options.PostEvery > TimeSpan.Zero, "RecurringRules:PostEvery must be longer than zero.")
    .ValidateOnStart();
builder.Services.AddHostedService<RecurringPostingJob>();

builder.Build().Run();
