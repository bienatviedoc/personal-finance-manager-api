using Microsoft.Extensions.Options;
using Pfm.Application.ExchangeRates;
using Pfm.Worker.ExchangeRates;
using Pfm.Worker.Identity;
using Pfm.Worker.Imports;
using Pfm.Worker.Transactions;

namespace Pfm.Worker;

public static class WorkerJobs
{
    /// <summary>
    /// Every background job, each with its options: bound from the job's own configuration section and checked as
    /// the worker starts, so a setting no job can run on stops the worker rather than a job.
    /// </summary>
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        services.AddJob<ExchangeRateFetchJob, ExchangeRateFetchOptions>(ExchangeRateFetchOptions.Section)
            .Validate(options => options.FetchEvery > TimeSpan.Zero, "ExchangeRates:FetchEvery must be longer than zero.")
            .Validate(
                options => options.DaysBackWhenEmpty is >= 0 and <= FetchExchangeRatesCommand.MaxDaysBackWhenEmpty,
                $"ExchangeRates:DaysBackWhenEmpty must be from 0 to {FetchExchangeRatesCommand.MaxDaysBackWhenEmpty}.");
        services.AddJob<RecurringPostingJob, RecurringPostingOptions>(RecurringPostingOptions.Section)
            .Validate(options => options.PostEvery > TimeSpan.Zero, "RecurringRules:PostEvery must be longer than zero.");
        services.AddJob<UserErasureJob, UserErasureOptions>(UserErasureOptions.Section)
            .Validate(options => options.RunEvery > TimeSpan.Zero, "UserErasure:RunEvery must be longer than zero.");
        services.AddJob<IntegrityCheckJob, IntegrityCheckOptions>(IntegrityCheckOptions.Section)
            .Validate(options => options.RunEvery > TimeSpan.Zero, "IntegrityCheck:RunEvery must be longer than zero.");
        services.AddJob<DeletedTransactionPurgeJob, DeletedTransactionPurgeOptions>(DeletedTransactionPurgeOptions.Section)
            .Validate(options => options.RunEvery > TimeSpan.Zero, "DeletedTransactionPurge:RunEvery must be longer than zero.");
        services.AddJob<ImportCommitJob, ImportCommitOptions>(ImportCommitOptions.Section)
            .Validate(options => options.CommitEvery > TimeSpan.Zero, "Imports:CommitEvery must be longer than zero.");
        services.AddJob<AbandonedUploadSweepJob, AbandonedUploadSweepOptions>(AbandonedUploadSweepOptions.Section)
            .Validate(options => options.RunEvery > TimeSpan.Zero, "AbandonedUploadSweep:RunEvery must be longer than zero.")
            .Validate(options => options.KeepFor > TimeSpan.Zero, "AbandonedUploadSweep:KeepFor must be longer than zero.");

        return services;
    }

    private static OptionsBuilder<TOptions> AddJob<TJob, TOptions>(this IServiceCollection services, string section)
        where TJob : PeriodicJob
        where TOptions : class
    {
        services.AddHostedService<TJob>();
        return services.AddOptions<TOptions>().BindConfiguration(section).ValidateOnStart();
    }
}
