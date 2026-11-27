using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>
/// Imports the rows of committed statements as transactions, in the background (spec section 5: an import runs as a
/// background job with progress). The worker runs it every few seconds; no user can. Each save records a few rows'
/// transactions together with the batch's count moving on, guarded by the batch's row version, so the user sees
/// the count grow, a row is never imported twice, and a worker that stops in between leaves nothing half done.
/// Once a batch's last row is imported its budget notifications are raised (spec BUD-7). Nothing is imported for a
/// deactivated user.
/// </summary>
public sealed class ImportCommits(
    IImportBatchRepository batches, IImportedTransactions transactions, IAccountLookup accounts, IUserDeactivation deactivation,
    IBudgetNotifications budgetNotifications, IActForUser actFor, IUnitOfWork unitOfWork, TimeProvider clock)
{
    /// <summary>How many rows one save imports, unless asked otherwise.</summary>
    public const int RowsPerSave = 250;

    /// <summary>Imports the next rows of every batch being committed, at most <paramref name="rowsPerSave"/> of each. Answers how many rows it imported.</summary>
    public async Task<int> ImportNextAsync(int rowsPerSave = RowsPerSave, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowsPerSave);
        var imported = 0;
        foreach (var userId in await batches.UsersWithCommittingAsync(cancellationToken))
        {
            if (await deactivation.IsDeactivatedAsync(userId, cancellationToken))
            {
                continue;
            }

            using var _ = actFor.ActFor(userId);
            foreach (var batchId in await batches.ListCommittingAsync(cancellationToken))
            {
                imported += await ImportNextOfAsync(batchId, rowsPerSave, cancellationToken);
            }

            // The next user's rows are read on their own.
            unitOfWork.DiscardChanges();
        }

        return imported;
    }

    /// <summary>Imports until no batch has a row left to import. Answers how many rows it imported.</summary>
    public async Task<int> ImportAllAsync(CancellationToken cancellationToken = default)
    {
        var total = 0;
        for (int imported; (imported = await ImportNextAsync(RowsPerSave, cancellationToken)) > 0;)
        {
            total += imported;
        }

        return total;
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
        catch (ImportRuleViolation violation)
        {
            return await FailAsync(batch, violation.Message, [], cancellationToken);
        }

        var next = chosen.Skip(batch.ImportedCount).Take(rowsPerSave).ToList();
        var refusal = await transactions.RecordAsync(
            batch.AccountId, batch.Id, [.. next.Select(entry => new TransactionToImport(entry.Date, entry.Type == StatementEntryType.Income, entry.Amount, entry.Description))],
            batch.ExpenseCategoryId, batch.IncomeCategoryId, batch.CommittedOn!.Value, cancellationToken);
        if (refusal is not null)
        {
            return await FailAsync(batch, refusal, [.. chosen.Take(batch.ImportedCount).Select(entry => entry.Date)], cancellationToken);
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
    private async Task<int> FailAsync(ImportBatch batch, string reason, IReadOnlyCollection<DateOnly> importedDates, CancellationToken cancellationToken)
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
