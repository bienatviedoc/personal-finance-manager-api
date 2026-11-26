using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>What ticking and unticking both send: which transaction, in which reconciliation.</summary>
public interface ITickCommand
{
    Guid ReconciliationId { get; }

    Guid TransactionId { get; }
}

/// <summary>
/// Ticking and unticking, but for what each does to the reconciliation. Answers the reconciliation with its
/// difference; null when the user has no such reconciliation or transaction. A completed reconciliation changes
/// nothing until it is reopened.
/// </summary>
public abstract class TickHandler<TCommand>(
    IValidator<TCommand> validator, IReconciliationRepository reconciliations, ITransactionRepository transactions,
    ReconciliationContext context, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<TCommand, TickOutcome?>(validator)
    where TCommand : ITickCommand
{
    /// <summary>
    /// Saving it moves the reconciliation's row version on, so a completion at the same moment cannot miss it: one
    /// of the two is made again on what the other saved.
    /// </summary>
    protected override Task<Result<TickOutcome?>> ExecuteAsync(TCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<TickOutcome?>>(async () =>
        {
            if (await reconciliations.FindAsync(command.ReconciliationId, cancellationToken) is not { } reconciliation
                || await transactions.FindAsync(command.TransactionId, cancellationToken) is not { } transaction)
            {
                return (TickOutcome?)null;
            }

            try
            {
                Change(reconciliation, transaction, clock.GetUtcNow());
            }
            catch (ReconciliationRuleViolation violation) when (violation.Property == nameof(Reconciliation.CompletedAt))
            {
                return TickOutcome.Completed;
            }
            catch (ReconciliationRuleViolation violation)
            {
                return Result<TickOutcome?>.Invalid(FieldError.On(nameof(ITickCommand.TransactionId), violation.Message));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new TickOutcome(await context.DetailsAsync(reconciliation, cancellationToken));
        });

    /// <summary>What the use case does to the reconciliation.</summary>
    /// <exception cref="ReconciliationRuleViolation">The domain refuses it.</exception>
    protected abstract void Change(Reconciliation reconciliation, Transaction transaction, DateTimeOffset now);
}
