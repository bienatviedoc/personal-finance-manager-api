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
        services.AddJob<ExchangeRateFetchJob, ExchangeRateFetchOptions>(
                ExchangeRateFetchOptions.Section, nameof(ExchangeRateFetchOptions.FetchEvery), options => options.FetchEvery)
            .Validate(
                options => FetchExchangeRatesCommand.IsValidDaysBackWhenEmpty(options.DaysBackWhenEmpty),
                $"{ExchangeRateFetchOptions.Section}:{nameof(ExchangeRateFetchOptions.DaysBackWhenEmpty)} must be from 0 to {FetchExchangeRatesCommand.MaxDaysBackWhenEmpty}.");
        services.AddJob<RecurringPostingJob, RecurringPostingOptions>(
            RecurringPostingOptions.Section, nameof(RecurringPostingOptions.PostEvery), options => options.PostEvery);
        services.AddJob<UserErasureJob, UserErasureOptions>(
            UserErasureOptions.Section, nameof(UserErasureOptions.RunEvery), options => options.RunEvery);
        services.AddJob<IntegrityCheckJob, IntegrityCheckOptions>(
            IntegrityCheckOptions.Section, nameof(IntegrityCheckOptions.RunEvery), options => options.RunEvery);
        services.AddJob<DeletedTransactionPurgeJob, DeletedTransactionPurgeOptions>(
            DeletedTransactionPurgeOptions.Section, nameof(DeletedTransactionPurgeOptions.RunEvery), options => options.RunEvery);
        services.AddJob<ImportCommitJob, ImportCommitOptions>(
            ImportCommitOptions.Section, nameof(ImportCommitOptions.CommitEvery), options => options.CommitEvery);
        services.AddJob<AbandonedUploadSweepJob, AbandonedUploadSweepOptions>(
                AbandonedUploadSweepOptions.Section, nameof(AbandonedUploadSweepOptions.RunEvery), options => options.RunEvery)
            .ValidateLongerThanZero(AbandonedUploadSweepOptions.Section, nameof(AbandonedUploadSweepOptions.KeepFor), options => options.KeepFor);

        return services;
    }

    /// <summary>
    /// Registers a job with its options, bound from its section and checked as the worker starts, its interval
    /// (named <paramref name="interval"/>) among them.
    /// </summary>
    private static OptionsBuilder<TOptions> AddJob<TJob, TOptions>(
        this IServiceCollection services, string section, string interval, Func<TOptions, TimeSpan> runEvery)
        where TJob : PeriodicJob
        where TOptions : class
    {
        services.AddHostedService<TJob>();
        return services.AddOptions<TOptions>().BindConfiguration(section).ValidateOnStart()
            .ValidateLongerThanZero(section, interval, runEvery);
    }

    /// <summary>Refuses a length of time of zero or less, named by its section and key in every job's one wording.</summary>
    private static OptionsBuilder<TOptions> ValidateLongerThanZero<TOptions>(
        this OptionsBuilder<TOptions> options, string section, string setting, Func<TOptions, TimeSpan> length)
        where TOptions : class =>
        options.Validate(value => length(value) > TimeSpan.Zero, $"{section}:{setting} must be longer than zero.");
}
