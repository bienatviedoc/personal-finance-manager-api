using Microsoft.Extensions.Options;
using Pfm.Application.Identity;
using Pfm.Infrastructure.Identity;

namespace Pfm.Worker.Identity;

/// <summary>
/// Erases the deactivated users whose 30 days to cancel are over, when the worker starts and every hour after
/// that (spec section 8, Privacy). A run that fails is logged, which reports it to the error tracker; whoever
/// it did not get to is erased by the next one.
/// </summary>
public sealed partial class UserErasureJob(
    IServiceScopeFactory scopes, IOptions<UserErasureOptions> options, TimeProvider time, ILogger<UserErasureJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.RunEvery, time);
        do
        {
            await EraseAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EraseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<EraseDeactivatedUsersHandler>()
                .HandleAsync(new EraseDeactivatedUsersCommand(), cancellationToken);

            if (result.Outcome.Users > 0)
            {
                LogErased(result.Outcome.Users);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Erased {Users} deactivated user(s) whose 30 days were over")]
    private partial void LogErased(int users);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erasing deactivated users failed; the next run will try again")]
    private partial void LogFailed(Exception exception);
}
