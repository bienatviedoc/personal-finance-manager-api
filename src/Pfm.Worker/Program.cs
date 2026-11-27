using Pfm.Application;
using Pfm.Infrastructure;
using Pfm.Infrastructure.Observability;
using Pfm.Worker.ExchangeRates;
using Pfm.Worker.Identity;
using Pfm.Worker.Imports;
using Pfm.Worker.Recurring;
using Pfm.Worker.Transactions;

var builder = Host.CreateApplicationBuilder(args);

builder.AddPfmObservability("pfm-worker")
    .WithMetrics(metrics => metrics.AddMeter(IntegrityCheckJob.MeterName));

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
builder.Services.AddHostedService<UserErasureJob>();
builder.Services.AddOptions<IntegrityCheckOptions>()
    .BindConfiguration(IntegrityCheckOptions.Section)
    .Validate(options => options.RunEvery > TimeSpan.Zero, "IntegrityCheck:RunEvery must be longer than zero.")
    .ValidateOnStart();
builder.Services.AddHostedService<IntegrityCheckJob>();
builder.Services.AddOptions<DeletedTransactionPurgeOptions>()
    .BindConfiguration(DeletedTransactionPurgeOptions.Section)
    .Validate(options => options.RunEvery > TimeSpan.Zero, "DeletedTransactionPurge:RunEvery must be longer than zero.")
    .ValidateOnStart();
builder.Services.AddHostedService<DeletedTransactionPurgeJob>();
builder.Services.AddOptions<ImportCommitOptions>()
    .BindConfiguration(ImportCommitOptions.Section)
    .Validate(options => options.CommitEvery > TimeSpan.Zero, "Imports:CommitEvery must be longer than zero.")
    .ValidateOnStart();
builder.Services.AddHostedService<ImportCommitJob>();
builder.Services.AddOptions<AbandonedUploadSweepOptions>()
    .BindConfiguration(AbandonedUploadSweepOptions.Section)
    .Validate(options => options.RunEvery > TimeSpan.Zero, "AbandonedUploadSweep:RunEvery must be longer than zero.")
    .Validate(options => options.KeepFor > TimeSpan.Zero, "AbandonedUploadSweep:KeepFor must be longer than zero.")
    .ValidateOnStart();
builder.Services.AddHostedService<AbandonedUploadSweepJob>();

builder.Build().Run();
