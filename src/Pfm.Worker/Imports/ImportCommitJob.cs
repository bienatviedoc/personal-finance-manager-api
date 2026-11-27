using Microsoft.Extensions.Options;
using Pfm.Application.Imports;

namespace Pfm.Worker.Imports;

/// <summary>
/// Imports the rows of committed statements as transactions, in the background with progress (spec section 5):
/// every few seconds it imports the next rows of each batch being committed, a save at a time, until none is left.
/// A run that fails is logged, which reports it to the error tracker; nothing is lost, as every save records its rows
/// with the batch's count, and the next run carries on from that count.
/// </summary>
public sealed partial class ImportCommitJob(
    IServiceScopeFactory scopes, IOptions<ImportCommitOptions> options, TimeProvider time, ILogger<ImportCommitJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CommitEvery, time);
        do
        {
            await ImportAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        try
        {
            // A scope for each save, so what one reads is not held in memory for the next.
            for (var imported = 1; imported > 0;)
            {
                await using var scope = scopes.CreateAsyncScope();
                imported = await scope.ServiceProvider.GetRequiredService<ImportCommits>().ImportNextAsync(cancellationToken: cancellationToken);
                if (imported > 0)
                {
                    LogImported(imported);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported {Rows} row(s) of committed statements")]
    private partial void LogImported(int rows);

    [LoggerMessage(Level = LogLevel.Error, Message = "Importing committed statements failed; the next run carries on where the last save left off")]
    private partial void LogFailed(Exception exception);
}
