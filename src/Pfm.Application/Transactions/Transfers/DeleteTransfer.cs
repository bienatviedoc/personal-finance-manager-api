using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Transfers;

/// <summary>
/// Deletes a transfer, softly and both legs in one save: they leave lists and both accounts' balances at
/// once and stay recoverable (spec section 3, rules 3 and 10). A transfer either of whose legs is reconciled is
/// locked and not deleted. The delete is recorded in the user's audit log in the same save.
/// </summary>
/// <param name="Id">The transfer id its two legs share.</param>
public sealed record DeleteTransferCommand(Guid Id);

public sealed class DeleteTransferValidator : AbstractValidator<DeleteTransferCommand>
{
    public DeleteTransferValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers null when the user has no transfer with that id, or already deleted it.</summary>
public sealed class DeleteTransferHandler(
    IValidator<DeleteTransferCommand> validator, ITransactionRepository transactions, IAuditLog auditLog, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<DeleteTransferCommand, DeleteOutcome?>(validator)
{
    /// <summary>A delete names no row version, so one that meets a change made at the same moment is made again on what was saved.</summary>
    protected override Task<Result<DeleteOutcome?>> ExecuteAsync(DeleteTransferCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<DeleteOutcome?>>(async () =>
        {
            var transfer = await transactions.FindTransferAsync(command.Id, deletedToo: false, cancellationToken);
            if (transfer is null)
            {
                return (DeleteOutcome?)null;
            }

            if (transfer.IsReconciled)
            {
                return DeleteOutcome.Reconciled;
            }

            transfer.Delete(clock.GetUtcNow());
            await auditLog.TransferDeletedAsync(command.Id, transfer.OutLeg.Amount, transfer.OutLeg.Currency, transfer.OutLeg.Date, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return DeleteOutcome.Deleted;
        });
}
