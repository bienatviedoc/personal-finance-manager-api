using Microsoft.Extensions.DependencyInjection;

namespace Pfm.Worker.UnitTests;

public class PeriodicJobTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task The_first_run_starts_with_the_job()
    {
        using var job = new TestJob(TimeSpan.FromHours(1));

        await job.StartAsync(CancellationToken.None);

        await job.Runs.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_failed_run_is_reported_and_the_next_run_still_happens()
    {
        var failure = new InvalidOperationException("The run failed.");
        using var job = new TestJob(TimeSpan.FromMilliseconds(10), (count, _) => count == 1 ? throw failure : Task.CompletedTask);

        await job.StartAsync(CancellationToken.None);

        Assert.Same(failure, await job.Failures.Reader.ReadAsync().AsTask().WaitAsync(Patience));
        await job.Runs.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        await job.Runs.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_failure_that_cannot_be_reported_does_not_stop_the_job()
    {
        using var job = new TestJob(
            TimeSpan.FromMilliseconds(10),
            (count, _) => count == 1 ? throw new InvalidOperationException("The run failed.") : Task.CompletedTask,
            _ => throw new InvalidOperationException("Reporting the failure failed."));

        await job.StartAsync(CancellationToken.None);

        await job.Failures.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        await job.Runs.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        await job.Runs.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        Assert.False(job.ExecuteTask!.IsFaulted);
        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Each_run_has_a_scope_of_its_own_disposed_when_it_ends()
    {
        using var job = new TestJob(TimeSpan.FromMilliseconds(10));

        await job.StartAsync(CancellationToken.None);
        var first = await job.Runs.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        var second = await job.Runs.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        await job.StopAsync(CancellationToken.None);

        Assert.NotSame(first, second);
        Assert.True(first.Disposed);
    }

    [Fact]
    public async Task Stopping_cancels_the_run_in_progress_without_calling_it_a_failure()
    {
        using var job = new TestJob(TimeSpan.FromHours(1), (_, cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken));
        await job.StartAsync(CancellationToken.None);
        await job.Runs.Reader.ReadAsync().AsTask().WaitAsync(Patience);

        await job.StopAsync(CancellationToken.None).WaitAsync(Patience);

        // Ended, not faulted: the host treats a cancelled job as stopped, not failed.
        Assert.True(job.ExecuteTask!.IsCompleted);
        Assert.False(job.ExecuteTask.IsFaulted);
        Assert.False(job.Failures.Reader.TryRead(out _));
    }
}
