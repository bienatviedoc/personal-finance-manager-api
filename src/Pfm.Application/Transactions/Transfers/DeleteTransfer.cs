using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Transfers;

/// <summary>
/// Deletes a transfer, softly and both legs in one save: they leave lists and both accounts' balances at
/// once and stay recoverable (spec section 3, rules 3 and 10).
/// </summary>
/// <param name="Id">The transfer id its two legs share.</param>
public sealed record DeleteTransferCommand(Guid Id);

public sealed class DeleteTransferValidator : AbstractValidator<DeleteTransferCommand>
{
    public DeleteTransferValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers null when the user has no transfer with that id, or already deleted it.</summary>
public sealed class DeleteTransferHandler(
    IValidator<DeleteTransferCommand> validator, ITransactionRepository transactions, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<DeleteTransferCommand, Done?>(validator)
{
    /// <summary>A delete names no row version, so one that meets a change made at the same moment is made again on what was saved.</summary>
    protected override Task<Result<Done?>> ExecuteAsync(DeleteTransferCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<Done?>>(async () =>
        {
            var transfer = await transactions.FindTransferAsync(command.Id, deletedToo: false, cancellationToken);
            if (transfer is null)
            {
                return (Done?)null;
            }

            transfer.Delete(clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new Done();
        });
}
