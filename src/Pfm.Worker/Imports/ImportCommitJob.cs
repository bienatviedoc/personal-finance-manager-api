using Microsoft.Extensions.Options;
using Pfm.Application.Imports;

namespace Pfm.Worker.Imports;

/// <summary>
/// Imports the rows of committed statements as transactions, in the background with progress (spec section 5):
/// every few seconds it imports the next rows of each batch being committed, a save at a time, until none is left.
/// What fails is logged, which reports it to the error tracker: a batch that fails holds up no other, and is stopped
/// after a few failures in a row (<see cref="ImportCommits.MaxAttempts"/>). Nothing is lost, as every save records
/// its rows with the batch's count, and the next run carries on from that count.
/// </summary>
public sealed partial class ImportCommitJob(
    IServiceScopeFactory scopes, IOptions<ImportCommitOptions> options, TimeProvider time, ILogger<ImportCommitJob> logger)
    : PeriodicJob(scopes, time)
{
    protected override TimeSpan RunEvery => options.Value.CommitEvery;

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // A scope for each save, so what one reads is not held in memory for the next.
        var saves = services.GetRequiredService<IServiceScopeFactory>();
        for (var imported = 1; imported > 0;)
        {
            await using var scope = saves.CreateAsyncScope();
            var run = await scope.ServiceProvider.GetRequiredService<ImportCommits>().ImportNextAsync(cancellationToken: cancellationToken);
            imported = run.Rows;
            if (imported > 0)
            {
                LogImported(imported);
            }

            foreach (var failure in run.Failures)
            {
                LogFailed(failure);
            }
        }
    }

    protected override void OnFailed(Exception exception) => LogFailed(exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported {Rows} row(s) of committed statements")]
    private partial void LogImported(int rows);

    [LoggerMessage(Level = LogLevel.Error, Message = "Importing committed statements failed; the next run carries on where the last save left off, and a batch failing again and again is stopped")]
    private partial void LogFailed(Exception exception);
}
