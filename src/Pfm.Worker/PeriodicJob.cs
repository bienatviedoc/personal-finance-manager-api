namespace Pfm.Worker;

/// <summary>
/// A background job that runs when the worker starts and every <see cref="RunEvery"/> after that, each run in a
/// scope of its own. A run that fails is handed to <see cref="OnFailed"/>, which logs it (and so reports it to the
/// error tracker), and the next run happens as usual, even when reporting the failure fails too. Stopping the worker cancels the run in progress, which ends
/// the job without calling it a failure.
/// </summary>
public abstract class PeriodicJob(IServiceScopeFactory scopes, TimeProvider time) : BackgroundService
{
    /// <summary>The longest interval a job can run on: the most a <see cref="PeriodicTimer"/> waits, about 49.7 days.</summary>
    public static readonly TimeSpan MaxRunEvery = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// How long after one run starts the next one does, read once when the job starts: longer than zero and no longer
    /// than <see cref="MaxRunEvery"/>.
    /// </summary>
    protected abstract TimeSpan RunEvery { get; }

    /// <summary>One run, given the services of its own scope.</summary>
    protected abstract Task RunAsync(IServiceProvider services, CancellationToken cancellationToken);

    /// <summary>Logs a run that failed. The next run still happens.</summary>
    protected abstract void OnFailed(Exception exception);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RunEvery, time);
        do
        {
            await RunInScopeAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunInScopeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await RunAsync(scope.ServiceProvider, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Report(exception);
        }
    }

    private void Report(Exception exception)
    {
        try
        {
            OnFailed(exception);
        }
#pragma warning disable CA1031 // A failure to report a failed run must not stop the job, and nothing is left to report it with.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
