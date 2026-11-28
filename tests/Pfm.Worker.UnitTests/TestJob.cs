using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace Pfm.Worker.UnitTests;

/// <summary>
/// A periodic job whose run each test sets; it records each run's scope, and each failure it is told of before
/// handing it to <paramref name="onFailed"/>.
/// </summary>
internal sealed class TestJob(TimeSpan runEvery, Func<int, CancellationToken, Task>? run = null, Action<Exception>? onFailed = null)
    : PeriodicJob(NewServices().GetRequiredService<IServiceScopeFactory>(), TimeProvider.System)
{
    private int _count;

    /// <summary>The scoped <see cref="Probe"/> of each run, as the run starts.</summary>
    public Channel<Probe> Runs { get; } = Channel.CreateUnbounded<Probe>();

    public Channel<Exception> Failures { get; } = Channel.CreateUnbounded<Exception>();

    protected override TimeSpan RunEvery => runEvery;

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        Runs.Writer.TryWrite(services.GetRequiredService<Probe>());
        await (run?.Invoke(++_count, cancellationToken) ?? Task.CompletedTask);
    }

    protected override void OnFailed(Exception exception)
    {
        Failures.Writer.TryWrite(exception);
        onFailed?.Invoke(exception);
    }

    private static ServiceProvider NewServices() => new ServiceCollection().AddScoped<Probe>().BuildServiceProvider();

    /// <summary>A scoped service, to tell one run's scope from another's and see that it is disposed.</summary>
    public sealed class Probe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
