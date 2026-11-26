using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions;

/// <summary>
/// Deletes a transaction, softly: it leaves lists and balances at once and stays recoverable (spec section 3,
/// rule 10). Deleting a leg of a transfer deletes the transfer: the other leg goes with it, in the same save
/// (rule 3).
/// </summary>
public sealed record DeleteTransactionCommand(Guid Id);

public sealed class DeleteTransactionValidator : AbstractValidator<DeleteTransactionCommand>
{
    public DeleteTransactionValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers null when the user has no transaction with that id, or already deleted it.</summary>
public sealed class DeleteTransactionHandler(
    IValidator<DeleteTransactionCommand> validator, ITransactionRepository transactions, IBudgetNotifications budgetNotifications, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<DeleteTransactionCommand, Done?>(validator)
{
    /// <summary>A delete names no row version, so one that meets a change made at the same moment is made again on what was saved.</summary>
    protected override Task<Result<Done?>> ExecuteAsync(DeleteTransactionCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<Done?>>(async () =>
        {
            // Edited by another request in between, it is deleted as edited; deleted by one, it is no longer found.
            var transaction = await transactions.FindAsync(command.Id, cancellationToken);
            if (transaction is null)
            {
                return (Done?)null;
            }

            if (transaction.TransferId is { } transferId)
            {
                // The other leg deleted by another request in between fails the save, and the delete is made again.
                (await transactions.FindTransferAsync(transferId, deletedToo: true, cancellationToken))!.Delete(clock.GetUtcNow());
            }
            else
            {
                transaction.Delete(clock.GetUtcNow());
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (transaction.TransferId is null)
            {
                // A refund deleted raises spent again.
                await budgetNotifications.CheckAsync([transaction.Date], cancellationToken);
            }

            return new Done();
        });
}
