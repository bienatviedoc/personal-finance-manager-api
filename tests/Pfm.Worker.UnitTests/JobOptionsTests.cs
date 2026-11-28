using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pfm.Worker.ExchangeRates;
using Pfm.Worker.Identity;
using Pfm.Worker.Imports;
using Pfm.Worker.Transactions;

namespace Pfm.Worker.UnitTests;

public class JobOptionsTests
{
    public static TheoryData<string, Func<IServiceProvider, object>> Settings => new()
    {
        { "ExchangeRates:FetchEvery", services => Options<ExchangeRateFetchOptions>(services).FetchEvery },
        { "ExchangeRates:DaysBackWhenEmpty", services => Options<ExchangeRateFetchOptions>(services).DaysBackWhenEmpty },
        { "RecurringRules:PostEvery", services => Options<RecurringPostingOptions>(services).PostEvery },
        { "UserErasure:RunEvery", services => Options<UserErasureOptions>(services).RunEvery },
        { "IntegrityCheck:RunEvery", services => Options<IntegrityCheckOptions>(services).RunEvery },
        { "DeletedTransactionPurge:RunEvery", services => Options<DeletedTransactionPurgeOptions>(services).RunEvery },
        { "Imports:CommitEvery", services => Options<ImportCommitOptions>(services).CommitEvery },
        { "AbandonedUploadSweep:RunEvery", services => Options<AbandonedUploadSweepOptions>(services).RunEvery },
        { "AbandonedUploadSweep:KeepFor", services => Options<AbandonedUploadSweepOptions>(services).KeepFor },
    };

    public static TheoryData<string, string> Refused => new()
    {
        { "ExchangeRates:FetchEvery", "00:00:00" },
        { "ExchangeRates:DaysBackWhenEmpty", "-1" },
        { "ExchangeRates:DaysBackWhenEmpty", "3661" },
        { "RecurringRules:PostEvery", "00:00:00" },
        { "UserErasure:RunEvery", "00:00:00" },
        { "IntegrityCheck:RunEvery", "00:00:00" },
        { "DeletedTransactionPurge:RunEvery", "-00:01:00" },
        { "Imports:CommitEvery", "00:00:00" },
        { "AbandonedUploadSweep:RunEvery", "00:00:00" },
        { "AbandonedUploadSweep:KeepFor", "00:00:00" },
    };

    [Theory]
    [MemberData(nameof(Settings))]
    public void Each_job_reads_its_setting_from_its_own_key(string key, Func<IServiceProvider, object> read)
    {
        var value = key.EndsWith("DaysBackWhenEmpty", StringComparison.Ordinal) ? "30" : "00:42:00";
        using var services = WorkerWith(key, value);

        Assert.Equal(value, read(services) is TimeSpan interval ? interval.ToString() : read(services).ToString());
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task The_worker_refuses_to_start_with_a_setting_no_job_can_run_on(string key, string value)
    {
        await using var services = WorkerWith(key, value);

        var refusal = await Assert.ThrowsAsync<OptionsValidationException>(() => ValidateOnStartAsync(services));
        Assert.Contains(key, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_worker_starts_with_no_settings_at_all()
    {
        await using var services = WorkerWith();

        await ValidateOnStartAsync(services);
    }

    private static Task ValidateOnStartAsync(IServiceProvider services) =>
        services.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(CancellationToken.None);

    private static T Options<T>(IServiceProvider services) where T : class => services.GetRequiredService<IOptions<T>>().Value;

    private static ServiceProvider WorkerWith(params string[] keyAndValue)
    {
        var settings = new Dictionary<string, string?>();
        for (var i = 0; i < keyAndValue.Length; i += 2)
        {
            settings[keyAndValue[i]] = keyAndValue[i + 1];
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddJobs();
        return services.BuildServiceProvider();
    }
}
