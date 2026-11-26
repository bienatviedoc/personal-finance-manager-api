using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>
/// Cancels a reconciliation in progress, entered against the wrong statement for instance: whatever is ticked
/// in it is unticked and it is removed. A completed one is reopened first.
/// </summary>
public sealed record CancelReconciliationCommand(Guid Id);

public sealed class CancelReconciliationValidator : AbstractValidator<CancelReconciliationCommand>
{
    public CancelReconciliationValidator() => RuleFor(command => command.Id).Required();
}

public enum CancelReconciliationOutcome
{
    Cancelled,

    /// <summary>The reconciliation is completed; nothing changed.</summary>
    Completed,
}

/// <summary>Answers null when the user has no reconciliation with that id.</summary>
public sealed class CancelReconciliationHandler(
    IValidator<CancelReconciliationCommand> validator, IReconciliationRepository reconciliations, ITransactionRepository transactions,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CancelReconciliationCommand, CancelReconciliationOutcome?>(validator)
{
    protected override Task<Result<CancelReconciliationOutcome?>> ExecuteAsync(CancelReconciliationCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<CancelReconciliationOutcome?>>(async () =>
        {
            if (await reconciliations.FindAsync(command.Id, cancellationToken) is not { } reconciliation)
            {
                return (CancelReconciliationOutcome?)null;
            }

            if (reconciliation.IsCompleted)
            {
                return CancelReconciliationOutcome.Completed;
            }

            foreach (var transaction in await transactions.ListTickedAsync(reconciliation.Id, cancellationToken))
            {
                reconciliation.Untick(transaction, clock.GetUtcNow());
            }

            reconciliations.Remove(reconciliation);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return CancelReconciliationOutcome.Cancelled;
        });
}
