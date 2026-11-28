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
            .Validate(options => options.FetchEvery > TimeSpan.Zero, LongerThanZero(ExchangeRateFetchOptions.Section, nameof(ExchangeRateFetchOptions.FetchEvery)))
            .Validate(
                options => options.DaysBackWhenEmpty is >= 0 and <= FetchExchangeRatesCommand.MaxDaysBackWhenEmpty,
                $"{ExchangeRateFetchOptions.Section}:{nameof(ExchangeRateFetchOptions.DaysBackWhenEmpty)} must be from 0 to {FetchExchangeRatesCommand.MaxDaysBackWhenEmpty}.");
        services.AddJob<RecurringPostingJob, RecurringPostingOptions>(RecurringPostingOptions.Section)
            .Validate(options => options.PostEvery > TimeSpan.Zero, LongerThanZero(RecurringPostingOptions.Section, nameof(RecurringPostingOptions.PostEvery)));
        services.AddJob<UserErasureJob, UserErasureOptions>(UserErasureOptions.Section)
            .Validate(options => options.RunEvery > TimeSpan.Zero, LongerThanZero(UserErasureOptions.Section, nameof(UserErasureOptions.RunEvery)));
        services.AddJob<IntegrityCheckJob, IntegrityCheckOptions>(IntegrityCheckOptions.Section)
            .Validate(options => options.RunEvery > TimeSpan.Zero, LongerThanZero(IntegrityCheckOptions.Section, nameof(IntegrityCheckOptions.RunEvery)));
        services.AddJob<DeletedTransactionPurgeJob, DeletedTransactionPurgeOptions>(DeletedTransactionPurgeOptions.Section)
            .Validate(options => options.RunEvery > TimeSpan.Zero, LongerThanZero(DeletedTransactionPurgeOptions.Section, nameof(DeletedTransactionPurgeOptions.RunEvery)));
        services.AddJob<ImportCommitJob, ImportCommitOptions>(ImportCommitOptions.Section)
            .Validate(options => options.CommitEvery > TimeSpan.Zero, LongerThanZero(ImportCommitOptions.Section, nameof(ImportCommitOptions.CommitEvery)));
        services.AddJob<AbandonedUploadSweepJob, AbandonedUploadSweepOptions>(AbandonedUploadSweepOptions.Section)
            .Validate(options => options.RunEvery > TimeSpan.Zero, LongerThanZero(AbandonedUploadSweepOptions.Section, nameof(AbandonedUploadSweepOptions.RunEvery)))
            .Validate(options => options.KeepFor > TimeSpan.Zero, LongerThanZero(AbandonedUploadSweepOptions.Section, nameof(AbandonedUploadSweepOptions.KeepFor)));

        return services;
    }

    /// <summary>What a refused interval says: every job's settings are named by their section and key, in one wording.</summary>
    private static string LongerThanZero(string section, string setting) => $"{section}:{setting} must be longer than zero.";

    private static OptionsBuilder<TOptions> AddJob<TJob, TOptions>(this IServiceCollection services, string section)
        where TJob : PeriodicJob
        where TOptions : class
    {
        services.AddHostedService<TJob>();
        return services.AddOptions<TOptions>().BindConfiguration(section).ValidateOnStart();
    }
}
