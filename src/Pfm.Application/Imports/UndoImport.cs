using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>
/// Undoes an import batch (spec IMP-5): every transaction it created is deleted, edited or not, and stays recoverable
/// for 30 days like any deleted transaction. A batch still being imported is stopped. Refused while any of its
/// transactions is reconciled.
/// </summary>
public sealed record UndoImportCommand(Guid Id);

public sealed class UndoImportValidator : AbstractValidator<UndoImportCommand>
{
    public UndoImportValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>What undoing a batch came to.</summary>
public abstract record UndoImportOutcome
{
    private UndoImportOutcome()
    {
    }

    /// <summary>The batch is undone, now or before, and its transactions deleted.</summary>
    /// <param name="Transactions">How many transactions this undo deleted: none when the batch was undone already.</param>
    public sealed record Undone(ImportBatchDetails Batch, int Transactions) : UndoImportOutcome;

    /// <summary>Nothing changed: these many of its transactions are reconciled, so locked (spec section 3, rule 11). Reopen their reconciliation first.</summary>
    public sealed record Reconciled(int Transactions) : UndoImportOutcome;

    /// <summary>Nothing changed: the statement was uploaded and never committed, so nothing was imported.</summary>
    public sealed record NotCommitted : UndoImportOutcome;
}

/// <summary>
/// Answers null when the user has no such batch. The transactions are deleted and the batch marked undone in one save,
/// guarded by the batch's row version (decision record 0007): a worker importing the batch's next rows at the same
/// moment either saves first, and this undo is made again and deletes those rows too, or meets the undone batch and
/// saves none of them. Undoing a batch again answers it as it is. One call, so whatever must happen on every undo
/// happens here.
/// </summary>
public sealed class UndoImportHandler(
    IValidator<UndoImportCommand> validator, IImportBatchRepository batches, IImportedTransactions transactions, IBudgetNotifications budgetNotifications,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<UndoImportCommand, UndoImportOutcome?>(validator)
{
    protected override Task<Result<UndoImportOutcome?>> ExecuteAsync(UndoImportCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<UndoImportOutcome?>>(() => UndoAsync(command, cancellationToken));

    private async Task<Result<UndoImportOutcome?>> UndoAsync(UndoImportCommand command, CancellationToken cancellationToken)
    {
        if (await batches.FindAsync(command.Id, cancellationToken) is not { } batch)
        {
            return (UndoImportOutcome?)null;
        }

        switch (batch.Status)
        {
            case ImportBatchStatus.Uploaded:
                return new UndoImportOutcome.NotCommitted();
            case ImportBatchStatus.Undone:
                return new UndoImportOutcome.Undone(ImportBatchDetails.Of(batch), 0);
        }

        var deleted = await transactions.DeleteAsync(batch.Id, cancellationToken);
        if (deleted is DeleteImportedOutcome.Reconciled reconciled)
        {
            return new UndoImportOutcome.Reconciled(reconciled.Transactions);
        }

        var (count, dates) = (DeleteImportedOutcome.Deleted)deleted;
        batch.Undo(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // A refund deleted raises spent again, as deleting one transaction does.
        await budgetNotifications.CheckAsync(dates, cancellationToken);
        return new UndoImportOutcome.Undone(ImportBatchDetails.Of(batch), count);
    }
}
