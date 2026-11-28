using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Pfm.Application.Transactions;
using Pfm.Domain.Transactions;

namespace Pfm.Worker.Transactions;

/// <summary>
/// Runs <see cref="IntegrityCheck"/> when the worker starts and once a day after that (spec sections 4 and 10).
/// Each drift is logged as a warning naming its user and the rows to look at; nothing is corrected. Every run
/// ends with one log line saying what it found, and the metrics below say when the last run was and what it
/// came to. A run that fails is logged, which reports it to the error tracker, and the next one runs as usual.
/// </summary>
public sealed partial class IntegrityCheckJob : PeriodicJob
{
    /// <summary>The meter of the job's metrics; the worker exports it.</summary>
    public const string MeterName = "Pfm.Worker.IntegrityCheck";

    private readonly IOptions<IntegrityCheckOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<IntegrityCheckJob> _logger;

    /// <summary><c>pfm.integrity_check.runs</c>: how many runs there have been, by <c>result</c>.</summary>
    private readonly Counter<long> _runs;

    /// <summary><c>pfm.integrity_check.last_run</c>: when the last run of each <c>result</c> ended, in Unix seconds.</summary>
    private readonly Gauge<long> _lastRun;

    /// <summary><c>pfm.integrity_check.drift</c>: how much drift of each <c>kind</c> the last run that finished found.</summary>
    private readonly Gauge<long> _drift;

    public IntegrityCheckJob(
        IServiceScopeFactory scopes, IOptions<IntegrityCheckOptions> options, TimeProvider time, IMeterFactory meters, ILogger<IntegrityCheckJob> logger)
        : base(scopes, time)
    {
        (_options, _time, _logger) = (options, time, logger);
        var meter = meters.Create(MeterName);
        _runs = meter.CreateCounter<long>("pfm.integrity_check.runs", "{run}", "Runs of the integrity check, by result: consistent, drift or failed.");
        _lastRun = meter.CreateGauge<long>("pfm.integrity_check.last_run", "s", "When the last integrity check of each result ended, in Unix time.");
        _drift = meter.CreateGauge<long>("pfm.integrity_check.drift", "{drift}", "Drift of each kind (lines, transfer, balance) the last integrity check found.");
    }

    protected override TimeSpan RunEvery => _options.Value.RunEvery;

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var report = await services.GetRequiredService<IntegrityCheck>().RunAsync(cancellationToken);
        Report(report);
    }

    protected override void OnFailed(Exception exception)
    {
        LogFailed(exception);
        Ended("failed");
    }

    private void Report(IntegrityReport report)
    {
        // Every kind is recorded, at zero when none was found, so the gauge does not keep a count from an earlier run.
        var found = new Dictionary<string, long> { ["lines"] = 0, ["transfer"] = 0, ["balance"] = 0 };
        foreach (var (userId, drift) in report.Drift)
        {
            switch (drift)
            {
                case Drift.Lines lines:
                    LogLinesDrift(userId, lines.TransactionId, lines.Amount, lines.LinesTotal);
                    found["lines"]++;
                    break;
                case Drift.Transfer transfer:
                    LogTransferDrift(userId, transfer.TransferId, transfer.Problem, transfer.LegIds);
                    found["transfer"]++;
                    break;
                case Drift.Balance balance:
                    LogBalanceDrift(userId, balance.AccountId, balance.Shown, balance.FromTransactions);
                    found["balance"]++;
                    break;
            }
        }

        if (report.Drift.Count == 0)
        {
            LogConsistent(report.Transactions, report.Users);
        }
        else
        {
            LogDriftFound(report.Drift.Count, report.Transactions, report.Users);
        }

        foreach (var (kind, count) in found)
        {
            _drift.Record(count, new KeyValuePair<string, object?>("kind", kind));
        }

        Ended(report.Drift.Count == 0 ? "consistent" : "drift");
    }

    private void Ended(string result)
    {
        var tag = new KeyValuePair<string, object?>("result", result);
        _runs.Add(1, tag);
        _lastRun.Record(_time.GetUtcNow().ToUnixTimeSeconds(), tag);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Integrity check found no drift in {Transactions} transaction(s) of {Users} user(s)")]
    private partial void LogConsistent(int transactions, int users);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Integrity check found {Drift} drift(s) in {Transactions} transaction(s) of {Users} user(s); nothing was corrected")]
    private partial void LogDriftFound(int drift, int transactions, int users);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drift: transaction {TransactionId} of user {UserId} is {Amount}, but its lines sum to {LinesTotal}")]
    private partial void LogLinesDrift(Guid userId, Guid transactionId, decimal amount, decimal linesTotal);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drift: transfer {TransferId} of user {UserId} does not match ({Problem}); its legs are {LegIds}")]
    private partial void LogTransferDrift(Guid userId, Guid transferId, TransferDrift problem, IReadOnlyList<Guid> legIds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drift: the transactions of account {AccountId} of user {UserId} come to {Shown} in its balance, but to {FromTransactions} added up one at a time")]
    private partial void LogBalanceDrift(Guid userId, Guid accountId, decimal shown, decimal fromTransactions);

    [LoggerMessage(Level = LogLevel.Error, Message = "The integrity check failed; the next run checks everything again")]
    private partial void LogFailed(Exception exception);
}
