using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>Ticks a transaction the statement shows. Ticking one already ticked changes nothing.</summary>
public sealed record TickTransactionCommand(Guid ReconciliationId, Guid TransactionId);

public sealed class TickTransactionValidator : AbstractValidator<TickTransactionCommand>
{
    public TickTransactionValidator()
    {
        RuleFor(command => command.ReconciliationId).Required();
        RuleFor(command => command.TransactionId).Required();
    }
}

/// <summary>
/// Answers the reconciliation with its difference; null when the user has no such reconciliation or
/// transaction. A completed reconciliation changes nothing until it is reopened.
/// </summary>
public sealed class TickTransactionHandler(
    IValidator<TickTransactionCommand> validator, IReconciliationRepository reconciliations, ITransactionRepository transactions,
    ReconciliationContext context, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<TickTransactionCommand, TickOutcome?>(validator)
{
    /// <summary>
    /// Saving it moves the reconciliation's row version on, so a completion at the same moment cannot miss it: one
    /// of the two is made again on what the other saved.
    /// </summary>
    protected override Task<Result<TickOutcome?>> ExecuteAsync(TickTransactionCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<TickOutcome?>>(async () =>
        {
            if (await reconciliations.FindAsync(command.ReconciliationId, cancellationToken) is not { } reconciliation
                || await transactions.FindAsync(command.TransactionId, cancellationToken) is not { } transaction)
            {
                return (TickOutcome?)null;
            }

            if (reconciliation.IsCompleted)
            {
                return TickOutcome.Completed;
            }

            try
            {
                reconciliation.Tick(transaction, clock.GetUtcNow());
            }
            catch (ReconciliationRuleViolation violation)
            {
                return Result<TickOutcome?>.Invalid(FieldError.On(nameof(TickTransactionCommand.TransactionId), violation.Message));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new TickOutcome(await context.DetailsAsync(reconciliation, cancellationToken));
        });
}
