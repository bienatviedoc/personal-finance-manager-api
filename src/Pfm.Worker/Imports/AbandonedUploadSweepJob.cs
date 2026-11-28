using Microsoft.Extensions.Options;
using Pfm.Application.Imports;

namespace Pfm.Worker.Imports;

/// <summary>
/// Removes statements uploaded and never committed once they are older than
/// <see cref="AbandonedUploadSweepOptions.KeepFor"/> (24 hours), when the worker starts and every hour after that, so
/// no statement's text is kept for good (spec ACC-8). A run that fails is logged, which reports it to the error
/// tracker; whatever it did not get to is removed by the next one.
/// </summary>
public sealed partial class AbandonedUploadSweepJob(
    IServiceScopeFactory scopes, IOptions<AbandonedUploadSweepOptions> options, TimeProvider time, ILogger<AbandonedUploadSweepJob> logger)
    : PeriodicJob(scopes, time)
{
    protected override TimeSpan RunEvery => options.Value.RunEvery;

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var removed = await services.GetRequiredService<AbandonedUploadSweep>().RunAsync(options.Value.KeepFor, cancellationToken);
        if (removed > 0)
        {
            LogRemoved(removed);
        }
    }

    protected override void OnFailed(Exception exception) => LogFailed(exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Batches} statement(s) uploaded and never committed")]
    private partial void LogRemoved(int batches);

    [LoggerMessage(Level = LogLevel.Error, Message = "Removing statements uploaded and never committed failed; the next run will try again")]
    private partial void LogFailed(Exception exception);
}
