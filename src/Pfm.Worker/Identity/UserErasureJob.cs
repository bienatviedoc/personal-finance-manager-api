using Microsoft.Extensions.Options;
using Pfm.Application.Identity;

namespace Pfm.Worker.Identity;

/// <summary>
/// Erases the deactivated users whose 30 days to cancel are over, when the worker starts and every hour after
/// that (spec section 8, Privacy). A run that fails is logged, which reports it to the error tracker; whoever
/// it did not get to is erased by the next one.
/// </summary>
public sealed partial class UserErasureJob(
    IServiceScopeFactory scopes, IOptions<UserErasureOptions> options, TimeProvider time, ILogger<UserErasureJob> logger)
    : PeriodicJob(scopes, time)
{
    protected override TimeSpan RunEvery => options.Value.RunEvery;

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<EraseDeactivatedUsersHandler>()
            .HandleAsync(new EraseDeactivatedUsersCommand(), cancellationToken);

        if (result.Outcome.Users > 0)
        {
            LogErased(result.Outcome.Users);
        }
    }

    protected override void OnFailed(Exception exception) => LogFailed(exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Erased {Users} deactivated user(s) whose 30 days were over")]
    private partial void LogErased(int users);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erasing deactivated users failed; the next run will try again")]
    private partial void LogFailed(Exception exception);
}
