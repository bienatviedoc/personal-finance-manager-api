using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>What one run of <see cref="ImportCommits.ImportNextAsync"/> came to.</summary>
/// <param name="Rows">How many rows it imported, of every batch.</param>
/// <param name="Failures">What went wrong unexpectedly, one for each batch (or user) it could not get on with.</param>
public sealed record ImportRun(int Rows, IReadOnlyList<Exception> Failures);

/// <summary>
/// Imports the rows of committed statements as transactions, in the background (spec section 5: an import runs as a
/// background job with progress). The worker runs it every few seconds; no user can. Each save records a few rows'
/// transactions together with the batch's count moving on, guarded by the batch's row version, so the user sees
/// the count grow, a row is never imported twice, and a worker that stops in between leaves nothing half done.
/// Once a batch's last row is imported its budget notifications are raised (spec BUD-7). Nothing is imported for a
/// deactivated user. A batch that fails unexpectedly holds up no other: the run carries on with the rest, and once
/// one has failed <see cref="MaxAttempts"/> times in a row it is stopped as <see cref="ImportFailure.Unexpected"/>.
/// </summary>
public sealed class ImportCommits(
    IImportBatchRepository batches, IImportedTransactions transactions, IAccountLookup accounts, IUserDeactivation deactivation,
    IBudgetNotifications budgetNotifications, IActForUser actFor, IUnitOfWork unitOfWork, TimeProvider clock)
{
    /// <summary>How many rows one save imports, unless asked otherwise.</summary>
    public const int RowsPerSave = 250;

    /// <summary>How many runs in a row may fail unexpectedly on one batch before it is stopped.</summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// Imports the next rows of every batch being committed, at most <paramref name="rowsPerSave"/> of each. Answers
    /// how many rows it imported, and what went wrong unexpectedly, batch by batch, for the worker to log.
    /// </summary>
    public async Task<ImportRun> ImportNextAsync(int rowsPerSave = RowsPerSave, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowsPerSave);
        var (imported, failures) = (0, new List<Exception>());
        foreach (var userId in await batches.UsersWithCommittingAsync(cancellationToken))
        {
            try
            {
                imported += await ImportNextForAsync(userId, rowsPerSave, failures, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failures.Add(new InvalidOperationException($"Importing the statements of user {userId} failed; the next run tries again.", exception));
            }

            // The next user's rows are read on their own.
            unitOfWork.DiscardChanges();
        }

        return new ImportRun(imported, failures);
    }

    private async Task<int> ImportNextForAsync(Guid userId, int rowsPerSave, List<Exception> failures, CancellationToken cancellationToken)
    {
        if (await deactivation.IsDeactivatedAsync(userId, cancellationToken))
        {
            return 0;
        }

        using var _ = actFor.ActFor(userId);
        var imported = 0;
        foreach (var batchId in await batches.ListCommittingAsync(cancellationToken))
        {
            try
            {
                imported += await ImportNextOfAsync(batchId, rowsPerSave, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failures.Add(new InvalidOperationException($"Importing the next rows of import batch {batchId} failed.", exception));
                await CountFailedAttemptAsync(batchId, failures, cancellationToken);
            }
        }

        return imported;
    }

    /// <summary>
    /// Counts a run that failed unexpectedly on the batch, which is stopped once <see cref="MaxAttempts"/> have in a
    /// row, keeping what it imported, whose budget notifications are then raised.
    /// </summary>
    private async Task CountFailedAttemptAsync(Guid batchId, List<Exception> failures, CancellationToken cancellationToken)
    {
        try
        {
            unitOfWork.DiscardChanges();
            if (await batches.FindAsync(batchId, cancellationToken) is not { Status: ImportBatchStatus.Committing } batch)
            {
                return;
            }

            var importedDates = batch.FailedAttempts + 1 >= MaxAttempts ? await ImportedDatesAsync(batch, cancellationToken) : [];
            batch.AttemptFailed(MaxAttempts, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (batch.Status == ImportBatchStatus.Failed)
            {
                await budgetNotifications.CheckAsync(importedDates, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            unitOfWork.DiscardChanges();
            failures.Add(new InvalidOperationException($"Counting a failed attempt at import batch {batchId} failed too; the next run tries again.", exception));
        }
    }

    /// <summary>The dates of the rows the batch imported so far, as far as its file still says them.</summary>
    private async Task<IReadOnlyCollection<DateOnly>> ImportedDatesAsync(ImportBatch batch, CancellationToken cancellationToken)
    {
        try
        {
            var account = await accounts.FindAsync(batch.AccountId, cancellationToken);
            return account is null ? [] : [.. batch.ChosenEntries(account.Currency).Take(batch.ImportedCount).Select(entry => entry.Date).Distinct()];
        }
        catch (ImportRuleViolation)
        {
            return [];
        }
    }

    private async Task<int> ImportNextOfAsync(Guid batchId, int rowsPerSave, CancellationToken cancellationToken)
    {
        // Read again each time: another worker may have moved it on.
        if (await batches.FindAsync(batchId, cancellationToken) is not { Status: ImportBatchStatus.Committing } batch)
        {
            return 0;
        }

        var account = await accounts.FindAsync(batch.AccountId, cancellationToken)
            ?? throw new InvalidOperationException("An import batch's account is archived, never deleted.");
        IReadOnlyList<StatementEntry> chosen;
        try
        {
            chosen = batch.ChosenEntries(account.Currency);
        }
        catch (ImportRuleViolation)
        {
            return await FailAsync(batch, ImportFailure.AccountCurrencyChanged, [], cancellationToken);
        }

        var next = chosen.Skip(batch.ImportedCount).Take(rowsPerSave).ToList();
        var recorded = await transactions.RecordAsync(
            batch.AccountId, batch.Id, [.. next.Select(entry => new TransactionToImport(entry.Date, entry.Type == StatementEntryType.Income, entry.Amount, entry.Description))],
            batch.ExpenseCategoryId, batch.IncomeCategoryId, batch.CommittedOn!.Value, cancellationToken);
        if (recorded != RecordImportOutcome.Recorded)
        {
            var failure = recorded switch
            {
                RecordImportOutcome.AccountArchived => ImportFailure.AccountArchived,
                RecordImportOutcome.CategoryArchived => ImportFailure.CategoryArchived,
                _ => ImportFailure.TransactionRefused,
            };
            return await FailAsync(batch, failure, [.. chosen.Take(batch.ImportedCount).Select(entry => entry.Date)], cancellationToken);
        }

        batch.Imported(next.Count, clock.GetUtcNow());
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrentChangeException)
        {
            // Another worker imported these rows first, or something they need changed meanwhile: the next run reads it all again.
            unitOfWork.DiscardChanges();
            return 0;
        }

        if (batch.Status == ImportBatchStatus.Committed)
        {
            await budgetNotifications.CheckAsync([.. chosen.Select(entry => entry.Date).Distinct()], cancellationToken);
        }

        return next.Count;
    }

    /// <summary>Stops the batch with the reason, keeping what it imported, whose budget notifications are then raised.</summary>
    private async Task<int> FailAsync(ImportBatch batch, ImportFailure reason, IReadOnlyCollection<DateOnly> importedDates, CancellationToken cancellationToken)
    {
        var batchId = batch.Id;
        unitOfWork.DiscardChanges();
        if (await batches.FindAsync(batchId, cancellationToken) is not { Status: ImportBatchStatus.Committing } stopping)
        {
            return 0;
        }

        stopping.Fail(reason, clock.GetUtcNow());
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrentChangeException)
        {
            unitOfWork.DiscardChanges();
            return 0;
        }

        await budgetNotifications.CheckAsync([.. importedDates.Distinct()], cancellationToken);
        return 0;
    }
}
