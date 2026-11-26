using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions;

/// <summary>
/// Restores a transaction deleted within the last 30 days (spec section 3, rule 10): it is back in lists and
/// balances as it was. Restoring a leg of a transfer restores the transfer: the other leg comes back with it, in
/// the same save (rule 3).
/// </summary>
public sealed record RestoreTransactionCommand(Guid Id);

public sealed class RestoreTransactionValidator : AbstractValidator<RestoreTransactionCommand>
{
    public RestoreTransactionValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>
/// Answers the transaction restored, or null when the user has no transaction with that id deleted within the last
/// 30 days: one not deleted, deleted longer ago, or restored already.
/// </summary>
public sealed class RestoreTransactionHandler(
    IValidator<RestoreTransactionCommand> validator, ITransactionRepository transactions, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<RestoreTransactionCommand, TransactionDetails?>(validator)
{
    /// <summary>A restore names no row version, so one that meets a change made at the same moment is made again on what was saved.</summary>
    protected override Task<Result<TransactionDetails?>> ExecuteAsync(RestoreTransactionCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<TransactionDetails?>>(async () =>
        {
            var now = clock.GetUtcNow();
            var transaction = await transactions.FindDeletedAsync(command.Id, cancellationToken);
            if (transaction is null || !transaction.IsRecoverableAt(now))
            {
                return (TransactionDetails?)null;
            }

            if (transaction.TransferId is { } transferId)
            {
                (await transactions.FindTransferAsync(transferId, deletedToo: true, cancellationToken))!.Restore(now);
            }
            else
            {
                transaction.Restore(now);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return TransactionDetails.Of(transaction);
        });
}
