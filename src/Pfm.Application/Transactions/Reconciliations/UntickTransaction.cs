using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>Unticks a transaction. Unticking one not ticked changes nothing.</summary>
public sealed record UntickTransactionCommand(Guid ReconciliationId, Guid TransactionId);

public sealed class UntickTransactionValidator : AbstractValidator<UntickTransactionCommand>
{
    public UntickTransactionValidator()
    {
        RuleFor(command => command.ReconciliationId).Required();
        RuleFor(command => command.TransactionId).Required();
    }
}

/// <summary>
/// Answers the reconciliation with its difference; null when the user has no such reconciliation or
/// transaction. A completed reconciliation changes nothing until it is reopened.
/// </summary>
public sealed class UntickTransactionHandler(
    IValidator<UntickTransactionCommand> validator, IReconciliationRepository reconciliations, ITransactionRepository transactions,
    ReconciliationContext context, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<UntickTransactionCommand, TickOutcome?>(validator)
{
    /// <summary>
    /// Saving it moves the reconciliation's row version on, so a completion at the same moment cannot miss it: one
    /// of the two is made again on what the other saved.
    /// </summary>
    protected override Task<Result<TickOutcome?>> ExecuteAsync(UntickTransactionCommand command, CancellationToken cancellationToken) =>
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
                reconciliation.Untick(transaction, clock.GetUtcNow());
            }
            catch (ReconciliationRuleViolation violation)
            {
                return Result<TickOutcome?>.Invalid(FieldError.On(nameof(UntickTransactionCommand.TransactionId), violation.Message));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new TickOutcome(await context.DetailsAsync(reconciliation, cancellationToken));
        });
}
