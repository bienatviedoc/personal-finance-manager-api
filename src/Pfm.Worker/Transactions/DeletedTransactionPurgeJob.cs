using Microsoft.Extensions.Options;
using Pfm.Application.Transactions;

namespace Pfm.Worker.Transactions;

/// <summary>
/// Removes for good the transactions deleted more than 30 days ago (spec section 3, rule 10), when the worker
/// starts and every hour after that. A run that fails is logged, which reports it to the error tracker; whatever
/// it did not get to is removed by the next one.
/// </summary>
public sealed partial class DeletedTransactionPurgeJob(
    IServiceScopeFactory scopes, IOptions<DeletedTransactionPurgeOptions> options, TimeProvider time, ILogger<DeletedTransactionPurgeJob> logger)
    : PeriodicJob(scopes, time)
{
    protected override TimeSpan RunEvery => options.Value.RunEvery;

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var purged = await services.GetRequiredService<DeletedTransactionPurge>().RunAsync(cancellationToken);
        if (purged.Transactions > 0)
        {
            LogPurged(purged.Transactions);
        }
    }

    protected override void OnFailed(Exception exception) => LogFailed(exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Transactions} transaction(s) deleted more than 30 days ago for good")]
    private partial void LogPurged(int transactions);

    [LoggerMessage(Level = LogLevel.Error, Message = "Removing transactions deleted more than 30 days ago failed; the next run will try again")]
    private partial void LogFailed(Exception exception);
}
