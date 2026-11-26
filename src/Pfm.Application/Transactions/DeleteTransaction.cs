using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions;

/// <summary>
/// Deletes a transaction, softly: it leaves lists and balances at once and stays recoverable (spec section 3,
/// rule 10). Deleting a leg of a transfer deletes the transfer: the other leg goes with it, in the same save
/// (rule 3). A reconciled transaction, or a leg of a transfer either of whose legs is, is locked and not deleted (rule 11).
/// </summary>
public sealed record DeleteTransactionCommand(Guid Id);

public sealed class DeleteTransactionValidator : AbstractValidator<DeleteTransactionCommand>
{
    public DeleteTransactionValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers null when the user has no transaction with that id, or already deleted it.</summary>
public sealed class DeleteTransactionHandler(
    IValidator<DeleteTransactionCommand> validator, ITransactionRepository transactions, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<DeleteTransactionCommand, DeleteOutcome?>(validator)
{
    /// <summary>A delete names no row version, so one that meets a change made at the same moment is made again on what was saved.</summary>
    protected override Task<Result<DeleteOutcome?>> ExecuteAsync(DeleteTransactionCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<DeleteOutcome?>>(async () =>
        {
            // Edited by another request in between, it is deleted as edited; deleted by one, it is no longer found.
            var transaction = await transactions.FindAsync(command.Id, cancellationToken);
            if (transaction is null)
            {
                return (DeleteOutcome?)null;
            }

            if (transaction.TransferId is { } transferId)
            {
                // The other leg deleted by another request in between fails the save, and the delete is made again.
                var transfer = (await transactions.FindTransferAsync(transferId, deletedToo: true, cancellationToken))!;
                if (transfer.IsReconciled)
                {
                    return DeleteOutcome.Reconciled;
                }

                transfer.Delete(clock.GetUtcNow());
            }
            else
            {
                if (transaction.IsReconciled)
                {
                    return DeleteOutcome.Reconciled;
                }

                transaction.Delete(clock.GetUtcNow());
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return DeleteOutcome.Deleted;
        });
}
