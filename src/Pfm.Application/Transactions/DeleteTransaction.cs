using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions;

/// <summary>Deletes a transaction, softly: it leaves lists and balances at once and stays recoverable (spec section 3, rule 10).</summary>
public sealed record DeleteTransactionCommand(Guid Id);

public sealed class DeleteTransactionValidator : AbstractValidator<DeleteTransactionCommand>
{
    public DeleteTransactionValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers null when the user has no transaction with that id, or already deleted it.</summary>
public sealed class DeleteTransactionHandler(
    IValidator<DeleteTransactionCommand> validator, ITransactionRepository transactions, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<DeleteTransactionCommand, Done?>(validator)
{
    /// <summary>A delete names no row version, so one that meets a change made at the same moment is tried again on what was saved.</summary>
    private const int Attempts = 3;

    protected override async Task<Result<Done?>> ExecuteAsync(DeleteTransactionCommand command, CancellationToken cancellationToken)
    {
        var transaction = await transactions.FindAsync(command.Id, cancellationToken);
        if (transaction is null)
        {
            return (Done?)null;
        }

        for (var attempt = 1; ; attempt++)
        {
            transaction.Delete(clock.GetUtcNow());
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new Done();
            }
            catch (ConcurrentChangeException) when (attempt < Attempts)
            {
                // Edited, or deleted, by another request in between. Deleting what is deleted changes nothing.
                await transactions.RefreshAsync(transaction, cancellationToken);
            }
        }
    }
}
