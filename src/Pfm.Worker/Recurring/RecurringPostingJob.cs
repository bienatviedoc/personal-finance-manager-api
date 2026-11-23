using Microsoft.Extensions.Options;
using Pfm.Application.Transactions.Recurring;

namespace Pfm.Worker.Recurring;

/// <summary>
/// Posts the occurrences of recurring rules that are due, when the worker starts and every few minutes after
/// that (spec REC-2). A run that fails is logged, which reports it to the error tracker; nothing is lost, as
/// the next run posts every occurrence still due, each with its original due date.
/// </summary>
public sealed partial class RecurringPostingJob(
    IServiceScopeFactory scopes, IOptions<RecurringPostingOptions> options, TimeProvider time, ILogger<RecurringPostingJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PostEvery, time);
        do
        {
            await PostAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PostAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var posted = await scope.ServiceProvider.GetRequiredService<RecurringPosting>().PostDueAsync(cancellationToken);

            if (posted.Occurrences > 0)
            {
                LogPosted(posted.Occurrences);
            }

            if (posted.RulesNotPosted > 0)
            {
                LogRulesNotPosted(posted.RulesNotPosted);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Posted {Occurrences} occurrence(s) of recurring rules")]
    private partial void LogPosted(int occurrences);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Rules} recurring rule(s) have an occurrence that was refused as a transaction (an archived account or category, or an amount that no longer fits); they wait until their user changes them")]
    private partial void LogRulesNotPosted(int rules);

    [LoggerMessage(Level = LogLevel.Error, Message = "Posting recurring rules failed; the next run posts what is still due")]
    private partial void LogFailed(Exception exception);
}
